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
using System.Collections.Generic;
using System.Collections.ObjectModel;
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

        private RpcMessageWriter _writer;
        private IResponseHandler _responseHandler;

        private ConcurrentDictionary<RemoteEndPoint, RpcManagedClient> _clients = 
            new ConcurrentDictionary<RemoteEndPoint, RpcManagedClient>();

        public RpcManager(RpcServerOptions options = null)
            : base(options)
        {
            _writer = new RpcMessageWriter(Options.Serializer);
        }

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

        protected override Task HandleMessage((IMessage, Aid, WireMessageId) receivedMsg, IRpcConnection connection)
        {
            var message = receivedMsg.Item1;
            if (message == null)
                throw new Exception(RpcErrors.InvalidMessage);

            var to = receivedMsg.Item2;
            if (to == null || to == Aid.Unknown)
                throw new Exception(RpcErrors.InvalidMessageReceiver);

            var actor = to.Actor?.Trim();
            var actorSystem = to.ActorSystem?.Trim();

            if (String.IsNullOrEmpty(actor) ||
                String.IsNullOrEmpty(actorSystem) ||
                !TryGetBindedSystem(actorSystem, out ActorSystem bindedSystem))
                throw new Exception(RpcErrors.InvalidMessageReceiver);

            var pid = bindedSystem.Get(actor);
            if (pid == null || pid == Aid.Unknown)
                throw new Exception(RpcErrors.InvalidMessageReceiver);

            if (!(message is FutureMessage))
                return pid.Tell(message);

            var header = message.Header as IDictionary<string, string>;
            if (header == null && message.Header != null)
            {
                header = new Dictionary<string, string>();
                foreach (var kv in message.Header)
                    header.Add(kv);
            }

            var response = pid.Request(message.Data, header);
            if (response == null)
                throw new Exception(RpcErrors.InvalidMessageResponse);

            return response.ContinueWith((t) => {
                SendMessage((t.Result, receivedMsg.Item2, receivedMsg.Item3), connection);
            });
        }

        /* public Task Send(IMessage message, Aid to)
        {
            ThrowIfDisposed();

            if (message == null)
                return Task.FromException(new ArgumentNullException(nameof(message)));

            var tcs = new TaskCompletionSource<object>();

            _requestQueue.Enqueue(new Request
            {
                Id = RpcMessageId.Next(),
                To = to,
                Message = message,
                TaskCompletionSource = tcs
            });

            Schedule();
            return tcs.Task;
        } */

        protected override Task SendMessage((IMessage, Aid, WireMessageId) receivedMsg, IRpcConnection connection)
        {
            var socket = connection.Connection;
            if (socket.IsConnected())
            {
                var writeEventArgs = AcquireSocketAsyncEventArgs(socket);
                
            }
            return Receive.Completed;
        }
    }
}