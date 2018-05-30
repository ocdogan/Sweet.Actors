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

namespace Sweet.Actors
{
    public class SynchronizedDictionary<T, K> : IDictionary<T, K>, IDictionary
    {
        #region Field Members

        private readonly object _syncRoot = new object();
        private readonly Dictionary<T, K> _innerDictionary;

        #endregion Field Members

        #region .Ctors

        public SynchronizedDictionary()
            : this(0, null)
        { }

        public SynchronizedDictionary(int capacity)
            : this(capacity, null)
        { }

        public SynchronizedDictionary(IEqualityComparer<T> comparer)
            : this(0, comparer)
        { }

        public SynchronizedDictionary(int capacity, IEqualityComparer<T> comparer)
        {
            _innerDictionary = new Dictionary<T, K>(capacity, comparer);
        }

        public SynchronizedDictionary(IDictionary<T, K> dictionary)
            : this(dictionary, null)
        { }

        public SynchronizedDictionary(IDictionary<T, K> dictionary, IEqualityComparer<T> comparer) :
            this(dictionary != null ? dictionary.Count : 0, comparer)
        {
            _innerDictionary = new Dictionary<T, K>(dictionary, comparer);
        }

        #endregion .Ctors

        #region Properties

        object ICollection.SyncRoot
        {
            get { return _syncRoot; }
        }

        public ICollection<T> Keys
        {
            get
            {
                lock (_syncRoot)
                {
                    return _innerDictionary.Keys;
                }
            }
        }

        public ICollection<K> Values
        {
            get
            {
                lock (_syncRoot)
                {
                    return _innerDictionary.Values;
                }
            }
        }

        public K this[T key]
        {
            get
            {
                lock (_syncRoot)
                {
                    return _innerDictionary[key];
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    _innerDictionary[key] = value;
                }
            }
        }

        public bool IsFixedSize
        {
            get { return false; }
        }

        public int Count
        {
            get
            {
                lock (_syncRoot)
                {
                    return _innerDictionary.Count;
                }
            }
        }

        public bool IsReadOnly
        {
            get { return false; }
        }

        ICollection IDictionary.Keys
        {
            get
            {
                lock (_syncRoot)
                {
                    return ((IDictionary)_innerDictionary).Keys;
                }
            }
        }

        ICollection IDictionary.Values
        {
            get
            {
                lock (_syncRoot)
                {
                    return ((IDictionary)_innerDictionary).Values;
                }
            }
        }

        object IDictionary.this[object key]
        {
            get
            {
                lock (_syncRoot)
                {
                    return ((IDictionary)_innerDictionary)[key];
                }
            }
            set
            {
                lock (_syncRoot)
                {
                    ((IDictionary)_innerDictionary)[key] = value;
                }
            }
        }

        public bool IsSynchronized
        {
            get { return true; }
        }

        #endregion Properties

        #region Methods

        public void Add(T key, K value)
        {
            lock (_syncRoot)
            {
                _innerDictionary.Add(key, value);
            }
        }

        public bool ContainsKey(T key)
        {
            lock (_syncRoot)
            {
                return _innerDictionary.ContainsKey(key);
            }
        }

        public bool Remove(T key)
        {
            lock (_syncRoot)
            {
                return _innerDictionary.Remove(key);
            }
        }

        public bool TryGetValue(T key, out K value)
        {
            lock (_syncRoot)
            {
                return _innerDictionary.TryGetValue(key, out value);
            }
        }

        public K GetValueOrUpdate(T key, Func<T, K> f)
        {
            K result;
            if (!TryGetValue(key, out result))
            {
                lock (_syncRoot)
                {
                    if (!TryGetValue(key, out result))
                    {
                        _innerDictionary[key] = (result = f(key));
                    }
                }
            }
            return result;
        }

        void ICollection<KeyValuePair<T, K>>.Add(KeyValuePair<T, K> item)
        {
            lock (_syncRoot)
            {
                ((ICollection<KeyValuePair<T, K>>)_innerDictionary).Add(item);
            }
        }

        public void Clear()
        {
            lock (_syncRoot)
            {
                _innerDictionary.Clear();
            }
        }

        bool ICollection<KeyValuePair<T, K>>.Contains(KeyValuePair<T, K> item)
        {
            lock (_syncRoot)
            {
                return ((ICollection<KeyValuePair<T, K>>)_innerDictionary).Contains(item);
            }
        }

        void ICollection<KeyValuePair<T, K>>.CopyTo(KeyValuePair<T, K>[] array, int arrayIndex)
        {
            lock (_syncRoot)
            {
                ((ICollection<KeyValuePair<T, K>>)_innerDictionary).CopyTo(array, arrayIndex);
            }
        }

        bool ICollection<KeyValuePair<T, K>>.Remove(KeyValuePair<T, K> item)
        {
            lock (_syncRoot)
            {
                return ((ICollection<KeyValuePair<T, K>>)_innerDictionary).Remove(item);
            }
        }

        public IEnumerator<KeyValuePair<T, K>> GetEnumerator()
        {
            lock (_syncRoot)
            {
                return _innerDictionary.GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            lock (_syncRoot)
            {
                return ((IEnumerable)_innerDictionary).GetEnumerator();
            }
        }

        void IDictionary.Add(object key, object value)
        {
            lock (_syncRoot)
            {
                ((IDictionary)_innerDictionary).Add(key, value);
            }
        }

        bool IDictionary.Contains(object key)
        {
            lock (_syncRoot)
            {
                return ((IDictionary)_innerDictionary).Contains(key);
            }
        }

        IDictionaryEnumerator IDictionary.GetEnumerator()
        {
            lock (_syncRoot)
            {
                return ((IDictionary)_innerDictionary).GetEnumerator();
            }
        }

        void IDictionary.Remove(object key)
        {
            lock (_syncRoot)
            {
                ((IDictionary)_innerDictionary).Remove(key);
            }
        }

        void ICollection.CopyTo(Array array, int index)
        {
            lock (_syncRoot)
            {
                ((ICollection)_innerDictionary).CopyTo(array, index);
            }
        }

        #endregion Methods
    }
}