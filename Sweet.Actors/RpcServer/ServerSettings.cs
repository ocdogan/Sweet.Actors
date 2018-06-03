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
using System.Net;
using System.Net.Sockets;

namespace Sweet.Actors
{
    public class ServerSettings
    {
        public const int AutoPort = 0;

        public const int MinConcurrentConnectionsCount = 10;
		public const int DefaultConcurrentConnectionsCount = 1024;

        private IPEndPoint _endPoint;

        private int _concurrentConnections = DefaultConcurrentConnectionsCount;

        private string _serializer = "default";

        public ServerSettings()
        {
            if (Socket.OSSupportsIPv4)
                _endPoint = new IPEndPoint(IPAddress.Any, AutoPort);
            else _endPoint = new IPEndPoint(IPAddress.IPv6Any, AutoPort);
        }

        public IPEndPoint EndPoint => _endPoint;

        public IPAddress Address => _endPoint?.Address ?? (Socket.OSSupportsIPv4 ? IPAddress.Any : IPAddress.IPv6Any);

        public int Port => _endPoint?.Port ?? AutoPort;

        public int ConcurrentConnections => _concurrentConnections; 

        public string Serializer => _serializer;

        public ServerSettings UsingIPAddress(IPAddress ipAddress)
        {
            if (ipAddress == null)
            {
                if (Socket.OSSupportsIPv4)
                    ipAddress = IPAddress.Any;
                else if (Socket.OSSupportsIPv6)
                    ipAddress = IPAddress.IPv6Any;
                else 
                    throw new Exception(Errors.InvalidAddress);
            }
            _endPoint = new IPEndPoint(ipAddress, _endPoint.Port);
            return this;
        }

        public ServerSettings UsingIPAddress(string ipAddress)
        {
            return UsingIPAddress(!String.IsNullOrEmpty(ipAddress) ? IPAddress.Parse(ipAddress) : null);
        }

        public ServerSettings UsingPort(int port)
        {
            port = Math.Max(0, port);
            if (port > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(Port));
            _endPoint = new IPEndPoint(_endPoint.Address, port);
            return this;
        }

        public ServerSettings UsingConcurrentConnections(int concurrentConnections)
        {
            _concurrentConnections = (concurrentConnections < 1) ? DefaultConcurrentConnectionsCount : 
                Math.Max(MinConcurrentConnectionsCount, concurrentConnections);
            return this;
        }

        public ServerSettings UsingSerializer(string serializer)
        {
            serializer = serializer?.Trim();
            _serializer = String.IsNullOrEmpty(serializer) ? "default" : serializer;
            return this;
        }

        public ServerSettings Clone()
        {
            var result = new ServerSettings();
            
            result._endPoint = new IPEndPoint(_endPoint.Address, _endPoint.Port);
            result._concurrentConnections = _concurrentConnections;
            result._serializer = _serializer;

            return result;
        }
    }
}
