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
    public sealed class WireMessageId : Id<WireMessage>
    {
        public static readonly WireMessageId Empty = new WireMessageId(0, 0, 0, 0, 0);

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
                            int dashPos = GetPositionOf('-', sid, startPos, Math.Min(startPos + 11, endPos));

                            if (dashPos > -1 &&
                                TryParseInt(sid, ref startPos, dashPos - startPos, out int pid))
                            {
                                startPos++;
                                var dotPos = GetPositionOf('.', sid, startPos, Math.Min(startPos + 11, endPos));

                                if (dotPos > -1 &&
                                    TryParseInt(sid, ref startPos, dotPos - startPos, out int major))
                                {
                                    startPos++;
                                    dotPos = GetPositionOf('.', sid, startPos, Math.Min(startPos + 11, endPos));

                                    if (dotPos > -1 &&
                                        TryParseInt(sid, ref startPos, dotPos - startPos, out int majorRevision))
                                    {
                                        startPos++;
                                        dotPos = GetPositionOf('.', sid, startPos, Math.Min(startPos + 11, endPos));

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
                var minus = (sid[startPos] == '-');
                if (minus)
                    startPos++;

                var digit = 1;
                for (var i = startPos + length - 1; i >= startPos; i--)
                {
                    if (!char.IsNumber(sid[i]))
                        return false;

                    value += (sid[i] - '0') * digit;
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
                if (sid[i] == ']')
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

            return (startPos > -1 && sid[startPos] == '[') ? startPos : -1;
        }
    }
}