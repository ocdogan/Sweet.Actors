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
    public abstract class RpcServer : Disposable, IRemoteServer
    {
        private class ReceiveContext : Disposable, IRpcConnection
        {
            private RpcServer _server;
            private Socket _connection;
            private IPEndPoint _remoteEndPoint;
            private readonly RpcReceiveBuffer _buffer;

            public ReceiveContext(RpcServer server, Socket connection)
            {
                _server = server;
                _connection = connection;
                _remoteEndPoint = (_connection?.RemoteEndPoint as IPEndPoint);
                _buffer = new RpcReceiveBuffer();
            }

            public Socket Connection => _connection;

            public IPEndPoint RemoteEndPoint => _remoteEndPoint;

            public IRemoteServer Server => _server;

            protected override void OnDispose(bool disposing)
            {
                _server = null;

                var connection = Interlocked.Exchange(ref _connection, null);
                if (connection != null)
                    using (connection)
                    {
                        if (connection.IsConnected())
                        {
                            connection.Shutdown(SocketShutdown.Both);
                            connection.Close();
                        }
                    }                
            }

            public void ProcessReceived(SocketAsyncEventArgs eventArgs)
            {
                try
                {
                    if (_buffer.OnReceiveData(eventArgs.Buffer, eventArgs.Offset, eventArgs.BytesTransferred))
                    {
                        RemoteMessage remoteMsg = null;
                        try
                        {
                            if (_buffer.TryGetMessage(out remoteMsg))
                                _server.HandleMessage(remoteMsg, this);
                        }
                        catch (Exception e)
                        {
                            var message = remoteMsg.Message;
                            if ((message != null) && (message.MessageType == MessageType.FutureMessage))
                            {
                                var response = message.ToWireMessage(remoteMsg.To, remoteMsg.MessageId, e);
                                _server.SendMessage(response, this);
                                return;
                            }
                            throw;
                        }
                    }
                }
                catch (Exception)
                {
                    _buffer.Dispose();
                    throw;
                }
            }
        }

        private const int MaxBufferSize = 4 * Constants.KB;
        private static readonly LingerOption NoLingerState = new LingerOption(true, 0);

        // States
        private int _stopping;
        private int _accepting;
        private long _status = RpcServerStatus.Stopped;

        private Socket _listener;
		private IPEndPoint _localEndPoint;
        private RpcServerOptions _options;

        private ConcurrentDictionary<Socket, object> _receivingSockets = new ConcurrentDictionary<Socket, object>();
        private readonly ConcurrentDictionary<string, ActorSystem> _actorSystemBindings = new ConcurrentDictionary<string, ActorSystem>();

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
            if (_actorSystemBindings.Count > 0)
            {
                var actorSystems = _actorSystemBindings.Values.ToArray();
                _actorSystemBindings.Clear();

                foreach (var actorSystem in actorSystems)
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
            return !Disposed && _actorSystemBindings.TryGetValue(actorSystem, out bindedSystem);
        }

        public virtual bool Bind(ActorSystem actorSystem)
        {
            ThrowIfDisposed();

            if (actorSystem != null &&
                (!_actorSystemBindings.TryGetValue(actorSystem.Name, out ActorSystem bindedSystem) || 
                bindedSystem == actorSystem))
            {
                _actorSystemBindings[actorSystem.Name] = actorSystem;
                return true;
            }
            return false;
        }

        public virtual bool Unbind(ActorSystem actorSystem)
        {
            ThrowIfDisposed();

            if (actorSystem != null &&
                _actorSystemBindings.TryGetValue(actorSystem.Name, out ActorSystem bindedSystem) &&
                bindedSystem == actorSystem)
                return _actorSystemBindings.TryRemove(actorSystem.Name, out bindedSystem);

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

        private void HandleAccept(SocketAsyncEventArgs eventArgs, bool completedSynchronously)
        {
            Interlocked.Exchange(ref _accepting, Constants.False);
            try
            {
                if (eventArgs.SocketError != SocketError.Success)
                {
                    StartListening();
                    return;
                }

                if (!(eventArgs.UserToken is RpcServer server))
                    return;

                var connection = eventArgs.AcceptSocket;
                if (connection.Connected)
                {
                    connection.LingerState = NoLingerState;

                    Task.Factory.StartNew(() =>
                    {
                        if (!server.RegisterConnection(connection))
                            server.CloseConnection(connection);
                        else
                        {
                            var readEventArgs = AcquireReceiveEventArgs(connection);
                            StartReceiveAsync(connection, readEventArgs);
                        }
                    }).Ignore();
                }

                if (completedSynchronously)
                    return;

                StartAccepting(eventArgs);
            }
            catch (Exception)
            {
                StartListening();
            }
        }

        private void StartReceiveAsync(Socket clientConnection, SocketAsyncEventArgs eventArgs)
        {
            try
            {
                if (!clientConnection.ReceiveAsync(eventArgs))
                    ProcessReceived(eventArgs);
            }
            catch (Exception)
            {
                CloseSocket(clientConnection);
                ReleaseReceiveEventArgs(eventArgs);
            }
        }

        private SocketAsyncEventArgs AcquireReceiveEventArgs(Socket clientConnection)
        {
            var result = SocketAsyncEventArgsCache.Default.Acquire();

            result.Completed += OnReceiveCompleted;
            result.UserToken = new ReceiveContext(this, clientConnection);

            return result;
        }

        private void ReleaseReceiveEventArgs(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs != null)
            {
                eventArgs.Completed -= OnReceiveCompleted;
                using (eventArgs.UserToken as ReceiveContext)
                    eventArgs.UserToken = null;

                SocketAsyncEventArgsCache.Default.Release(eventArgs);
            }
        }

        private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs.LastOperation != SocketAsyncOperation.Receive)
                throw new ArgumentException(Errors.ExpectingReceiveCompletedOperation);

            ((RpcServer)((ReceiveContext)eventArgs.UserToken).Server).ProcessReceived(eventArgs);
        }

        private void ProcessReceived(SocketAsyncEventArgs eventArgs)
        {
            var receiveContext = (ReceiveContext)eventArgs.UserToken;

            var continueReceiving = false;
            var clientConnection = receiveContext.Connection;

            if (eventArgs.BytesTransferred > 0 &&
                eventArgs.SocketError == SocketError.Success)
            {
                try
                {
                    receiveContext.ProcessReceived(eventArgs);
                    continueReceiving = true;
                }
                catch (Exception)
                { }
            }
            
            if (continueReceiving)
                StartReceiveAsync(clientConnection, eventArgs);
            else {
                CloseSocket(clientConnection);
                ReleaseReceiveEventArgs(eventArgs);
            }
        }

        private void CloseConnection(Socket clientConnection)
        {
            if (clientConnection != null)
            {
                UnregisterConnection(clientConnection);

                if ((clientConnection is NativeSocket nativeSocket) &&
                    nativeSocket.Disposed)
                    return;

                using (clientConnection)
                    clientConnection.Close();
            }
        }

        protected virtual bool RegisterConnection(Socket clientConnection)
        {
            if (clientConnection != null)
            {
                _receivingSockets[clientConnection] = null;
                return true;
            }
            return false;
        }

        protected virtual bool UnregisterConnection(Socket clientConnection)
        {
            if (clientConnection != null)
                return _receivingSockets.TryRemove(clientConnection, out object obj);
            return false;
        }

        protected virtual void ClearConnections()
        {
            _receivingSockets.Clear();
        }

        private byte[] ReadData(Socket clientConnection, int expected)
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
                    int bytesRead = clientConnection.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
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
