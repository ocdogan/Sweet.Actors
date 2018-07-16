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

        private WireMessageId(long major, long majorRevision, long minor, long minorRevision, int processId = -1)
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
            return $"[{Common.ProcessId}-{buffer[0]}.{buffer[1]}.{buffer[2]}.{buffer[3]}]";
        }

        public static bool TryParse(string sid, out WireMessageId id)
        {
            id = Empty;

            sid = sid?.Trim();
            if (!String.IsNullOrEmpty(sid) && sid.Length >= EmptyLength &&
                sid[0] == '[' && sid[sid.Length - 1] == ']')
            {
                sid = sid.Substring(1, sid.Length - 2);

                var pidPos = sid.IndexOf('-');
                if (pidPos > 0 &&
                    int.TryParse(sid.Substring(0, pidPos), out int pid))
                {
                    var parts = sid.Substring(pidPos + 1).Split('.');

                    if (parts != null && parts.Length == 4 &&
                        long.TryParse(parts[0], out long major) &&
                        long.TryParse(parts[1], out long majorRevision) &&
                        long.TryParse(parts[2], out long minor) &&
                        long.TryParse(parts[3], out long minorRevision))
                    {
                        id = new WireMessageId(major, majorRevision, minor, minorRevision, pid);
                        return true;
                    }
                }
            }
            return false;
        }
    }
}