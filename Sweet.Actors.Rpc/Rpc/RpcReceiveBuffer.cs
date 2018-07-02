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

namespace Sweet.Actors.Rpc
{
    internal class RpcReceiveBuffer : Disposable
    {
        private const int BlockSize = 16 * Constants.KB;
        private const int LargeBufferMultiple = 1 << 20;
        private const int MaximumBufferSize = 8 * (1 << 20);

        public static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcConstants.FrameDataSize);

        private ConcurrentQueue<RemoteMessage> _messageQueue = new ConcurrentQueue<RemoteMessage>();

        private ChunkedStream _stream;

        public RpcReceiveBuffer()
        {
           _stream = new ChunkedStream();
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                var queue = Interlocked.Exchange(ref _messageQueue, null);

                var stream = Interlocked.Exchange(ref _stream, null);
                if (stream != null)
                {
                    AsyncEventPool.Run(stream.Dispose);
                }
            }
        }

        public bool OnReceiveData(byte[] buffer, int offset, int bytesTransferred)
		{
            if ((buffer != null) && (bytesTransferred > 0))
            {
                _stream.Write(buffer, offset, bytesTransferred);

                var parsed = false;
                while (RpcMessageParser.TryParse(_stream, out RemoteMessage message))
                {
                    if (message != null)
                    {
                        _messageQueue.Enqueue(message);
                        parsed = true;
                    }
                }
                return parsed;
            }
            return false;
        }

		public bool TryGetMessage(out RemoteMessage message)
		{
			message = null;
            if (!Disposed && _messageQueue.TryDequeue(out message))
                return true;
            return false;
        }
    }
}
