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
    public abstract class ObjectCacheBase<T> : Disposable
    {
        protected const int MinLimit = 64;
        protected const int DefaultLimit = 1024;

        private static readonly T[] EmptyArray = new T[0];

        private int _limit = -1;
        private readonly Func<ObjectCacheBase<T>, T> _provider;

        private ConcurrentQueue<T> _cache = new ConcurrentQueue<T>();

        public ObjectCacheBase(Func<ObjectCacheBase<T>, T> provider, int initialCount = 0, int limit = DefaultLimit)
        {
            _provider = provider ?? throw new ArgumentNullException(nameof(provider));

            if (limit > 0)
                _limit = Math.Max(MinLimit, limit);

            initialCount = Math.Max(0, initialCount);
            if (initialCount > 0)
                for (var i = 0; i < initialCount; i++)
                    Enqueue(_provider(this));
        }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
                Clear();
        }

        protected void Enqueue(T item)
        {
            OnEnqueue(item);
            _cache.Enqueue(item);
        }

        public T Acquire()
        {
            if (!_cache.TryDequeue(out T result))
                return _provider(this);
            return result;
        }

        public T[] Acquire(int count)
        {
            if (count == 1)
                return new T[] { Acquire() };

            if (count > 0)
            {
                var result = new T[count];
                for (var i = 0; i < count; i++)
                    result[i] = Acquire();
                return result;
            }
            return EmptyArray;
        }

        public void Release(T item)
        {
            if (item != null)
            {
                if ((_limit < 1) || (_cache.Count >= _limit) || Disposed)
                    OnDispose(item);
                else
                    Enqueue(item);
            }
        }

        public void Release(IList<T> items)
        {
            if (items != null)
            {
                foreach (var item in items)
                    Release(item);
            }
        }

        protected abstract void OnDispose(T item);

        protected abstract void OnEnqueue(T item);

        public void Release(IEnumerable<T> items)
        {
            if (items != null)
                foreach (var item in items)
                    Release(item);
        }

        public void Clear()
        {
            var cache = Interlocked.Exchange(ref _cache, new ConcurrentQueue<T>());
            if (cache != null)
            {
                foreach (var item in cache)
                {
                    try
                    {
                        OnDispose(item);
                    }
                    catch (Exception)
                    { }
                }
            }
        }
    }
}
