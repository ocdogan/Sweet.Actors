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
    public sealed class RpcMessageId : Id<RpcMessage>
    {
        public static readonly RpcMessageId Empty = new RpcMessageId(-1L, -1L, -1L, -1L, 0);

        private RpcMessageId(long major, long majorRevision, long minor, long minorRevision, int processId = -1)
            : base(major, majorRevision, minor, minorRevision, processId)
        { }

        public static string Next()
        {
            var buffer = Generate();
            return $"[{Common.ProcessId}-{buffer[0]}.{buffer[1]}.{buffer[2]}.{buffer[3]}]";;
        }

        public static bool TryParse(string sid, out RpcMessageId id)
        {
            id = RpcMessageId.Empty;
            
            sid = sid?.Trim();
            if (!String.IsNullOrEmpty(sid) && sid.Length >= "[0-0.0.0.0]".Length &&
                sid[0] == '[' && sid[sid.Length-1] == ']')
            {
                sid = sid.Substring(1, sid.Length-2);
                
                var pidPos = sid.IndexOf('-');
                if (pidPos > 0)
                {
                    int pid;
                    if (int.TryParse(sid.Substring(0, pidPos), out pid))
                    {                        
                        var parts = sid.Substring(pidPos + 1).Split('.');
                        if (parts != null && parts.Length == 4)
                        {
                            long major;
                            if (long.TryParse(parts[0], out major))
                            {
                                long majorRevision;
                                if (long.TryParse(parts[1], out majorRevision))
                                {
                                    long minor;
                                    if (long.TryParse(parts[2], out minor))
                                    {
                                        long minorRevision;
                                        if (long.TryParse(parts[3], out minorRevision))
                                        {
                                            id = new RpcMessageId(major, majorRevision, minor, minorRevision, pid);
                                            return true;
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
    }
}