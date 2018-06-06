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
    public abstract class RpcSettings<T>
        where T : RpcSettings<T>
    {
        private IPEndPoint _endPoint;

        private int _sendTimeoutMSec = RpcConstants.DefaultSendTimeout;
        private int _receiveTimeoutMSec = RpcConstants.DefaultReceiveTimeout;

        private string _serializer = "default";

        protected RpcSettings()
        {
            if (Socket.OSSupportsIPv4)
                _endPoint = new IPEndPoint(IPAddress.Any, Constants.DefaultPort);
            else _endPoint = new IPEndPoint(IPAddress.IPv6Any, Constants.DefaultPort);
        }

        public IPEndPoint EndPoint => _endPoint;

        public IPAddress Address => _endPoint?.Address ?? (Socket.OSSupportsIPv4 ? IPAddress.Any : IPAddress.IPv6Any);

        public int Port => _endPoint?.Port ?? Constants.DefaultPort;

        public string Serializer => _serializer;

        public int SendTimeoutMSec => _sendTimeoutMSec;

        public int ReceiveTimeoutMSec => _receiveTimeoutMSec;

        public T UsingEndPoint(IPEndPoint ipEndPoint)
        {
            var ipAddress = ipEndPoint?.Address;
            var port = ipEndPoint?.Port ?? Constants.DefaultPort;

            if (ipAddress == null)
            {
                if (Socket.OSSupportsIPv4)
                    ipAddress = IPAddress.Any;
                else if (Socket.OSSupportsIPv6)
                    ipAddress = IPAddress.IPv6Any;
                else
                    throw new Exception(Errors.InvalidAddress);
            }
            _endPoint = new IPEndPoint(ipAddress, port);
            return (T)this;
        }

        public T UsingIPAddress(IPAddress ipAddress)
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
            return (T)this;
        }

        public T UsingIPAddress(string ipAddress)
        {
            return UsingIPAddress(!String.IsNullOrEmpty(ipAddress) ? IPAddress.Parse(ipAddress) : null);
        }

        public T UsingPort(int port)
        {
            port = Math.Max(0, port);
            if (port > IPEndPoint.MaxPort)
                throw new ArgumentOutOfRangeException(nameof(Port));
            _endPoint = new IPEndPoint(_endPoint.Address, port);
            return (T)this;
        }

        public T UsingSerializer(string serializer)
        {
            serializer = serializer?.Trim();
            _serializer = String.IsNullOrEmpty(serializer) ? "default" : serializer;
            return (T)this;
        }

        public T UsingReceiveTimeoutMSec(int receiveTimeoutMSec)
        {
            if (receiveTimeoutMSec < 1)
                _receiveTimeoutMSec = RpcConstants.DefaultReceiveTimeout;
            else _receiveTimeoutMSec = Math.Min(RpcConstants.MaxReceiveTimeout, Math.Max(RpcConstants.MinReceiveTimeout, receiveTimeoutMSec));

            return (T)this;
        }

        public T UsingSendTimeoutMSec(int sendTimeoutMSec)
        {
            if (sendTimeoutMSec < 1)
                _sendTimeoutMSec = RpcConstants.DefaultSendTimeout;
            else _sendTimeoutMSec = Math.Min(RpcConstants.MaxSendTimeout, Math.Max(RpcConstants.MinSendTimeout, sendTimeoutMSec));

            return (T)this;
        }

        protected abstract T NewInstance();

        public virtual T Clone()
        {
            var result = NewInstance();

            result._endPoint = new IPEndPoint(_endPoint.Address, _endPoint.Port);
            result._serializer = _serializer;

            return result;
        }
    }
}
