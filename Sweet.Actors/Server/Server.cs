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
    public class Server : Disposable
    {
        private class ReceiveContext
        {
            private Socket _connection;
            private readonly ReceiveBuffer _buffer;

            public ReceiveContext(Socket connection)
            {
                _connection = connection;
                _buffer = new ReceiveBuffer();
            }

            public Socket Connection
            {
                get
                {
                    return _connection;
                }
                internal set
                {
                    _connection = value;
                    RemoteEndPoint = _connection?.RemoteEndPoint;
                }
            }

            public EndPoint RemoteEndPoint { get; private set; }

            public Server Server { get; internal set; }

            public void ProcessReceived(SocketAsyncEventArgs eventArgs)
            {
                try
                {
                    _buffer.OnReceiveData(eventArgs.Buffer, eventArgs.BytesTransferred);

                    while (true)
                    {
                        Message msg = null;
                        try
                        {
                            if (!_buffer.TryDecodeMessage(out msg))
                                break;
                            Server.HandleMessage(msg, Connection);
                        }
                        catch (Exception e)
                        {
                            if ((msg == null) || msg.MessageType != MessageType.FutureMessage)
                                throw;

                            var response = CreateResponseError(msg, e);
                            Server.SendMessage(response);
                        }
                    }
                }
                catch (Exception)
                {
                    _buffer.Reset();
                    throw;
                }
            }

            public void Reset()
            {
                _buffer.Reset();
            }

            private Message CreateResponseError(Message msg, Exception exception)
            {
                return null;
            }
        }

        private const int MaxBufferSize = 4 * 1024;
        private readonly LingerOption _lingerState = new LingerOption(true, 0);

        private Socket _listener;
		private IPEndPoint _localEndPoint;
        private ServerSettings _serverSettings;

        private ConcurrentQueue<SocketAsyncEventArgs> _socketAsyncEventArgsPool =
            new ConcurrentQueue<SocketAsyncEventArgs>();

        private ConcurrentDictionary<Socket, object> _receivingSockets = new ConcurrentDictionary<Socket, object>();

        // States
        private int _stopping;
        private int _accepting;
        private long _status = ServerStatus.Stopped;

        public Server(ServerSettings serverSettings = null)
        {
            _serverSettings = serverSettings?.Clone() ?? new ServerSettings();
        }

        public IPEndPoint EndPoint => _localEndPoint ?? _serverSettings?.EndPoint;

        public long Status
        {
            get
            {
                Interlocked.MemoryBarrier();
                if (_stopping == Constants.True)
                    return ServerStatus.Stopping;
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
            if (Status != ServerStatus.Stopping)
            {
                Interlocked.Exchange(ref _status, ServerStatus.Starting);

                var serverEP = _serverSettings.EndPoint;

                Socket listener = null;
                try
                {
                    var family = serverEP.AddressFamily;
                    if (family == AddressFamily.Unknown || family == AddressFamily.Unspecified)
                        family = IPAddress.Any.AddressFamily;

                    listener = new NativeSocket(family, SocketType.Stream, ProtocolType.Tcp) { Blocking = false };
                    SetIOLoopbackFastPath(listener);
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _status, ServerStatus.Stopped);

                    listener.Dispose();
                    throw;
                }

                Interlocked.Exchange(ref _listener, listener);
                try
                {
                    listener.Bind(serverEP);
                    listener.Listen(_serverSettings.ConcurrentConnections);

                    var localEP = (listener.LocalEndPoint as IPEndPoint) ?? 
                        (IPEndPoint)listener.LocalEndPoint;

                    Interlocked.Exchange(ref _localEndPoint, localEP);
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _status, ServerStatus.Stopped);

                    TryToCloseSocket(listener);
                    throw;
                }

                Interlocked.Exchange(ref _status, ServerStatus.Started);

                StartAccepting(NewSocketAsyncEventArgs());
            }
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
                    Interlocked.Exchange(ref _status, ServerStatus.Stopped);
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

        private void SetIOLoopbackFastPath(Socket socket)
        {
            if (Common.IsWinPlatform)
            {
                try
                {
                    var ops = BitConverter.GetBytes(1);
                    socket.IOControl(Constants.SIO_LOOPBACK_FAST_PATH, ops, null);
                }
                catch (Exception)
                { }
            }
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

        private void OnAcceptCompleted(object sender, SocketAsyncEventArgs eventArgs)
        {
            ((Server)eventArgs.UserToken).HandleAccept(eventArgs, false);
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

                if (!(eventArgs.UserToken is Server server))
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
                            var readEventArgs = GetSocketReceiveAsyncEventArgs(connection);
                            StartReceiveAsync(connection, readEventArgs, server);
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

        private void StartReceiveAsync(Socket connection, SocketAsyncEventArgs eventArgs, Server server)
        { }

        private SocketAsyncEventArgs AcquireSocketAsyncEventArgs()
        {
            if (!_socketAsyncEventArgsPool.TryDequeue(out SocketAsyncEventArgs result))
                result = new SocketAsyncEventArgs();
            return result;
        }

        private void ReleaseSocketAsyncEventArgs(SocketAsyncEventArgs eventArgs)
        {
            if (eventArgs != null)
                _socketAsyncEventArgsPool.Enqueue(eventArgs);
        }

        private SocketAsyncEventArgs GetSocketReceiveAsyncEventArgs(Socket connection)
        {
            var result = AcquireSocketAsyncEventArgs();

            var token = ((ReceiveContext)result.UserToken);
            token.Server = this;
            token.Connection = connection;

            return result;
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
