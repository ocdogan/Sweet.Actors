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

using System.Threading;

namespace Sweet.Actors
{
    public abstract class Id<T>
    {
        protected sealed class IdPart
        {
            private long m_Id = 0L;
            private long m_Initial = 0L;

            private IdPart m_Next;
            private int m_Position;

            private object m_Lock = new object();

            public IdPart(IdPart next, int position, long initialId = 0L)
            {
                m_Id = initialId;
                m_Initial = initialId;
                m_Next = next;
                m_Position = position;
            }

            public void SetNext(IdPart next)
            {
                m_Next = next;
            }

            public void SetSeed(long id)
            {
                m_Id = id;
            }

            public void Generate(long[] buffer)
            {
                var id = Interlocked.Add(ref m_Id, 1L);

                Interlocked.MemoryBarrier();
                if (id < 0 && m_Next != null)
                {
                    lock (m_Lock)
                    {
                        var original = Interlocked.CompareExchange(ref m_Id, m_Initial, id);
                        if (original < 0)
                        {
                            id = 0L;
                            m_Next.Generate(buffer);
                        }
                    }
                }
                buffer[m_Position] = id;
            }
        }

        private static readonly IdPart s_MajorGen;
        private static readonly IdPart s_MajorRevisionGen;
        private static readonly IdPart s_MinorGen;
        private static readonly IdPart s_MinorRevisionGen;

        private int _processId;

        static Id()
        {
            s_MajorGen = new IdPart(null, 3);
            s_MajorRevisionGen = new IdPart(s_MajorGen, 2);
            s_MinorGen = new IdPart(s_MajorRevisionGen, 1);
            s_MinorRevisionGen = new IdPart(s_MinorGen, 0, -1);

            s_MajorGen.SetNext(s_MinorRevisionGen);
        }

        protected Id(long major, long majorRevision, long minor, long minorRevision, int processId = -1)
        {
            Major = major;
            MajorRevision = majorRevision;
            Minor = minor;
            MinorRevision = minorRevision;
            _processId = processId < 0 ? Common.ProcessId : processId;
        }

        public long Major { get; }

        public long MajorRevision { get; }

        public long Minor { get; }

        public long MinorRevision { get; }

        public int ProcessId => _processId;

        public override string ToString() => $"[{ProcessId}-{Major}.{MajorRevision}.{Minor}.{MinorRevision}]";

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = MinorRevision.GetHashCode();
                hash = 31 * hash + Minor.GetHashCode();
                hash = 31 * hash + MajorRevision.GetHashCode();
                return 31 * hash + Major.GetHashCode();
            }
        }

        public override bool Equals(object obj)
        {
            if (ReferenceEquals(obj, null))
                return false;

            var other = obj as Id<T>;
            if (ReferenceEquals(other, null))
                return false;
            
            return other.ProcessId == ProcessId &&
                        other.MinorRevision == MinorRevision &&
                        other.Minor == Minor &&
                        other.MajorRevision == MajorRevision &&
                        other.Major == Major;
        }

        protected static long[] Generate()
        {
            var buffer = new long[4];
            s_MinorRevisionGen.Generate(buffer);

            return buffer;
        }
    }
}
