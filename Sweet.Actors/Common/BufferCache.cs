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

namespace Sweet.Actors
{
    public class BufferCache
    {
		private const int MinSegmentSize = 512;
        private const int DefaultSegmentSize = 4 * 1024;

		private const int MinLimit = 1000;
		private const int DefaultLimit = 1000;
        
		public static readonly BufferCache Default = new BufferCache(10);

		private int _limit = -1;
		private int _segmentSize;
		private readonly ConcurrentQueue<BufferSegment> _cache = new ConcurrentQueue<BufferSegment>();

		public BufferCache(int initialCount = 0, int limit = DefaultLimit, int segmentSize = DefaultSegmentSize)
        {
			if (limit > 0)
				_limit = Math.Max(MinLimit, limit);

			if (segmentSize < 1)
				_segmentSize = DefaultSegmentSize;
			else _segmentSize = Math.Max(MinSegmentSize, segmentSize);

			initialCount = Math.Max(0, initialCount);
			if (initialCount > 0)
				for (var i = 0; i < initialCount; i++)
					_cache.Enqueue(new BufferSegment(_segmentSize));
        }

		public BufferSegment Acquire()
		{
			BufferSegment result;
			if (!_cache.TryDequeue(out result))
				return new BufferSegment(_segmentSize);
			return result;
		}

		public void Release(BufferSegment segment)
		{
			if (segment != null)
			{
				if ((_limit < 1) || (_cache.Count >= _limit))
					segment.Dispose();
				else
				{
					segment.Reset();
					_cache.Enqueue(segment);
				}
			}
		}

		public void Release(IEnumerable<BufferSegment> segments)
        {
            if (segments != null)
				foreach (var segment in segments)
					Release(segment);
        }
    }
}
