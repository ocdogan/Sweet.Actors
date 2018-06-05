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

namespace Sweet.Actors
{
    public sealed class BufferCache : ObjectCacheBase<BufferSegment>
    {
        public static readonly BufferCache Default = new BufferCache(10);

        private const int MinSegmentSize = 512;
        private const int DefaultSegmentSize = Constants.FrameSize;

        private int _segmentSize;

        public BufferCache(int initialCount = 0, int limit = DefaultLimit, int segmentSize = DefaultSegmentSize)
            : base(SegmentProvider, 0, limit)
        {
            if (segmentSize < 1)
                _segmentSize = DefaultSegmentSize;
            else _segmentSize = Math.Max(MinSegmentSize, segmentSize);

            initialCount = Math.Max(0, initialCount);
            if (initialCount > 0)
                for (var i = 0; i < initialCount; i++)
                    Enqueue(SegmentProvider(this));
        }

        public int SegmentSize => _segmentSize;

        private static BufferSegment SegmentProvider(ObjectCacheBase<BufferSegment> owner)
        {
            return new BufferSegment(((BufferCache)owner)._segmentSize);
        }

        protected override void OnDispose(BufferSegment item)
        {
            item.Dispose();
        }

        protected override void OnEnqueue(BufferSegment item)
        {
            item.Reset();
        }
    }
}
