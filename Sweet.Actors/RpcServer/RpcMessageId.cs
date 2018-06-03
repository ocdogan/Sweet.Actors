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