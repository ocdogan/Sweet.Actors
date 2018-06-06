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
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class RpcServer : Disposable
    {
        private class ReceiveContext : Disposable
        {
            private RpcServer _server;
            private Socket _connection;
            private IPEndPoint _remoteEndPoint;
            private readonly ReceiveBuffer _buffer;

            public ReceiveContext(RpcServer server, Socket connection)
            {
                _server = server;
                _connection = connection;
                _remoteEndPoint = (_connection?.RemoteEndPoint as IPEndPoint);
                _buffer = new ReceiveBuffer();
            }

            public Socket Connection => _connection;

            public IPEndPoint RemoteEndPoint => _remoteEndPoint;

            public RpcServer Server => _server;

            protected override void OnDispose(bool disposing)
            {
                _server = null;

                var connection = Interlocked.Exchange(ref _connection, null);
                if (connection != null)
                    using (connection)
                    {
                        connection.Shutdown(SocketShutdown.Both);
                        connection.Close();
                    }                
            }

            public void ProcessReceived(SocketAsyncEventArgs eventArgs)
            {
                try
                {
                    _buffer.OnReceiveData(eventArgs.Buffer, eventArgs.BytesTransferred);

                    Message msg = null;
                    try
                    {
                        if (_buffer.TryGetMessage(out ReceivedMessage receivedMsg))
                            Server.HandleMessage(msg, Connection);
                    }
                    catch (Exception e)
                    {
                        if (msg == null || msg.MessageType != MessageType.FutureMessage)
                            throw;

                        var response = CreateResponseError(msg, e);
                        Server.SendMessage(response);
                    }
                }
                catch (Exception)
                {
                    _buffer.Dispose();
                    throw;
                }
            }

            private Message CreateResponseError(Message msg, Exception exception)
            {
                return null;
            }
        }

        private const int MaxBufferSize = 4 * Constants.KB;
        private readonly LingerOption _lingerState = new LingerOption(true, 0);

        private Socket _listener;
		private IPEndPoint _localEndPoint;
        private RpcServerSettings _settings;

        private ConcurrentDictionary<Socket, object> _receivingSockets = new ConcurrentDictionary<Socket, object>();

        // States
        private int _stopping;
        private int _accepting;
        private long _status = RpcServerStatus.Stopped;

        static RpcServer()
        {
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("default");
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("wire");
        }

        public RpcServer(RpcServerSettings settings = null)
        {
            _settings = ((RpcServerSettings)settings?.Clone()) ?? new RpcServerSettings();
        }

        public IPEndPoint EndPoint => _localEndPoint ?? _settings?.EndPoint;

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

        protected override void OnDispose(bool disposing)
        {
            base.OnDispose(disposing);
            if (!disposing)
                TryToStop();
            else Stop();
        }

        public void Start()
        {
            Stop();
            StartListening();
        }

        private void StartListening()
        {
            if ((_stopping != Constants.True) &&
                Common.CompareAndSet(ref _status, RpcServerStatus.Stopped, RpcServerStatus.Starting))
            {
                var servingEP = _settings.EndPoint;

                Socket listener = null;
                try
                {
                    var family = servingEP.AddressFamily;
                    if (family == AddressFamily.Unknown || family == AddressFamily.Unspecified)
                        family = IPAddress.Any.AddressFamily;

                    listener = new NativeSocket(family, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
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
                    listener.Listen(_settings.ConcurrentConnections);

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

            if (_settings.SendTimeoutMSec > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.SendTimeout,
                                        _settings.SendTimeoutMSec == int.MaxValue ? Timeout.Infinite : _settings.SendTimeoutMSec);
            }

            if (_settings.ReceiveTimeoutMSec > 0)
            {
                socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.ReceiveTimeout,
                                        _settings.ReceiveTimeoutMSec == int.MaxValue ? Timeout.Infinite : _settings.ReceiveTimeoutMSec);
            }

            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.KeepAlive, true);

            socket.LingerState.Enabled = false;
            socket.SetSocketOption(SocketOptionLevel.Socket, SocketOptionName.DontLinger, true);

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

        private static void CloseSocket(Socket socket)
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
                    connection.LingerState = _lingerState;

                    Task.Factory.StartNew(() =>
                    {
                        if (!server.RegisterConnection(connection))
                            server.CloseConnection(connection);
                        else
                        {
                            var readEventArgs = AcquireSocketAsyncEventArgs(connection);
                            StartReceiveAsync(server, connection, readEventArgs);
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

        private void StartReceiveAsync(RpcServer server, Socket connection, SocketAsyncEventArgs eventArgs)
        {
            try
            {
                if (!connection.ReceiveAsync(eventArgs))
                    ProcessReceived(eventArgs);
            }
            catch (Exception)
            {
                CloseSocket(connection);
                ReleaseSocketAsyncEventArgs(eventArgs);
            }
        }

        private SocketAsyncEventArgs AcquireSocketAsyncEventArgs(Socket connection)
        {
            var result = SocketAsyncEventArgsCache.Default.Acquire();

            result.Completed += OnReceiveCompleted;
            result.UserToken = new ReceiveContext(this, connection);

            return result;
        }

        private void ReleaseSocketAsyncEventArgs(SocketAsyncEventArgs eventArgs)
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

            ((ReceiveContext)eventArgs.UserToken).Server.ProcessReceived(eventArgs);
        }

        private void ProcessReceived(SocketAsyncEventArgs eventArgs)
        {
            var receiveContext = (ReceiveContext)eventArgs.UserToken;

            var continueReceiving = false;
            var connection = receiveContext.Connection;

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
                StartReceiveAsync(receiveContext.Server, connection, eventArgs);
            else {
                CloseSocket(connection);
                ReleaseSocketAsyncEventArgs(eventArgs);
            }
        }

        private void CloseConnection(Socket connection)
        {
            if (connection != null)
            {
                UnregisterConnection(connection);

                if ((connection is NativeSocket nativeSocket) &&
                    nativeSocket.Disposed)
                    return;

                using (connection)
                    connection.Close();
            }
        }

        protected virtual bool RegisterConnection(Socket connection)
        {
            if (connection != null)
            {
                _receivingSockets[connection] = null;
                return true;
            }
            return false;
        }

        protected virtual bool UnregisterConnection(Socket connection)
        {
            if (connection != null)
                return _receivingSockets.TryRemove(connection, out object obj);
            return false;
        }

        protected virtual void ClearConnections()
        {
            _receivingSockets.Clear();
        }

        private byte[] ReadData(Socket connection, int expected)
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
                    int bytesRead = connection.Receive(buffer, offset, buffer.Length - offset, SocketFlags.None);
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

        private void HandleMessage(Message msg, Socket connection)
        { }

        private void SendMessage(Message msg)
        { }
    }
}
