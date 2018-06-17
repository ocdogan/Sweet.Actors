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
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal class RpcClientManager : Disposable, IRemoteManager
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

        private class RpcMockClient
        {
            private RpcClient _client;
            private int _creationTime;

            public RpcMockClient(RpcClient client)
            {
                _client = client;
                _creationTime = Environment.TickCount;
            }

            public RpcClient Client => _client;

            public bool Valid => (_client == null) && 
                (Environment.TickCount - _creationTime < DefaultTimeout);
        }

        private static readonly ConcurrentDictionary<string, EndPointResolver> _resolvers = 
            new ConcurrentDictionary<string, EndPointResolver>();
        
        private IResponseHandler _responseHandler;
        private ConcurrentDictionary<RemoteEndPoint, RpcMockClient> _clients = new ConcurrentDictionary<RemoteEndPoint, RpcMockClient>();

        protected override void OnDispose(bool disposing)
        {
            if (disposing)
            {
                foreach (var c in _clients.Values)
                    using (c.Client) {}
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

            var record = _clients.GetOrAdd(endPoint, NewClient);
            if (record.Valid)
                return record.Client;
            return (_clients[endPoint] = NewClient(endPoint)).Client;
        }   

        private static RpcMockClient NewClient(RemoteEndPoint endPoint)
        {
            var addresses = _resolvers.GetOrAdd(endPoint.Host, 
                                (host) => new EndPointResolver(host)).Resolve();
            
            if (addresses.IsEmpty())
                return new RpcMockClient(null);

            var settings = new RpcClientSettings().
                UsingEndPoint(new IPEndPoint(addresses[0], endPoint.Port));

            return new RpcMockClient(new RpcClient(settings));
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

                return client.Send(message, null);
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }
    }
}