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
    internal class RpcReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        public static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        /* private static readonly RecyclableMemoryStreamManager StreamManager = 
            new RecyclableMemoryStreamManager(BlockSize, LargeBufferMultiple, MaximumBufferSize); */

        private ConcurrentQueue<RpcPartitionedMessage> _messageQueue = new ConcurrentQueue<RpcPartitionedMessage>();

        private ChunkedStream _stream;
        private RpcMessageParser _parser;
        // private RecyclableMemoryStream _stream;

        private string _serializerKey;
        private IWireSerializer _serializer;

        public RpcReceiveBuffer()
        {
           //  _stream = new RecyclableMemoryStream(StreamManager);
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

        public bool OnReceiveData(byte[] buffer, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                _stream.Write(buffer, 0, bytesTransferred);
                if (_parser.TryParse(_stream, out RpcPartitionedMessage message))
                {
                    _messageQueue.Enqueue(message);
                    return true;
                }
            }
            return false;
        }

		private void ReleaseMessages(ConcurrentQueue<RpcPartitionedMessage> queue)
		{
            if (queue != null)
                while (queue.TryDequeue(out RpcPartitionedMessage receivedMsg))
                    ReleaseFrames(receivedMsg);
        }

        private static void ReleaseFrames(RpcPartitionedMessage receivedMsg)
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

		public bool TryGetMessage(out RemoteMessage message)
		{
			message = null;
            if (!Disposed &&
                _messageQueue.TryDequeue(out RpcPartitionedMessage receivedMsg))
            {
                try
                {
                    var frames = receivedMsg.Frames;
                    if (frames != null)
                    {
                        var framesCnt = frames.Count;
                        if (framesCnt > 0)
                        {
                            var serializer = GetSerializer(receivedMsg);
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
                                message = serializer.Deserialize(stream);

                                if (message.Message != null && message.Message != Message.Empty &&
                                    message.To != Aid.Unknown)
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

        private IWireSerializer GetSerializer(RpcPartitionedMessage message)
        {
            IWireSerializer serializer = null;

            var serializerKey = message.Header.SerializerKey;
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
