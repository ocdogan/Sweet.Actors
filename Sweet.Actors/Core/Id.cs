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
using System.Threading;

namespace Sweet.Actors
{
    public abstract class Id<T>
    {
        protected sealed class IdPart
        {
            private int _id = 0;
            private int _initial = 0;

            private bool _isTail;
            private IdPart _next;
            private int _position;

            private object _lock = new object();

            public IdPart(IdPart next, int position, int initialId = 0, bool isTail = false)
            {
                _id = initialId;
                _isTail = isTail;
                _initial = initialId;
                _next = next;
                _position = position;
            }

            public void SetNext(IdPart next)
            {
                _next = next;
            }

            public void SetSeed(int id)
            {
                _id = id;
            }

            public void Generate(int[] buffer)
            {
                var id = Interlocked.Add(ref _id, 1);
                if (id < 0)
                {
                    lock (_lock)
                    {
                        var original = Interlocked.CompareExchange(ref _id, _initial, id);
                        if (original != id)
                            Interlocked.Exchange(ref _id, _initial);

                        if (_isTail)
                            id = Interlocked.Add(ref _id, 1);

                        _next?.Generate(buffer);
                    }
                }

                buffer[_position] = id;
            }
        }

        private static readonly IdPart s_MajorGen;
        private static readonly IdPart s_MajorRevisionGen;
        private static readonly IdPart s_MinorGen;
        private static readonly IdPart s_MinorRevisionGen;

        protected static readonly byte[] ProcessIdBytes = Encoding.ASCII.GetBytes(Common.ProcessId.ToString());
        protected static readonly int ProcessIdBytesLength = ProcessIdBytes.Length;

        protected const int IntToStringMaxLength = 10;

        protected const char Dot = '.';
        protected const char Colon = ':';
        protected const char Minus = '-';
        protected const char Zero = '0';
        protected const char ParenthesesOpen = '[';
        protected const char ParenthesesClose = ']';

        private int _processId;
        private int _major;
        private int _majorRevision;
        private int _minor;
        private int _minorRevision;

        static Id()
        {
            s_MajorGen = new IdPart(null, 0);
            s_MajorRevisionGen = new IdPart(s_MajorGen, 1);
            s_MinorGen = new IdPart(s_MajorRevisionGen, 2);
            s_MinorRevisionGen = new IdPart(s_MinorGen, 3, 0, true);
        }

        protected Id(int major, int majorRevision, int minor, int minorRevision, int processId = -1)
        {
            _major = major;
            _majorRevision = majorRevision;
            _minor = minor;
            _minorRevision = minorRevision;
            _processId = processId < 0 ? Common.ProcessId : processId;
        }

        public int Major => _major;

        public int MajorRevision => _majorRevision;

        public int Minor => _minor;

        public int MinorRevision => _minorRevision;

        public int ProcessId => _processId;

        public char[] ToChars()
        {
            // $"[{ProcessId}:{Major}.{MajorRevision}.{Minor}.{MinorRevision}]"
            return AsChars(_processId, _major, _majorRevision, _minor, _minorRevision);
        }

        public override string ToString()
        {
            // $"[{ProcessId}:{Major}.{MajorRevision}.{Minor}.{MinorRevision}]"
            return new string(AsChars(_processId, _major, _majorRevision, _minor, _minorRevision));
        }

        protected static char[] AsChars(int processId, int major, int majorRevision, int minor, int minorRevision)
        {
            const int bufferSize = 56;

            var offset = 0;
            var buffer = new char[bufferSize];

            buffer[offset++] = ParenthesesOpen;

            if (processId != Common.ProcessId || processId == 0)
                WriteIdPart(processId, buffer, Colon, ref offset);
            else
            {
                Array.Copy(ProcessIdBytes, 0, buffer, offset, ProcessIdBytesLength);

                offset += ProcessIdBytesLength;
                buffer[offset++] = Colon;
            }

            WriteIdPart(major, buffer, Dot, ref offset);
            WriteIdPart(majorRevision, buffer, Dot, ref offset);
            WriteIdPart(minor, buffer, Dot, ref offset);
            WriteIdPart(minorRevision, buffer, ParenthesesClose, ref offset);

            if (offset >= bufferSize)
                return buffer;

            var result = new char[offset];
            Array.Copy(buffer, result, offset);

            return result;
        }

        private static void WriteIdPart(int value, char[] buffer, char separator, ref int offset)
        {
            if (value > -1 && value < 10)
                buffer[offset++] = (char)(Zero + value);
            else
            {
                if (value < 0) // negative value
                {
                    buffer[offset++] = Minus;
                    value = -value;
                }

                var rightOffset = offset + IntToStringMaxLength;
                do
                {
                    buffer[--rightOffset] = (char)(Zero + (value % 10));
                } while ((value /= 10) > 0);

                if (rightOffset == offset)
                    offset += IntToStringMaxLength;
                else
                {
                    var length = IntToStringMaxLength - (rightOffset - offset);
                    if (length == 1)
                        buffer[offset++] = buffer[rightOffset];
                    else
                    {
                        Array.Copy(buffer, rightOffset, buffer, offset, length);
                        offset += length;
                    }
                }
            }

            buffer[offset++] = separator;
        }

        public override int GetHashCode()
        {
            unchecked
            {
                var hash = _minorRevision.GetHashCode();
                hash = 31 * hash + _minor.GetHashCode();
                hash = 31 * hash + _majorRevision.GetHashCode();
                return 31 * hash + _major.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            var other = obj as Id<T>;
            if (other is null)
                return false;
            
            return other.GetHashCode() == GetHashCode() &&
                other.ProcessId == ProcessId &&
                other.MinorRevision == MinorRevision &&
                other.Minor == Minor &&
                other.MajorRevision == MajorRevision &&
                other.Major == Major;
        }

        protected static int[] Generate()
        {
            var buffer = new int[4];
            s_MinorRevisionGen.Generate(buffer);

            return buffer;
        }
    }
}
