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
using System.Net.Sockets;

namespace Sweet.Actors
{
    public class SocketAsyncEventArgsCache : ObjectCacheBase<SocketAsyncEventArgs>
    {
        public static readonly SocketAsyncEventArgsCache Default = new SocketAsyncEventArgsCache(16, 100);

        private const int MinSegmentSize = 512;
        private const int DefaultSegmentSize = 4 * Constants.KB;

        private int _segmentSize = DefaultSegmentSize;

        internal SocketAsyncEventArgsCache(int initialCount = 0, int limit = DefaultLimit, int segmentSize = DefaultSegmentSize)
            : base(CreateSocketAsyncEventArgs, 0, limit)
        {
            if (segmentSize < 1)
                _segmentSize = DefaultSegmentSize;
            else _segmentSize = Math.Max(MinSegmentSize, segmentSize);

            initialCount = Math.Max(0, initialCount);
            if (initialCount > 0)
                for (var i = 0; i < initialCount; i++)
                    Enqueue(CreateSocketAsyncEventArgs(this));
        }

        private static SocketAsyncEventArgs CreateSocketAsyncEventArgs(ObjectCacheBase<SocketAsyncEventArgs> owner)
        {
            var result = new SocketAsyncEventArgs();
            var buffer = new byte[((SocketAsyncEventArgsCache)owner)._segmentSize];
            result.SetBuffer(buffer, 0, buffer.Length);
        
            return result;
        }

        protected override void OnDispose(SocketAsyncEventArgs item)
        {
            if (item != null)
                item.Dispose();
        }

        protected override void OnEnqueue(SocketAsyncEventArgs item)
        {
            item.UserToken = null;
            item.AcceptSocket = null;
            item.SocketError = SocketError.Success;
        }
    }
}
