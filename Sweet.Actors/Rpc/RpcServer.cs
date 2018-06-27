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
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public abstract partial class RpcServer : Disposable, IRemoteServer
    {
        protected struct LRUItem<T, K>
        {
            public K Key;
            public T Value;
        }

        private const int MaxBufferSize = 4 * Constants.KB;

        // States
        private int _stopping;
        private int _accepting;
        private long _status = RpcServerStatus.Stopped;

        private Socket _listener;
		private IPEndPoint _localEndPoint;
        private RpcServerOptions _options;

        private LRUItem<ActorSystem, string> _lastBinding;

        private ConcurrentDictionary<string, ActorSystem> _actorSystemBindings = new ConcurrentDictionary<string, ActorSystem>();
        private ConcurrentDictionary<RpcConnection, Socket> _receiveContexts = new ConcurrentDictionary<RpcConnection, Socket>();

        static RpcServer()
        {
            RpcSerializerRegistry.Register<DefaultRpcSerializer>(Constants.DefaultSerializerKey);
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("wire");
        }

        internal RpcServer(RpcServerOptions options = null)
        {
            _options = options?.Clone() ?? RpcServerOptions.Default;
        }

        protected override void OnDispose(bool disposing)
        {
            var bindings = Interlocked.Exchange(ref _actorSystemBindings, null);
            if (bindings != null)
            {
                foreach (var actorSystem in bindings.Values)
                    actorSystem.SetRemoteManager(null);
            }

            if (!disposing)
                TryToStop();
            else Stop();

            base.OnDispose(disposing);
        }

        protected RpcServerOptions Options => _options;

        public IPEndPoint EndPoint => _localEndPoint ?? _options?.EndPoint;

        public long Status
        {
            get
            {
                Interlocked.MemoryBarrier();
                if (_stopping == Constants.True)
                    return RpcServerStatus.Stopping;
                return Interlocked.Read(ref _status);
            }
        }

        public void Start()
        {
            Stop();
            StartListening();
        }

        protected bool TryGetBindedSystem(string actorSystem, out ActorSystem bindedSystem)
        {
            bindedSystem = null;
            if (actorSystem == _lastBinding.Key)
            {
                bindedSystem = _lastBinding.Value;
                return true;
            }

            var result = (_actorSystemBindings?.TryGetValue(actorSystem, out bindedSystem) ?? false);
            if (result)
                _lastBinding = new LRUItem<ActorSystem, string> {
                    Key = actorSystem,
                    Value = bindedSystem
                };
            return result;
        }

        public virtual bool Bind(ActorSystem actorSystem)
        {
            ThrowIfDisposed();

            if (actorSystem != null)
            {
                var actorSystemName = actorSystem.Name;
                if (TryGetBindedSystem(actorSystemName, out ActorSystem bindedSystem))
                    return bindedSystem == actorSystem;

                _actorSystemBindings[actorSystemName] = actorSystem;

                _lastBinding = new LRUItem<ActorSystem, string> {
                    Key = actorSystemName,
                    Value = actorSystem
                };

                return true;
            }
            return false;
        }

        public virtual bool Unbind(ActorSystem actorSystem)
        {
            ThrowIfDisposed();

            if (actorSystem != null &&
                TryGetBindedSystem(actorSystem.Name, out ActorSystem bindedSystem) && bindedSystem == actorSystem)
            {
                if (_lastBinding.Key == actorSystem.Name)
                    _lastBinding = new LRUItem<ActorSystem, string> { };

                return (_actorSystemBindings?.TryRemove(actorSystem.Name, out bindedSystem) ?? false);
            }
            return false;
        }

        private void StartListening()
        {
            if ((_stopping != Constants.True) &&
                Common.CompareAndSet(ref _status, RpcServerStatus.Stopped, RpcServerStatus.Starting))
            {
                var servingEP = _options.EndPoint;

                Socket listener = null;
                try
                {
                    var addressFamily = servingEP.AddressFamily;
                    if (addressFamily == AddressFamily.Unknown || addressFamily == AddressFamily.Unspecified)
                        addressFamily = IPAddress.Any.AddressFamily;

                    listener = new NativeSocket(addressFamily, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                    Configure(listener);
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _status, RpcServerStatus.Stopped);

                    using (listener) { }
                    throw;
                }

                Interlocked.Exchange(ref _listener, listener);
                try
                {
                    listener.Bind(servingEP);
                    listener.Listen(_options.ConcurrentConnections);

                    var localEP = (listener.LocalEndPoint as IPEndPoint) ?? 
                        (IPEndPoint)listener.LocalEndPoint;

                    Interlocked.Exchange(ref _localEndPoint, localEP);
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _status, RpcServerStatus.Stopped);

                    TryToCloseSocket(listener);
                    throw;
                }

                Interlocked.Exchange(ref _status, RpcServerStatus.Started);

                StartAccepting(NewSocketAsyncEventArgs());
            }
        }

        private void Configure(Socket socket)
        {
            socket.SetIOLoopbackFastPath();

            if (_options.SendTimeoutMSec > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout,
                                        _options.SendTimeoutMSec == int.MaxValue ? Timeout.Infinite : _options.SendTimeoutMSec);
            }

            if (_options.ReceiveTimeoutMSec > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout,
                                        _options.ReceiveTimeoutMSec == int.MaxValue ? Timeout.Infinite : _options.ReceiveTimeoutMSec);
            }

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            socket.NoDelay = true;
        }

        private SocketAsyncEventArgs NewSocketAsyncEventArgs()
        {
            var result = new SocketAsyncEventArgs {
                UserToken = this
            };
            result.Completed += OnAcceptCompleted;

            return result;
        }

        private bool TryToStop()
        {
            try
            {
                Stop();
                return true;
            }
            catch (Exception)
            { }
            return false;
        }

        public void Stop()
        {
            if (Common.CompareAndSet(ref _stopping, false, true))
            {
                try
                {
                    var listener = Interlocked.Exchange(ref _listener, null);
                    CloseSocket(listener);

					Interlocked.Exchange(ref _localEndPoint, null);

                    Interlocked.Exchange(ref _accepting, Constants.False);
                    Interlocked.Exchange(ref _status, RpcServerStatus.Stopped);
                }
                finally
                {
                    Interlocked.Exchange(ref _stopping, Constants.False);
                }
            }
        }

        protected static void CloseSocket(Socket socket)
        {
            if (socket != null)
            {
                using (socket)
                    socket.Close();
            }
        }

        private static bool TryToCloseSocket(Socket socket)
        {
            if (socket != null)
            {
                try
                {
                    using (socket)
                        socket.Close();
                }
                catch (Exception)
                {
                    return false;
                }
            }
            return true;
        }

        private void StartAccepting(SocketAsyncEventArgs eventArgs)
        {
            Interlocked.Exchange(ref _accepting, Constants.True);
            try
            {
                eventArgs.AcceptSocket = null;

                var listener = _listener;
                while (!listener.AcceptAsync(eventArgs))
                {
                    Interlocked.Exchange(ref _accepting, Constants.True);
                    HandleAccept(eventArgs, true);
                }
            }
            catch (Exception)
            {
                StartListening();
            }
        }

        private static void OnAcceptCompleted(object sender, SocketAsyncEventArgs eventArgs)
        {
            ((RpcServer)eventArgs.UserToken).HandleAccept(eventArgs, false);
        }

        private void HandleAccept(SocketAsyncEventArgs acceptEventArgs, bool completedSynchronously)
        {
            Interlocked.Exchange(ref _accepting, Constants.False);
            try
            {
                if (acceptEventArgs.SocketError != SocketError.Success)
                {
                    StartListening();
                    return;
                }

                if (!(acceptEventArgs.UserToken is RpcServer server))
                    return;

                var socket = acceptEventArgs.AcceptSocket;
                if (socket.Connected)
                {
                    Task.Factory.StartNew(() => {
                        StartReceiveAsync(socket);
                    }).Ignore();
                }

                if (completedSynchronously)
                    return;

                StartAccepting(acceptEventArgs);
            }
            catch (Exception)
            {
                StartListening();
            }
        }

        private void StartReceiveAsync(Socket clientSocket)
        {
            if (clientSocket != null)
            {
                RpcConnection receiveCtx = null;
                try
                {
                    receiveCtx = new RpcConnection(this, clientSocket, HandleMessage, SendMessage);
                    receiveCtx.OnDisconnect += ContextDisconnected;

                    _receiveContexts[receiveCtx] = clientSocket;

                    if (!receiveCtx.StartReceiveAsync())
                        throw new Exception(RpcErrors.CannotStartToReceive);
                }
                catch (Exception)
                {
                    ContextDisconnected(receiveCtx, EventArgs.Empty);
                    throw;
                }
            }
        }

        private void ContextDisconnected(object sender, EventArgs e)
        {
            if (sender is RpcConnection receiveCtx)
            {
                receiveCtx.OnDisconnect -= ContextDisconnected;

                _receiveContexts.TryRemove(receiveCtx, out Socket socket);
                CloseConnection(socket);

                if (!receiveCtx.Disposed)
                    receiveCtx.Dispose();
            }
        }

        private void CloseConnection(Socket clientSocket)
        {
            if (clientSocket != null)
            {
                if ((clientSocket is NativeSocket nativeSocket) &&
                    nativeSocket.Disposed)
                    return;

                using (clientSocket)
                    clientSocket.Close();
            }
        }

        protected virtual void ClearConnections()
        {
            _receiveContexts.Clear();
        }

        private byte[] ReadData(Socket clientSocket, int expected)
        {
            if (expected < 1)
                return null;

            if (expected > MaxBufferSize)
                expected = MaxBufferSize;

            var buffer = new byte[expected];

            int offset = 0;
            while (offset < buffer.Length)
            {
                try
                {
                    int bytesRead = clientSocket.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
                    if (bytesRead == 0)
                        return null;

                    offset += bytesRead;
                }
                catch (Exception)
                {
                    return null;
                }
            }
            return buffer;
        }

        protected abstract Task HandleMessage(RemoteMessage remoteMessage, IRpcConnection rpcConnection);

        protected abstract Task SendMessage(WireMessage wireMessage, IRpcConnection rpcConnection);
    }
}
