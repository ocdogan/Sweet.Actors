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
using System.Threading;
using System.Text;

using Microsoft.IO;

namespace Sweet.Actors
{
    internal class ReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        private static readonly RecyclableMemoryStreamManager StreamManager = 
            new RecyclableMemoryStreamManager(BlockSize, LargeBufferMultiple, MaximumBufferSize);

        private ConcurrentQueue<ReceivedMessage> _messageQueue = new ConcurrentQueue<ReceivedMessage>();

        private byte[] _mainHeaderBuffer = new byte[RpcConstants.HeaderSize];
        private byte[] _frameHeaderBuffer = new byte[RpcConstants.FrameHeaderSize];

        private RecyclableMemoryStream _stream;

        public ReceiveBuffer()
        {
            _stream = new RecyclableMemoryStream(StreamManager);
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _messageQueue, null);
            using (Interlocked.Exchange(ref _stream, null))
            { }
        }

        public void OnReceiveData(byte[] buffer, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                _stream.Write(buffer, 0, bytesTransferred);
                TryParse();
            }
        }

        private void TryParse()
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
        }

		public bool TryGetMessage(out ReceivedMessage msg)
		{
			msg = null;
            if (!Disposed)
                return _messageQueue.TryDequeue(out msg);
			return false;
        }
    }
}
