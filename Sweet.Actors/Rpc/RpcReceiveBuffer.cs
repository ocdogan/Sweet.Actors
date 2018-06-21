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

namespace Sweet.Actors
{
    internal class RpcReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        public static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        private ConcurrentQueue<RpcPartitionedMessage> _messageQueue = new ConcurrentQueue<RpcPartitionedMessage>();

        private ChunkedStream _stream;
        private RpcMessageParser _parser;

        private string _serializerKey;
        private IWireSerializer _serializer;

        public RpcReceiveBuffer()
        {
           _stream = new ChunkedStream();
           _parser = new RpcMessageParser();
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

        public bool OnReceiveData(byte[] buffer, int offset, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                _stream.Write(buffer, offset, bytesTransferred);

                var parsed = false;
                while (_parser.TryParse(_stream, out RpcPartitionedMessage message))
                {
                    _messageQueue.Enqueue(message);
                    parsed = true;
                }
                return parsed;
            }
            return false;
        }

		private void ReleaseMessages(ConcurrentQueue<RpcPartitionedMessage> queue)
		{
            if (queue != null)
                while (queue.TryDequeue(out RpcPartitionedMessage rpcMessage))
                    ReleaseFrames(rpcMessage);
        }

        private static void ReleaseFrames(RpcPartitionedMessage rpcMessage)
        {
            if (rpcMessage != null)
            {
                var frames = rpcMessage.Frames;
                if (frames != null)
                {
                    var frameCnt = frames.Count;
                    for (var i = 0; i < frameCnt; i++)
                        FrameCache.Release(frames[i].Data);
                }
            }
        }

		public bool TryGetMessage(out RemoteMessage remoteMessage)
		{
			remoteMessage = null;
            if (!Disposed &&
                _messageQueue.TryDequeue(out RpcPartitionedMessage rpcMessage))
            {
                try
                {
                    var rpcMessageFrames = rpcMessage.Frames;
                    if (rpcMessageFrames != null)
                    {
                        var rpcMessageFrameCnt = rpcMessageFrames.Count;
                        if (rpcMessageFrameCnt > 0)
                        {
                            var serializer = GetSerializer(rpcMessage);
                            if (serializer == null)
                                throw new Exception(RpcErrors.InvalidSerializerKey);

                            var frameDataList = new List<ArraySegment<byte>>(rpcMessageFrameCnt);
                            foreach (var rpcMessageFrame in rpcMessageFrames)
                            {
                                var data = rpcMessageFrame.Data;
                                if (data != null)
                                {
                                    var dataLen = data.Length;
                                    if (dataLen > 0)
                                    {
                                        var frameDataLen = rpcMessageFrame.DataLength;

                                        frameDataList.Add((frameDataLen == dataLen) ?
                                            new ArraySegment<byte>(data) :
                                            new ArraySegment<byte>(data, 0, frameDataLen));
                                    }
                                }
                            }

                            using (var stream = new ChunkedStream(frameDataList, false))
                            {
                                remoteMessage = serializer.Deserialize(stream);

                                if (remoteMessage.Message != null && remoteMessage.Message != Message.Empty &&
                                    remoteMessage.To != Aid.Unknown)
                                    return true;
                            }
                        }
                    }
                }
                finally
                {
                    ReleaseFrames(rpcMessage);
                }
            };
			return false;
        }

        private IWireSerializer GetSerializer(RpcPartitionedMessage rpcMessage)
        {
            IWireSerializer serializer = null;

            var serializerKey = rpcMessage.Header.SerializerKey;
            if (String.IsNullOrEmpty(serializerKey))
                serializerKey = Constants.DefaultSerializerKey;

            if (serializerKey == _serializerKey)
                serializer = _serializer;
            else
            {
                _serializer = (serializer = RpcSerializerRegistry.Get(serializerKey));
                _serializerKey = serializerKey;
            }

            return serializer;
        }
    }
}
