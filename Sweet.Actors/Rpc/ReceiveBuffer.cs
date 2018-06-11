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
                        FrameCache.Release(frames[i].FrameData);
                }
            }
        }

        /* private void TryParse()
        {
            var stream = _stream;

            var streamLen = stream?.Length ?? 0;
            var positionSnapshot = stream?.Position ?? 0;

            var bufferSize = streamLen - positionSnapshot;
            if (bufferSize >= RpcConstants.HeaderSize)
            {
                stream.Read(_mainHeaderBuffer, 0, RpcConstants.HeaderSize);

                if (_mainHeaderBuffer[0] != RpcConstants.HeaderSign)
                    throw new Exception(Errors.InvalidMessageType);

                var headerIndex = 1;

                var receivedMsg = new ReceivedMessage();

                receivedMsg.Header.ProcessId = _mainHeaderBuffer.ToInt(headerIndex);
                headerIndex += 4;

                receivedMsg.Header.MessageId = _mainHeaderBuffer.ToUShort(headerIndex);
                headerIndex += 2;

                receivedMsg.Header.SerializerKey = 
                    Encoding.UTF8.GetString(_mainHeaderBuffer, headerIndex, RpcConstants.SerializerRegistryNameLength);
                headerIndex += 2;

                var frameCount = (receivedMsg.Header.FrameCount = _mainHeaderBuffer.ToUShort(headerIndex));
                
                var completed = frameCount == 0;
                if (!completed)
                {
                    bufferSize -= RpcConstants.HeaderSize;

                    for (var i = (ushort)0; i < frameCount; i++)
                    {
                        if (bufferSize >= RpcConstants.FrameHeaderSize)
                        {
                            stream.Read(_frameHeaderBuffer, 0, RpcConstants.FrameHeaderSize);

                            if (_frameHeaderBuffer[0] != RpcConstants.FrameSign)
                                throw new Exception(Errors.InvalidMessageType);

                            var frame = new ReceivedFrame();
                        }
                    }
                }

                if (!completed)
                    stream.Position = positionSnapshot;
                else {
                    _messageQueue.Enqueue(receivedMsg);
                }
            }
        } */

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

                    var headerIndex = 1;

                    var receivedMsg = new ReceivedMessage();

                    receivedMsg.Header.ProcessId = _mainHeaderBuffer.ToInt(headerIndex);
                    headerIndex += 4;

                    receivedMsg.Header.MessageId = _mainHeaderBuffer.ToUShort(headerIndex);
                    headerIndex += 2;

                    receivedMsg.Header.SerializerKey = 
                        Encoding.UTF8.GetString(_mainHeaderBuffer, headerIndex, RpcConstants.SerializerRegistryNameLength);
                    headerIndex += 2;

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

                                headerIndex = 1;

                                frame.ProcessId = _mainHeaderBuffer.ToInt(headerIndex);
                                headerIndex += 4;

                                frame.MessageId = _mainHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += 2;

                                if (frame.ProcessId != receivedMsg.Header.ProcessId ||
                                    frame.MessageId != receivedMsg.Header.MessageId)
                                    throw new Exception(RpcErrors.InvalidMessage);

                                frame.FrameId = _mainHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += 2;

                                var frameDataLen = _mainHeaderBuffer.ToUShort(headerIndex);
                                headerIndex += 2;
                                
                                if (frameDataLen > 0)
                                {
                                    if (bufferedLen < frameDataLen)
                                        break;
                                    
                                    var dataBuffer = FrameCache.Acquire();

                                    var readLen = reader.Read(dataBuffer, 0, frameDataLen);
                                    if (readLen < frameDataLen)
                                        break;

                                    frame.FrameData = dataBuffer;
                                }

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

		public bool TryGetMessage(out (IMessage, Address) msg)
		{
			msg = (Message.Empty, Address.Unknown);

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

                            var serializerKey = receivedMsg.Header.SerializerKey?.Trim();
                            if (String.IsNullOrEmpty(serializerKey))
                                serializerKey = "default";

                            if (serializerKey == _serializerKey)
                                serializer = _serializer;
                            else _serializer = (serializer = RpcSerializerRegistry.Get(serializerKey));
                            
                            if (serializer == null)
                                throw new Exception(RpcErrors.InvalidSerializerKey);

                            var dataList = new List<byte[]>(framesCnt);
                            foreach (var frame in frames)
                                dataList.Add(frame.FrameData);

                            using (var stream = new ChunkedStream(dataList, false))
                            {
                                msg = serializer.Deserialize(stream);

                                if (msg.Item1 != null && msg.Item1 != Message.Empty &&
                                    msg.Item2 != Address.Unknown)
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
