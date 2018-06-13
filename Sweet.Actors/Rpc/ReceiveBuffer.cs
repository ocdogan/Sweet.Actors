#region License
//  The MIT License (MIT)
//
//  Copyright (c) 2017, Cagatay Dogan
//
//  Permission is hereby granted, free of charge, to any person obtaining a copy
//  of this software and associated documentation files (the "Software"), to deal
//  in the Software without restriction, including without limitation the rights
//  to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
//  copies of the Software, and to permit persons to whom the Software is
//  furnished to do so, subject to the following conditions:
//
//      The above copyright notice and this permission notice shall be included in
//      all copies or substantial portions of the Software.
//
//      THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
//      IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
//      FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
//      AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
//      LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
//      OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
//      THE SOFTWARE.
#endregion License

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Text;

// using Microsoft.IO;

namespace Sweet.Actors
{
    internal class ReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        public static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        /* private static readonly RecyclableMemoryStreamManager StreamManager = 
            new RecyclableMemoryStreamManager(BlockSize, LargeBufferMultiple, MaximumBufferSize); */

        private ConcurrentQueue<ReceivedMessage> _messageQueue = new ConcurrentQueue<ReceivedMessage>();

        private byte[] _mainHeaderBuffer = new byte[RpcConstants.HeaderSize];
        private byte[] _frameHeaderBuffer = new byte[RpcConstants.FrameHeaderSize];

        private ChunkedStream _stream;
        // private RecyclableMemoryStream _stream;

        private string _serializerKey;
        private IRpcSerializer _serializer;

        public ReceiveBuffer()
        {
           //  _stream = new RecyclableMemoryStream(StreamManager);
           _stream = new ChunkedStream();
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                var queue = Interlocked.Exchange(ref _messageQueue, null);
                ReleaseMessages(queue);

                using (Interlocked.Exchange(ref _stream, null))
                { }
            }
        }

        public bool OnReceiveData(byte[] buffer, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                _stream.Write(buffer, 0, bytesTransferred);
                return TryParse();
            }
            return false;
        }

		private void ReleaseMessages(ConcurrentQueue<ReceivedMessage> queue)
		{
            if (queue != null)
                while (queue.TryDequeue(out ReceivedMessage receivedMsg))
                    ReleaseFrames(receivedMsg);
        }

        private static void ReleaseFrames(ReceivedMessage receivedMsg)
        {
            if (receivedMsg != null)
            {
                var frames = receivedMsg.Frames;
                if (frames != null)
                {
                    var frameCnt = frames.Count;
                    for (var i = 0; i < frameCnt; i++)
                        FrameCache.Release(frames[i].Data);
                }
            }
        }

        private bool TryParse()
        {
            var stream = _stream;
            var bufferedLen = stream?.Position ?? 0;

            if (bufferedLen >= RpcConstants.HeaderSize)
            {
                using (var reader = stream.NewReader())
                {
                    reader.Read(_mainHeaderBuffer, 0, RpcConstants.HeaderSize);

                    if (_mainHeaderBuffer[0] != RpcConstants.HeaderSign)
                        throw new Exception(Errors.InvalidMessageType);

                    var headerIndex = sizeof(byte);

                    var receivedMsg = new ReceivedMessage();

                    receivedMsg.Header.ProcessId = _mainHeaderBuffer.ToInt(headerIndex);
                    headerIndex += sizeof(int);

                    receivedMsg.Header.MessageId = _mainHeaderBuffer.ToInt(headerIndex);
                    headerIndex += sizeof(int);

                    var serializerKey =
                        Encoding.UTF8.GetString(_mainHeaderBuffer, headerIndex, RpcConstants.SerializerRegistryNameLength)?.TrimEnd();
                    headerIndex += RpcConstants.SerializerRegistryNameLength;

                    if (serializerKey != null)
                    {
                        var pos = serializerKey.IndexOf('\0');
                        if (pos > -1)
                            serializerKey = serializerKey.Substring(0, pos);
                    }

                    if (String.IsNullOrEmpty(serializerKey))
                        serializerKey = Constants.DefaultSerializerKey;

                    receivedMsg.Header.SerializerKey = serializerKey;

                    var frameCount = (receivedMsg.Header.FrameCount = _mainHeaderBuffer.ToUShort(headerIndex));
                    
                    var completed = frameCount == 0;
                    if (!completed)
                    {
                        bufferedLen -= RpcConstants.HeaderSize;

                        if (bufferedLen < RpcConstants.FrameHeaderSize + (frameCount - 1)*RpcConstants.FrameSize)
                            return false;

                        try
                        {
                            for (var i = (ushort)0; i < frameCount; i++)
                            {
                                if (bufferedLen < RpcConstants.FrameHeaderSize)
                                    break;

                                reader.Read(_frameHeaderBuffer, 0, RpcConstants.FrameHeaderSize);
                                bufferedLen -= RpcConstants.FrameHeaderSize;

                                if (_frameHeaderBuffer[0] != RpcConstants.FrameSign)
                                    throw new Exception(Errors.InvalidMessageType);

                                var frame = new ReceivedFrame();

                                headerIndex = sizeof(byte);

                                frame.ProcessId = _frameHeaderBuffer.ToInt(headerIndex);
                                headerIndex += sizeof(int);

                                frame.MessageId = _frameHeaderBuffer.ToInt(headerIndex);
                                headerIndex += sizeof(int);

                                if (frame.ProcessId != receivedMsg.Header.ProcessId ||
                                    frame.MessageId != receivedMsg.Header.MessageId)
                                    throw new Exception(RpcErrors.InvalidMessage);

                                frame.FrameId = _frameHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += sizeof(ushort);

                                var frameDataLen = _frameHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += sizeof(ushort);
                                
                                if (frameDataLen > 0)
                                {
                                    if (bufferedLen < frameDataLen)
                                        break;

                                    bufferedLen -= frameDataLen;
                                    var dataBuffer = FrameCache.Acquire();

                                    var readLen = reader.Read(dataBuffer, 0, frameDataLen);
                                    if (readLen < frameDataLen)
                                        break;

                                    frame.Data = dataBuffer;
                                    frame.DataLength = frameDataLen;
                                }

                                receivedMsg.Frames.Add(frame);

                                if (i == frameCount-1)
                                    completed = true;
                            }
                        }
                        catch (Exception)
                        {
                            ReleaseFrames(receivedMsg);
                            throw;
                        }
                    }

                    if (completed)
                    {
                        _messageQueue.Enqueue(receivedMsg);
                        return true;
                    }
                }
            }
            return false;
        }

		public bool TryGetMessage(out (IMessage, Pid) msg)
		{
			msg = (Message.Empty, Pid.Unknown);

            if (!Disposed &&
                _messageQueue.TryDequeue(out ReceivedMessage receivedMsg))
            {
                try
                {
                    var frames = receivedMsg.Frames;
                    if (frames != null)
                    {
                        var framesCnt = frames.Count;
                        if (framesCnt > 0)
                        {
                            IRpcSerializer serializer = null;

                            var serializerKey = receivedMsg.Header.SerializerKey;
                            if (String.IsNullOrEmpty(serializerKey))
                                serializerKey = Constants.DefaultSerializerKey;

                            if (serializerKey == _serializerKey)
                                serializer = _serializer;
                            else
                            {
                                _serializerKey = serializerKey;
                                _serializer = (serializer = RpcSerializerRegistry.Get(serializerKey));
                            }
                            
                            if (serializer == null)
                                throw new Exception(RpcErrors.InvalidSerializerKey);

                            var dataList = new List<ArraySegment<byte>>(framesCnt);
                            foreach (var frame in frames)
                            {
                                var frameData = frame.Data;
                                if (frameData != null)
                                {
                                    if (frame.DataLength == frameData.Length)
                                        dataList.Add(new ArraySegment<byte>(frameData));
                                    else
                                        dataList.Add(new ArraySegment<byte>(frameData, 0, frame.DataLength));
                                }
                            }

                            using (var stream = new ChunkedStream(dataList, false))
                            {
                                msg = serializer.Deserialize(stream);

                                if (msg.Item1 != null && msg.Item1 != Message.Empty &&
                                    msg.Item2 != Pid.Unknown)
                                    return true;
                            }
                        }
                    }
                }
                finally
                {
                    ReleaseFrames(receivedMsg);
                }
            };
			return false;
        }
    }
}
