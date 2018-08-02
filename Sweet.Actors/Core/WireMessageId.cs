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
using System.IO;

namespace Sweet.Actors
{
    public sealed class WireMessageId : Id<WireMessage>
    {
        public static readonly WireMessageId Empty = new WireMessageId(0, 0, 0, 0, 0);

        private const int BufferSize = 5 * sizeof(int);
        private static readonly int[] BufferOffsets = { 0, sizeof(int), 2 * sizeof(int), 3 * sizeof(int), 4 * sizeof(int) };

        private static readonly int EmptyLength = Empty.ToString().Length;

        private WireMessageId(int major, int majorRevision, int minor, int minorRevision, int processId = -1)
            : base(major, majorRevision, minor, minorRevision, processId)
        { }

        public static WireMessageId Next()
        {
            var buffer = Generate();
            return new WireMessageId(buffer[0], buffer[1], buffer[2], buffer[3], -1);
        }

        public static string NextAsString()
        {
            var buffer = Generate();
            return new String(AsChars(Common.ProcessId, buffer[0], buffer[1], buffer[2], buffer[3]));
        }

        internal static WireMessageId Convert(int major, int majorRevision, int minor, int minorRevision, int processId)
        {
            if (major < 0)
                throw new ArgumentOutOfRangeException(nameof(major));

            if (majorRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(majorRevision));

            if (minor < 0)
                throw new ArgumentOutOfRangeException(nameof(minor));

            if (minorRevision < 0)
                throw new ArgumentOutOfRangeException(nameof(minorRevision));

            if (processId < 0)
                throw new ArgumentOutOfRangeException(nameof(processId));

            if (major == 0 && majorRevision == 0 && 
                minor == 0 && minorRevision == 0 && processId == 0)
                return Empty;

            return new WireMessageId(major, majorRevision, minor, minorRevision, processId);
        }

        public static WireMessageId Read(IStreamReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            var buffer = new byte[BufferSize];
            if (reader.Read(buffer, 0, BufferSize) != BufferSize)
                throw new ArgumentOutOfRangeException(nameof(buffer));

            return Convert(buffer.ToInt(BufferOffsets[0]),
                buffer.ToInt(BufferOffsets[1]),
                buffer.ToInt(BufferOffsets[2]),
                buffer.ToInt(BufferOffsets[3]),
                buffer.ToInt(BufferOffsets[4]));
        }

        public static WireMessageId Read(BinaryReader reader)
        {
            if (reader == null)
                throw new ArgumentNullException(nameof(reader));

            var buffer = new byte[BufferSize];
            if (reader.Read(buffer, 0, BufferSize) != BufferSize)
                throw new ArgumentOutOfRangeException(nameof(buffer));

            return Convert(buffer.ToInt(BufferOffsets[0]), 
                buffer.ToInt(BufferOffsets[1]), 
                buffer.ToInt(BufferOffsets[2]), 
                buffer.ToInt(BufferOffsets[3]), 
                buffer.ToInt(BufferOffsets[4]));
        }

        public void Write(IStreamWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(Major.ToBytes());
            writer.Write(MajorRevision.ToBytes());
            writer.Write(Minor.ToBytes());
            writer.Write(MinorRevision.ToBytes());
            writer.Write(ProcessId.ToBytes());
        }

        public void Write(BinaryWriter writer)
        {
            if (writer == null)
                throw new ArgumentNullException(nameof(writer));

            writer.Write(Major.ToBytes());
            writer.Write(MajorRevision.ToBytes());
            writer.Write(Minor.ToBytes());
            writer.Write(MinorRevision.ToBytes());
            writer.Write(ProcessId.ToBytes());
        }

        public static bool TryParse(string sid, out WireMessageId id)
        {
            id = Empty;

            if (sid != null)
            {
                var length = sid.Length;
                if (length >= EmptyLength)
                {
                    var startPos = GetStartPos(sid, length);
                    if (startPos > -1)
                    {
                        startPos++;

                        var endPos = GetEndPos(sid, length, startPos);
                        if (endPos > -1)
                        {
                            int dashPos = GetPositionOf(Colon, sid, startPos, Math.Min(startPos + 11, endPos));

                            if (dashPos > -1 &&
                                TryParseInt(sid, ref startPos, dashPos - startPos, out int pid))
                            {
                                startPos++;
                                var dotPos = GetPositionOf(Dot, sid, startPos, Math.Min(startPos + 11, endPos));

                                if (dotPos > -1 &&
                                    TryParseInt(sid, ref startPos, dotPos - startPos, out int major))
                                {
                                    startPos++;
                                    dotPos = GetPositionOf(Dot, sid, startPos, Math.Min(startPos + 11, endPos));

                                    if (dotPos > -1 &&
                                        TryParseInt(sid, ref startPos, dotPos - startPos, out int majorRevision))
                                    {
                                        startPos++;
                                        dotPos = GetPositionOf(Dot, sid, startPos, Math.Min(startPos + 11, endPos));

                                        if (dotPos > -1 &&
                                            TryParseInt(sid, ref startPos, dotPos - startPos, out int minor))
                                        {
                                            startPos++;
                                            if (TryParseInt(sid, ref startPos, endPos - startPos, out int minorRevision))
                                            {
                                                id = new WireMessageId(major, majorRevision, minor, minorRevision, pid);
                                                return true;
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            return false;
        }

        private static bool TryParseInt(string sid, ref int startPos, int length, out int value)
        {
            value = 0;
            if (length > 0)
            {
                var minus = (sid[startPos] == Minus);
                if (minus)
                    startPos++;

                var digit = 1;
                for (var i = startPos + length - 1; i >= startPos; i--)
                {
                    if (!char.IsNumber(sid[i]))
                        return false;

                    value += (sid[i] - Zero) * digit;
                    digit *= 10;
                }

                startPos += length;
                if (minus)
                {
                    value = -value;
                    startPos--;
                }

                return true;
            }
            return false;
        }

        private static int GetPositionOf(char charToFind, string sid, int startPos, int endPos)
        {
            var dotPos = -1;
            for (var i = startPos; i < endPos; i++)
            {
                if (sid[i] == charToFind)
                {
                    dotPos = i;
                    break;
                }
            }

            return dotPos;
        }

        private static int GetEndPos(string sid, int length, int startPos)
        {
            const int maxLength = 56;
            var searchRange = Math.Min(length, maxLength + startPos);

            var endPos = -1;
            for (var i = searchRange - 1; i >= startPos; i--)
            {
                if (sid[i] == ParenthesesClose)
                {
                    endPos = i;
                    break;
                }
            }

            if (endPos > -1)
            {
                if (endPos < length - 1)
                {
                    for (var i = endPos + 1; i < length; i++)
                    {
                        if (!char.IsWhiteSpace(sid[i]))
                        {
                            endPos = -1;
                            break;
                        }
                    }
                }
            }
            return endPos;
        }

        private static int GetStartPos(string sid, int length)
        {
            var startPos = -1;
            for (var i = 0; i < length; i++)
            {
                if (!char.IsWhiteSpace(sid[i]))
                {
                    startPos = i;
                    break;
                }
            }

            return (startPos > -1 && sid[startPos] == ParenthesesOpen) ? startPos : -1;
        }
    }
}