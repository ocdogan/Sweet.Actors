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
using System.Text;

namespace Sweet.Actors
{
    public class ArraySlice : Disposable, IEquatable<ArraySlice>
    {
        #region Constants

        private const string Nil = "(nil)";
        private const string Empty = "(empty)";

        #endregion Constants

        #region Field Members

        private int _offset;
        private int _count;
        private int _length;

        private int _hashCode;
        private byte[] _array;
        private ArraySliceCache _owner;

        #endregion Field Members

        #region .Ctors

        public ArraySlice(byte[] array)
        {
            _array = array;
            _count = _length = array?.Length ?? 0;
        }

        public ArraySlice(byte[] array, int offset, int count)
        {
            if (offset < 0)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (count < 0)
                throw new ArgumentOutOfRangeException(nameof(count));

            _length = array?.Length ?? 0;
            if (_length - offset < count)
                throw new ArgumentOutOfRangeException(nameof(count));

            _array = array;
            _offset = offset;
            _count = count;
        }

        internal ArraySlice(byte[] array, ArraySliceCache owner)
        {
            _array = array;
            _owner = owner;
            _count = _length = array?.Length ?? 0;
        }

        #endregion .Ctors

        #region Properties

        public byte[] Array
        {
            get { return _array; }
        }

        public int Count => _count;

        public int Offset => _offset;

        #endregion Properties

        #region Methods

        protected override void OnDispose(bool disposing)
        {
            if (disposing && _owner != null && !_owner.Disposed)
                _owner.Release(_array);

            base.OnDispose(disposing);
        }

        public bool Equals(byte[] other)
        {
            if (!ReferenceEquals(other, null))
                return _array.EqualTo(other);

            return false;
        }

        public bool Equals(string other)
        {
            if (!ReferenceEquals(other, null))
                return _array.EqualTo(other.ToBytes());

            return false;
        }

        public bool Equals(ArraySlice other)
        {
            if (!ReferenceEquals(other, null))
            {
                if (ReferenceEquals(other, this))
                    return true;

                if (GetHashCode() == other.GetHashCode())
                    return _array.EqualTo(other._array);
            }

            return false;
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
                return (_array == null);

            var rba = obj as ArraySlice;
            if (!ReferenceEquals(rba, null))
                return this.Equals(rba);

            var ba = obj as byte[];
            if (!ReferenceEquals(ba, null))
                return _array.EqualTo(ba);

            var s = obj as string;
            if (!ReferenceEquals(s, null))
                return _array.EqualTo(s.ToBytes());

            return false;
        }

        public override int GetHashCode()
        {
            if (_hashCode == 0)
            {
                var hash = 1;
                var seed = 314;

                if (_array != null && _length > 0)
                {
                    for (var i = 0; i < _length; i++)
                    {
                        hash = (hash * seed) + _array[i];
                        seed *= 159;
                    }
                }
                _hashCode = hash ^ _offset ^ _count;
            }
            return _hashCode;
        }

        public override string ToString()
        {
            if (_array == null)
                return Nil;
            if (_array.Length == 0)
                return Empty;

            var sb = new StringBuilder(512);
            sb.Append('[');
            var length = Math.Min(100, _array.Length);

            for (var i = 0; i < length; i++)
            {
                sb.Append(_array[i]);
                if (i < length - 1)
                    sb.Append(", ");
            }

            if (_array.Length > 100)
                sb.Append(" ... ");

            sb.Append(']');
            return sb.ToString();
        }

        #endregion Methods

        #region Conversion Methods

        #region To RedisByteArray

        public static implicit operator ArraySlice(byte[] value)  // implicit to RedisByteArray conversion operator
        {
            return new ArraySlice(value);
        }

        public static implicit operator ArraySlice(string value)  // implicit to RedisByteArray conversion operator
        {
            return new ArraySlice(value.ToBytes());
        }

        #endregion To RedisByteArray

        #region From RedisByteArray

        public static implicit operator byte[] (ArraySlice value)  // implicit from RedisByteArray conversion operator
        {
            return !ReferenceEquals(value, null) ? value.Array : null;
        }

        public static implicit operator string(ArraySlice value)  // implicit from RedisByteArray conversion operator
        {
            return ReferenceEquals(value, null) ? null : value.Array.ToUTF8String();
        }

        #endregion From RedisByteArray

        #endregion Conversion Methods

        #region Operator Overloads

        public static bool operator ==(ArraySlice a, ArraySlice b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);

            if (ReferenceEquals(b, null))
                return false;

            if (ReferenceEquals(a, b))
                return true;

            return a.Equals(b);
        }

        public static bool operator !=(ArraySlice a, ArraySlice b)
        {
            return !(a == b);
        }

        public static bool operator ==(byte[] a, ArraySlice b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);

            if (ReferenceEquals(b, null))
                return false;

            return a.EqualTo(b._array);
        }

        public static bool operator !=(byte[] a, ArraySlice b)
        {
            return !(b == a);
        }

        public static bool operator ==(ArraySlice a, byte[] b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);

            if (ReferenceEquals(b, null))
                return false;

            return b.EqualTo(a._array);
        }

        public static bool operator !=(ArraySlice a, byte[] b)
        {
            return !(a == b);
        }

        public static bool operator ==(string a, ArraySlice b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);

            if (ReferenceEquals(b, null))
                return false;

            return b.Equals(a);
        }

        public static bool operator !=(string a, ArraySlice b)
        {
            return !(b == a);
        }

        public static bool operator ==(ArraySlice a, string b)
        {
            if (ReferenceEquals(a, null))
                return ReferenceEquals(b, null);

            if (ReferenceEquals(b, null))
                return false;

            return a.Equals(b);
        }

        public static bool operator !=(ArraySlice a, string b)
        {
            return !(a == b);
        }

        #endregion Operator Overloads
    }
}
