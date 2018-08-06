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
using System.Collections;
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

        public static readonly ByteArrayCache FrameCache = new ByteArrayCache(10, -1, RpcMessageSizeOf.EachFrameData);

        private long _count;
        private ConcurrentQueue<WireMessage> _messageQueue = new ConcurrentQueue<WireMessage>();

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
                stream?.Dispose();
            }
        }

        public bool OnReceiveData(byte[] buffer, int offset, int bytesReceived)
		{
            var parsed = false;
            if ((buffer != null) && (bytesReceived > 0))
            {
                _stream.Write(buffer, offset, bytesReceived);

                while (RpcMessageParser.TryParse(_stream, out IEnumerable<WireMessage> messages))
                {
                    if (messages != null)
                    {
                        foreach (var message in messages)
                        {
                            if (message != null)
                            {
                                _messageQueue.Enqueue(message);
                                Interlocked.Add(ref _count, 1L);

                                parsed = true;
                            }
                        }
                    }
                }
            }
            return parsed;
        }

        public bool TryGetMessage(out WireMessage message)
		{
			message = null;
            if (!Disposed && _messageQueue.TryDequeue(out message))
            {
                Interlocked.Add(ref _count, -1L);
                return true;
            }
            return false;
        }

        public bool TryGetMessage(int count, out IList messages)
        {
            messages = null;
            if (!Disposed)
            {
                var queueSize = (int)Interlocked.Read(ref _count);
                if (queueSize > 0)
                {
                    count = Math.Min(Math.Min(RpcConstants.MaxBulkMessageLength, Math.Max(1, count)), queueSize);

                    WireMessage message;
                    if (count == 1)
                    {
                        if (_messageQueue.TryDequeue(out message))
                        {
                            Interlocked.Add(ref _count, -1L);

                            messages = new WireMessage[] { message };
                            return true;
                        }
                        return false;
                    }

                    var list = new List<WireMessage>(count);

                    for (var i = 0; i < count; i++)
                    {
                        if (!_messageQueue.TryDequeue(out message))
                            break;

                        Interlocked.Add(ref _count, -1L);
                        list.Add(message);
                    }

                    if (list.Count > 0)
                    {
                        messages = list;
                        return true;
                    }
                }
            }
            return false;
        }
    }
}
