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
using System.Linq;
using System.Net;
using System.Net.Sockets;

namespace Sweet.Actors
{
    public class ExtEndPoint : EndPoint, IEquatable<ExtEndPoint>, ICloneable
    {
        #region IPAddressEntry

        private class IPAddressEntry
        {
            #region .Ctors

            public IPAddressEntry(string host, IPAddress[] ipAddresses, bool eternal = false)
            {
                Eternal = eternal;
                Host = host;
                IPAddresses = ipAddresses;
                CreationDate = DateTime.UtcNow;
            }

            #endregion .Ctors

            #region Properties

            public bool Eternal { get; private set; }

            public string Host { get; private set; }

            public IPAddress[] IPAddresses { get; private set; }

            public DateTime CreationDate { get; private set; }

            public bool Expired
            {
                get { return !Eternal && (DateTime.UtcNow - CreationDate).TotalSeconds >= 30d; }
            }

            #endregion Properties

            #region Methods

            public void SetIPAddresses(IPAddress[] ipAddresses, bool eternal = false)
            {
                Eternal = eternal;
                IPAddresses = ipAddresses;
                CreationDate = DateTime.UtcNow;
            }

            #endregion Methods
        }

        #endregion IPAddressEntry

        #region Static Members

        public static readonly ExtEndPoint Empty = new ExtEndPoint("", -1);

        public static readonly ExtEndPoint LocalHostEndPoint = new ExtEndPoint(Constants.LocalHost, Constants.DefaultPort);
        public static readonly ExtEndPoint IP4LoopbackEndPoint = new ExtEndPoint(Constants.IP4Loopback, Constants.DefaultPort);
        public static readonly ExtEndPoint IP6LoopbackEndPoint = new ExtEndPoint(Constants.IP6Loopback, Constants.DefaultPort);

        public static readonly HashSet<IPAddress> LocalIPs = new HashSet<IPAddress>(new[] { IPAddress.Loopback, IPAddress.IPv6Loopback });

        private static readonly IPAddress[] EmptyAddresses = new IPAddress[0];

        private static readonly SynchronizedDictionary<string, IPAddressEntry> s_DnsEntries =
            new SynchronizedDictionary<string, IPAddressEntry>();

        #endregion Static Members

        #region Field Members

        private int _port;
        private string _host;

        private IPAddressEntry _entry;
        private AddressFamily _addressFamily = AddressFamily.Unknown;

        #endregion Field Members

        #region .Ctors

        static ExtEndPoint()
        {
            try
            {
                var hostNameIPs = Dns.GetHostAddresses(Dns.GetHostName());
                if (hostNameIPs != null)
                    LocalIPs.UnionWith(hostNameIPs);
            }
            catch (Exception)
            { }
        }

        public ExtEndPoint(string host, int port)
        {
            _host = host ?? String.Empty;
            _port = port;
        }

        public ExtEndPoint(IPAddress ipAddress, int port)
        {
            _addressFamily = ipAddress?.AddressFamily ?? AddressFamily.Unknown;

            _host = ipAddress?.ToString() ?? String.Empty;
            _port = port;
        }

        #endregion .Ctors

        #region Properties

        public string Host
        {
            get { return _host; }
            private set { _host = value ?? String.Empty; }
        }

        public int Port
        {
            get { return _port; }
            private set { _port = value; }
        }

        public bool IsEmpty
        {
            get { return _host.IsEmpty() || _port < 1; }
        }

        public override AddressFamily AddressFamily
        {
            get
            {
                if (_addressFamily == AddressFamily.Unknown)
                {
                    var entry = GetEntry(_host);

                    if ((entry != null) && !entry.IPAddresses.IsEmpty())
                        _addressFamily = entry.IPAddresses[0].AddressFamily;
                    else if (Socket.OSSupportsIPv4)
                        _addressFamily = AddressFamily.InterNetwork;
                    else _addressFamily = AddressFamily.InterNetworkV6;
                }
                return _addressFamily;
            }
        }

        #endregion Properties

        #region Methods

        #region Overrides

        public override string ToString()
        {
            return String.Format("{0}:{1}", Host, Port);
        }

        public override int GetHashCode()
        {
            var hash = 13;
            hash = (hash * 7) + (Host ?? String.Empty).GetHashCode();
            hash = (hash * 7) + Port.GetHashCode();
            return hash;
        }

        public override bool Equals(object obj)
        {
            if (obj is null)
                return false;

            if (ReferenceEquals(obj, this))
                return true;

            var other = obj as ExtEndPoint;
            if (!(other is null))
                return _port == other._port &&
                     String.Equals(_host, other._host, StringComparison.OrdinalIgnoreCase);

            var ipEP = obj as IPEndPoint;
            if (!(ipEP is null))
                return _port == ipEP.Port &&
                     String.Equals(_host, ipEP.Address.ToString(), StringComparison.OrdinalIgnoreCase);

            var dnsEP = obj as DnsEndPoint;
            if (!(dnsEP is null))
                return _port == dnsEP.Port &&
                     String.Equals(_host, dnsEP.Host, StringComparison.OrdinalIgnoreCase);

            return false;
        }

        public bool Equals(ExtEndPoint other)
        {
            if (other is null)
                return false;

            if (ReferenceEquals(other, this))
                return true;

            return Port == other.Port &&
                 String.Equals(Host, other.Host, StringComparison.OrdinalIgnoreCase);
        }

        #endregion Overrides

        public IPAddress[] ResolveHost()
        {
            var entry = _entry;
            if (entry == null || entry.Expired)
                entry = _entry = GetEntry(Host);

            return (entry == null) ? EmptyAddresses :
                (entry.IPAddresses ?? EmptyAddresses);
        }

        private static IPAddressEntry GetEntry(string host)
        {
            if (host.IsEmpty())
                return null;

            if (s_DnsEntries.TryGetValue(host, out IPAddressEntry entry) && !entry.Expired)
                return entry;

            lock (((ICollection)s_DnsEntries).SyncRoot)
            {
                if (s_DnsEntries.TryGetValue(host, out entry) && !entry.Expired)
                    return entry;

                var isIp = false;

                IPAddress[] ipAddresses = null;
                if (host.Equals(Constants.LocalHost, StringComparison.OrdinalIgnoreCase))
                {
                    if (Socket.OSSupportsIPv4)
                    {
                        isIp = true;
                        ipAddresses = new[] { IPAddress.Parse(Constants.IP4Loopback) };
                    }
                    else if (Socket.OSSupportsIPv6)
                    {
                        isIp = true;
                        ipAddresses = new[] { IPAddress.Parse(Constants.IP6Loopback) };
                    }
                }

                if (!isIp)
                {
                    isIp = IPAddress.TryParse(host, out IPAddress ipAddress);

                    ipAddresses = isIp ? new[] { ipAddress } :
                        AsyncEx.GetHostAddressesAsync(host).Result;

                    if (!ipAddresses.IsEmpty())
                    {
                        isIp = isIp ||
                            ipAddresses.All(ip => IPAddress.IsLoopback(ip) || LocalIPs.Contains(ip));

                        if (ipAddresses.Length > 1)
                        {
                            ipAddresses = ipAddresses
                                .OrderBy((addr) =>
                                { return addr.AddressFamily == AddressFamily.InterNetwork ? -1 : 1; })
                                .ToArray();
                        }
                    }
                }

                if (entry != null)
                    entry.SetIPAddresses(ipAddresses ?? EmptyAddresses, isIp);
                else
                    s_DnsEntries[host] = entry = new IPAddressEntry(host, ipAddresses ?? EmptyAddresses, isIp);
            }
            return entry;
        }

        public static HashSet<IPEndPoint> ToIPEndPoints(ExtEndPoint[] endPoints)
        {
            if (endPoints.IsEmpty())
                return null;

            var ipEPList = new HashSet<IPEndPoint>();
            foreach (var ep in endPoints)
            {
                if (!ep.IsEmpty())
                {
                    try
                    {
                        var ipAddresses = ep.ResolveHost();
                        if (ipAddresses != null)
                        {
                            var length = ipAddresses.Length;
                            if (length > 0)
                            {
                                for (var i = 0; i < length; i++)
                                    ipEPList.Add(new IPEndPoint(ipAddresses[i], ep.Port));
                            }
                        }
                    }
                    catch (Exception)
                    { }
                }
            }

            return ipEPList;
        }

        public object Clone()
        {
            if (ReferenceEquals(this, Empty))
                return this;
            return new ExtEndPoint(Host, Port);
        }

        #endregion Methods

        #region Operator Overloads

        public static bool operator ==(ExtEndPoint a, ExtEndPoint b)
        {
            if (a is null)
                return b is null;

            return a.Equals(b);
        }

        public static bool operator !=(ExtEndPoint a, ExtEndPoint b)
        {
            return !(a == b);
        }

        public static bool operator ==(ExtEndPoint a, IPEndPoint b)
        {
            if (a is null)
                return b is null;

            return a.Equals(b);
        }

        public static bool operator !=(ExtEndPoint a, IPEndPoint b)
        {
            return !(a == b);
        }

        public static bool operator ==(IPEndPoint a, ExtEndPoint b)
        {
            return (b == a);
        }

        public static bool operator !=(IPEndPoint a, ExtEndPoint b)
        {
            return !(b == a);
        }

        public static bool operator ==(ExtEndPoint a, DnsEndPoint b)
        {
            if (a is null)
                return b is null;

            return a.Equals(b);
        }

        public static bool operator !=(ExtEndPoint a, DnsEndPoint b)
        {
            return !(a == b);
        }

        public static bool operator ==(DnsEndPoint a, ExtEndPoint b)
        {
            return (b == a);
        }

        public static bool operator !=(DnsEndPoint a, ExtEndPoint b)
        {
            return !(b == a);
        }

        #endregion Operator Overloads
    }
}