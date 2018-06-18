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
using System.Collections.Concurrent;
using System.Net;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class RpcManager : RpcServer, IRemoteManager
    {
        private const int DefaultTimeout = 30000;

        private class EndPointResolver
        {
            private bool _isIPAddress;
            private string _host;
            private int _timeoutMSec;
            private int _resolveTimeMSec;
            private IPAddress[] _addresses;

            public EndPointResolver(string host, int timeoutMSec = -1)
            {
                _host = host?.Trim();
                if (_isIPAddress = IPAddress.TryParse(host, out IPAddress address))
                    _addresses = new IPAddress[] { address };
                _timeoutMSec = timeoutMSec < 1 ? DefaultTimeout : timeoutMSec;
            }

            public IPAddress[] Resolve()
            {
                if (!_isIPAddress)
                {
                    if (_resolveTimeMSec == 0)
                    {
                        var entry = Dns.GetHostEntry(_host);
                        _resolveTimeMSec = Environment.TickCount;

                        return (_addresses = entry.AddressList);
                    }

                    if (_timeoutMSec > 0)
                    {
                        var now = Environment.TickCount;
                        if (now - _resolveTimeMSec > _timeoutMSec)
                        {
                            var entry = Dns.GetHostEntry(_host);
                            _resolveTimeMSec = now;

                            return (_addresses = entry.AddressList);
                        }
                    }
                }
                return _addresses;
            }
        }

        private class RpcManagedClient : RpcClient
        {
            private int _creationTime;

            public RpcManagedClient(RpcClientOptions options)
                : base(options)
            {
                _creationTime = Environment.TickCount;
            }
        }

        private static readonly ConcurrentDictionary<string, EndPointResolver> _resolvers =
            new ConcurrentDictionary<string, EndPointResolver>();

        private IResponseHandler _responseHandler;
        private ConcurrentDictionary<RemoteEndPoint, RpcManagedClient> _clients = 
            new ConcurrentDictionary<RemoteEndPoint, RpcManagedClient>();

        public RpcManager(RpcServerOptions options = null)
            : base(options)
        { }

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var client in _clients.Values)
                    using (client) { }
            }
            base.OnDispose(disposing);
        }

        public void SetResponseHandler(IResponseHandler handler)
        {
            _responseHandler = handler;
        }

        private RpcClient GetClient(RemoteEndPoint endPoint)
        {
            if (endPoint == null)
                throw new ArgumentNullException(nameof(endPoint));

            return _clients.GetOrAdd(endPoint, NewClient);
        }

        private static RpcManagedClient NewClient(RemoteEndPoint endPoint)
        {
            var addresses = _resolvers.GetOrAdd(endPoint.Host,
                                (host) => new EndPointResolver(host)).Resolve();

            if (addresses.IsEmpty())
                return new RpcManagedClient(null);

            var settings = new RpcClientOptions().
                UsingEndPoint(new IPEndPoint(addresses[0], endPoint.Port));

            return new RpcManagedClient(settings);
        }

        public Task Send(IMessage message, RemoteAddress to)
        {
            try
            {
                ThrowIfDisposed();

                if (message == null)
                    throw new ArgumentNullException(nameof(message));

                if (to == null)
                    throw new ArgumentNullException(nameof(to));

                var client = GetClient(to.EndPoint);
                if (client == null)
                    throw new Exception(RpcErrors.CannotResolveEndPoint);

                return client.Send(message, to.Actor);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        public override bool Bind(ActorSystem actorSystem)
        {
            if (base.Bind(actorSystem))
            {
                actorSystem.SetRemoteManager(this);
                return true;
            }
            return false;
        }

        public override bool Unbind(ActorSystem actorSystem)
        {
            if (base.Unbind(actorSystem))
            {
                actorSystem.SetRemoteManager(null);
                return true;
            }
            return false;
        }

        protected override void HandleMessage((IMessage, Aid) receivedMsg, IRpcConnection connection)
        {
            var to = receivedMsg.Item2;
            if (to != null)
            {
                var msg = receivedMsg.Item1;
            }
        }

        protected override void SendMessage(IMessage message, IRpcConnection connection)
        {
            throw new NotImplementedException();
        }
    }
}