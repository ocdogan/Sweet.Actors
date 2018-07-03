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
using System.Collections.Generic;
using System.Diagnostics.Contracts;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Sweet.Actors
{
    #region PartitionedList

    public class PartitionedList<T> : Disposable, IEnumerable, IEnumerable<T>
    {
        #region IEnumerator<T>

        [Serializable]
        public struct PartitionedListEnumerator : IEnumerator<T>, IDisposable, IEnumerator
        {
            private PartitionedList<T> _list;
            private List<Bucket> _buckets;
            private int _bucketCount;
            private int _currBucket;
            private int _currItem;
            private int _version;
            private T _current;

            public T Current
            {
                get { return _current; }
            }

            object IEnumerator.Current
            {
                get
                {
                    if (_currItem < 0 || _currBucket < 0 || _currBucket > _bucketCount - 1)
                        throw new Exception("Enum operation can't happen");
                    return Current;
                }
            }

            internal PartitionedListEnumerator(PartitionedList<T> list)
            {
                _list = list;
                _buckets = _list._buckets;

                _currItem = 0;
                _currBucket = -1;
                _bucketCount = _buckets.Count;

                _version = _list._version;
                _current = default(T);
            }

            public void Dispose()
            {
            }

            private Bucket GetCurrentBucket()
            {
                if (_currItem < 0)
                    throw new Exception("Enum operation can't happen");

                if (_version != _list._version)
                    throw new Exception("Enum failed version");

                if (_currBucket < 0) _currBucket = 0;

                while (_currBucket < _buckets.Count)
                {
                    var bucket = _buckets[_currBucket];
                    if (_currItem < bucket.Count)
                        return bucket;

                    _currItem = 0;
                    _currBucket++;
                }
                return null;
            }

            public bool MoveNext()
            {
                if (_version != _list._version)
                    throw new Exception("Enum failed version");

                var bucket = GetCurrentBucket();
                if (bucket != null)
                {
                    _current = bucket[_currItem];
                    _currItem++;
                    return true;
                }
                return MoveNextRare();
            }

            private bool MoveNextRare()
            {
                if (_version != _list._version)
                    throw new Exception("Enum failed version");

                _currItem = -1;
                _currBucket = _bucketCount;

                _current = default(T);
                return false;
            }

            void IEnumerator.Reset()
            {
                if (_version != _list._version)
                    throw new Exception("Enum failed version");

                _currItem = 0;
                _currBucket = -1;
                _current = default(T);
            }
        }

        #endregion IEnumerator<T>

        #region Bucket

        private class Bucket : List<T>, IDisposable
        {
            #region Field Members

            private int _disposed;
            private PartitionedList<T> _parent;
            private readonly ReaderWriterLockSlim _syncRoot = new ReaderWriterLockSlim();

            #endregion Field Members

            #region .Ctors

            public Bucket(PartitionedList<T> parent, int capacity = -1)
                : base(capacity < 0 ? 0 : capacity)
            {
                _parent = parent;
            }

            #endregion .Ctors

            #region Destructors

            ~Bucket()
            {
                Dispose(false);
            }

            public void Dispose()
            {
                Dispose(true);
            }

            protected virtual void OnDispose()
            {
                _syncRoot.EnterWriteLock();
                try
                {
                    _parent = null;
                    Clear();
                }
                finally
                {
                    _syncRoot.ExitWriteLock();
                }
            }

            protected virtual void Dispose(bool disposing)
            {
                var wasDisposed = Interlocked.Exchange(ref _disposed, 1) == 1;
                if (wasDisposed)
                    return;

                if (disposing)
                    GC.SuppressFinalize(this);

                OnDispose();
            }

            #endregion Destructors

            #region Properties

            public T Minimum
            {
                get
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        var count = Count;
                        if (count > 0)
                        {
                            var first = base[0];
                            if (count > 1)
                            {
                                if (_parent == null || !_parent.Sorted)
                                {
                                    first = this.Min<T>();
                                }
                                else
                                {
                                    var last = base[count - 1];
                                    if (Comparer<T>.Default.Compare(last, first) < 0)
                                        return last;
                                }
                            }
                            return first;
                        }
                    }
                    finally
                    {
                        _syncRoot.ExitUpgradeableReadLock();
                    }
                    return default(T);
                }
            }

            public T Maximum
            {
                get
                {
                    _syncRoot.EnterUpgradeableReadLock();
                    try
                    {
                        var count = Count;
                        if (count > 0)
                        {
                            var last = base[count - 1];
                            if (count > 1)
                            {
                                if (_parent == null || !_parent.Sorted)
                                {
                                    last = this.Max<T>();
                                }
                                else
                                {
                                    var first = base[0];
                                    if (Comparer<T>.Default.Compare(first, last) > 0)
                                        return first;
                                }
                            }
                            return last;
                        }
                    }
                    finally
                    {
                        _syncRoot.ExitUpgradeableReadLock();
                    }
                    return default(T);
                }
            }

            #endregion Properties
        }

        #endregion Bucket

        #region Constants

        private const int LargeObjectHeapSize = 85000;
        private const int MaxArraySize = LargeObjectHeapSize - 1000;

        #endregion Constants

        #region Field Members

        private int _count;
        private int _bucketSize;
        private int _version;
        private bool _sorted;
        private int _uncompleteCount;
        private List<Bucket> _buckets = new List<Bucket>();

        private readonly ReaderWriterLockSlim _syncRoot = new ReaderWriterLockSlim();

        #endregion Field Members

        #region .Ctors

        public PartitionedList(int capacity = 0)
        {
            var itemSize = Marshal.SizeOf(typeof(IntPtr));
            if (typeof(T).IsValueType)
                itemSize = Marshal.SizeOf(typeof(T));

            _bucketSize = MaxArraySize / itemSize;

            if (capacity > 0)
                _buckets.Add(new Bucket(this, Math.Min(capacity, _bucketSize)));
        }

        #endregion .Ctors

        #region Destructors

        protected override void OnDispose(bool disposing)
        {
            _syncRoot.EnterWriteLock();
            try
            {
                var buckets = Interlocked.Exchange(ref _buckets, null);
                foreach (var bucket in buckets)
                    bucket.Dispose();

                buckets.Clear();

                _count = 0;
                _uncompleteCount = 0;
                _sorted = false;
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
        }

        #endregion Destructors

        #region Properties

        public int Count
        {
            get { return _count; }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        public bool IsSynchronized
        {
            get { return true; }
        }

        public object SyncRoot
        {
            get { return _syncRoot; }
        }

        public virtual uint Version
        {
            get { return 1; }
        }

        public bool Sorted
        {
            get { return _sorted; }
        }

        public virtual T this[int index]
        {
            get
            {
                if (index < 0 || index > _count - 1)
                    throw new ArgumentOutOfRangeException(nameof(index));

                ThrowIfDisposed();

                _syncRoot.EnterUpgradeableReadLock();
                try
                {
                    if (_uncompleteCount == 0)
                    {
                        var bucketIndex = index / _bucketSize;
                        index %= _bucketSize;

                        return _buckets[bucketIndex][index];
                    }

                    foreach (var bucket in _buckets)
                    {
                        if (index < bucket.Count)
                            return bucket[index];

                        index -= bucket.Count;
                        if (index < 0)
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                }
                finally
                {
                    _syncRoot.ExitUpgradeableReadLock();
                }
                return default(T);
            }
            set
            {
                if (index < 0 || index > _count - 1)
                    throw new ArgumentOutOfRangeException(nameof(index));

                ThrowIfDisposed();

                _syncRoot.EnterWriteLock();
                try
                {
                    _version++;

                    if (_uncompleteCount == 0)
                    {
                        var bucketIndex = index / _bucketSize;
                        index %= _bucketSize;

                        _buckets[bucketIndex][index] = value;
                        return;
                    }

                    foreach (var bucket in _buckets)
                    {
                        if (index < bucket.Count)
                        {
                            bucket[index] = value;
                            return;
                        }

                        index -= bucket.Count;
                        if (index < 0)
                            throw new ArgumentOutOfRangeException(nameof(index));
                    }
                    _sorted = false;
                }
                finally
                {
                    _syncRoot.ExitWriteLock();
                }
            }
        }

        #endregion Properties

        #region Methods

        #region ICollection

        public virtual void Put(T item)
        {
            ThrowIfDisposed();

            _syncRoot.EnterWriteLock();
            try
            {
                _version++;

                Bucket currBucket = null;
                var addedToUncompleted = false;

                var bucketCount = _buckets.Count;
                if (bucketCount > 0)
                {
                    if (_uncompleteCount == 0)
                    {
                        currBucket = _buckets[bucketCount - 1];
                        if (currBucket.Count > _bucketSize - 1)
                            currBucket = null;
                    }
                    else
                    {
                        for (var i = bucketCount - 1; i > -1; i--)
                        {
                            var bucket = _buckets[i];
                            if (bucket.Count < _bucketSize)
                            {
                                currBucket = bucket;
                                addedToUncompleted = (i < bucketCount - 1);
                                break;
                            }
                        }
                    }
                }

                if (currBucket == null)
                {
                    currBucket = new Bucket(this, _bucketSize);
                    _buckets.Add(currBucket);
                }

                currBucket.Add(item);
                _count++;

                if (addedToUncompleted)
                    _uncompleteCount--;
                _sorted = false;
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
        }

        public virtual void Clear()
        {
            ThrowIfDisposed();

            _syncRoot.EnterWriteLock();
            try
            {
                _version++;

                var buckets = Interlocked.Exchange(ref _buckets, new List<Bucket>());
                foreach (var bucket in buckets)
                    bucket.Dispose();

                buckets.Clear();

                _count = 0;
                _uncompleteCount = 0;
                _sorted = false;
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
        }

        public virtual bool Contains(T item)
        {
            ThrowIfDisposed();

            _syncRoot.EnterUpgradeableReadLock();
            try
            {
                foreach (var bucket in _buckets)
                    if (bucket.Contains(item))
                        return true;
            }
            finally
            {
                _syncRoot.ExitUpgradeableReadLock();
            }
            return false;
        }

        public virtual void CopyTo(T[] array, int arrayIndex)
        {
            ThrowIfDisposed();

            _syncRoot.EnterUpgradeableReadLock();
            try
            {
                foreach (var partition in _buckets)
                {
                    partition.CopyTo(array, arrayIndex);
                    arrayIndex += partition.Count;
                }
            }
            finally
            {
                _syncRoot.ExitUpgradeableReadLock();
            }
        }

        public virtual bool Remove(T item)
        {
            ThrowIfDisposed();

            _syncRoot.EnterWriteLock();
            try
            {
                _version++;

                Bucket bucket;
                var bucketCount = _buckets.Count;

                for (var i = 0; i < bucketCount; i++)
                {
                    bucket = _buckets[i];
                    if (bucket.Remove(item))
                    {
                        _count--;

                        if (bucket.Count == 0)
                        {
                            _buckets.RemoveAt(i);
                            bucket.Dispose();

                            if (i > 0 && i == bucketCount - 1)
                            {
                                bucket = _buckets[i - 1];
                                if (bucket.Count < _bucketSize)
                                    _uncompleteCount--;
                            }
                        }
                        else if (i < bucketCount - 1)
                            _uncompleteCount++;

                        return true;
                    }
                }
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
            return false;
        }

        public IEnumerator<T> GetEnumerator()
        {
            ThrowIfDisposed();
            return new PartitionedListEnumerator(this);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            ThrowIfDisposed();
            return new PartitionedListEnumerator(this);
        }

        #endregion ICollection

        #region IList

        public virtual int IndexOf(T item)
        {
            ThrowIfDisposed();

            _syncRoot.EnterUpgradeableReadLock();
            try
            {
                var start = 0;
                foreach (var bucket in _buckets)
                {
                    var index = bucket.IndexOf(item);
                    if (index > -1)
                        return start + index;

                    start += bucket.Count;
                }
                return -1;
            }
            finally
            {
                _syncRoot.ExitUpgradeableReadLock();
            }
        }

        public virtual void RemoveAt(int index)
        {
            if (index < 0 || index > _count - 1)
                throw new ArgumentOutOfRangeException(nameof(index));

            ThrowIfDisposed();

            _syncRoot.EnterWriteLock();
            try
            {
                _version++;

                Bucket bucket;
                var bucketCount = _buckets.Count;

                for (var i = 0; i < bucketCount; i++)
                {
                    bucket = _buckets[i];

                    if (index < bucket.Count)
                    {
                        bucket.RemoveAt(index);
                        _count--;

                        if (bucket.Count == 0)
                        {
                            _buckets.RemoveAt(i);
                            bucket.Dispose();

                            if (i > 0 && i == bucketCount - 1)
                            {
                                bucket = _buckets[i - 1];
                                if (bucket.Count < _bucketSize)
                                    _uncompleteCount--;
                            }
                        }
                        else if (i < bucketCount - 1)
                            _uncompleteCount++;
                        return;
                    }

                    index -= bucket.Count;
                    if (index < 0)
                        throw new ArgumentOutOfRangeException(nameof(index));
                }
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
        }

        #endregion IList

        #region Sort

        private void Swap(int i, int j)
        {
            if (i != j)
            {
                T t = this[i];
                this[i] = this[j];
                this[j] = t;
            }
        }

        private void SwapIfGreater(IComparer<T> comparer, int a, int b)
        {
            if (a != b && (comparer.Compare(this[a], this[b]) > 0))
            {
                T key = this[a];
                this[a] = this[b];
                this[b] = key;
            }
        }

        private void DownHeap(int i, int n, int lo, IComparer<T> comparer)
        {
            Contract.Requires(comparer != null);
            Contract.Requires(lo >= 0);
            Contract.Requires(lo < _count);

            T d = this[lo + i - 1];
            int child;
            while (i <= n / 2)
            {
                child = 2 * i;
                if (child < n && comparer.Compare(this[lo + child - 1], this[lo + child]) < 0)
                    child++;

                if (!(comparer.Compare(d, this[lo + child - 1]) < 0))
                    break;

                this[lo + i - 1] = this[lo + child - 1];
                i = child;
            }
            this[lo + i - 1] = d;
        }

        private void Heapsort(int lo, int hi, IComparer<T> comparer)
        {
            Contract.Requires(comparer != null);
            Contract.Requires(lo >= 0);
            Contract.Requires(hi > lo);
            Contract.Requires(hi < _count);

            var n = hi - lo + 1;
            for (int i = n / 2; i >= 1; i = i - 1)
            {
                DownHeap(i, n, lo, comparer);
            }
            for (int i = n; i > 1; i = i - 1)
            {
                Swap(lo, lo + i - 1);
                DownHeap(1, i - 1, lo, comparer);
            }
        }

        private void DepthLimitedQuickSort(int left, int right, IComparer<T> comparer, int depthLimit)
        {
            do
            {
                if (depthLimit == 0)
                {
                    Heapsort(left, right, comparer);
                    return;
                }

                int i = left;
                int j = right;

                int middle = i + ((j - i) >> 1);
                SwapIfGreater(comparer, i, middle);  // swap the low with the mid point
                SwapIfGreater(comparer, i, j);       // swap the low with the high
                SwapIfGreater(comparer, middle, j);  // swap the middle with the high

                T x = this[middle];
                do
                {
                    while (comparer.Compare(this[i], x) < 0) i++;
                    while (comparer.Compare(x, this[j]) < 0) j--;

                    if (i > j) break;
                    if (i < j)
                    {
                        T key = this[i];
                        this[i] = this[j];
                        this[j] = key;
                    }
                    i++;
                    j--;
                } while (i <= j);

                depthLimit--;

                if (j - left <= right - i)
                {
                    if (left < j) DepthLimitedQuickSort(left, j, comparer, depthLimit);
                    left = i;
                }
                else
                {
                    if (i < right) DepthLimitedQuickSort(i, right, comparer, depthLimit);
                    right = j;
                }
            } while (left < right);
        }

        public void Sort(IComparer<T> comparer = null)
        {
            Sort(0, _count, comparer);
        }

        public void Sort(int index, int length, IComparer<T> comparer = null)
        {
            ThrowIfDisposed();

            _syncRoot.EnterWriteLock();
            try
            {
                try
                {
                    if (comparer == null)
                        comparer = Comparer<T>.Default;

                    DepthLimitedQuickSort(index, length + index - 1, comparer, 32);
                    _sorted = true;
                }
                catch (IndexOutOfRangeException)
                {
                    throw new Exception("Bad comparer");
                }
                catch (Exception e)
                {
                    throw new InvalidOperationException("Invalid operation. Comparer failed", e);
                }
            }
            finally
            {
                _syncRoot.ExitWriteLock();
            }
        }

        #endregion Sort

        #region Search

        public int BinarySearch<K>(int index, int length, K value, Func<T, K, int> comparer)
        {
            var lo = index;
            var hi = index + length - 1;

            int order, current;
            while (lo <= hi)
            {
                current = lo + ((hi - lo) >> 1);
                order = comparer(this[current], value);

                if (order == 0)
                    return current;

                if (order < 0)
                {
                    lo = current + 1;
                }
                else
                {
                    hi = current - 1;
                }
            }
            return ~lo;
        }

        #endregion Search

        #endregion Methods
    }

    #endregion PartitionedList
}