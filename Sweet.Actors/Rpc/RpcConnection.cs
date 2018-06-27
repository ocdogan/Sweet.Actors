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
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal class RpcConnection : Disposable, IRpcConnection
    {
        public event EventHandler OnDisconnect;

        private static readonly LingerOption NoLingerState = new LingerOption(true, 0);

        private long _receiving;

        private Stream _netStream;
        private Stream _outStream;

        private object _state;
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;
        private readonly RpcReceiveBuffer _buffer;

        private SocketAsyncEventArgs _receiveEventArgs;

        private Func<WireMessage, IRpcConnection, Task> _responseHandler;
        private Func<RemoteMessage, IRpcConnection, Task> _messageHandler;

        public RpcConnection(object state, Socket socket, 
            Func<RemoteMessage, IRpcConnection, Task> messageHandler,
            Func<WireMessage, IRpcConnection, Task> responseHandler)
        {
            _state = state;

            _socket = socket;
            _socket.LingerState = NoLingerState;

            _remoteEndPoint = (_socket?.RemoteEndPoint as IPEndPoint);
            _buffer = new RpcReceiveBuffer();

            _messageHandler = messageHandler;
            _responseHandler = responseHandler;

            _receiveEventArgs = SocketAsyncEventArgsCache.Default.Acquire();

            _receiveEventArgs.UserToken = this;
            _receiveEventArgs.Completed += OnReceiveCompleted;
        }

        public Socket Connection => _socket;

        public IPEndPoint RemoteEndPoint => _remoteEndPoint;

        public object State => _state;

        public bool Receiving => Interlocked.Read(ref _receiving) > 0L;

        public Stream Out => _outStream;

        protected override void OnDispose(bool disposing)
        {
            _state = null;
            if (disposing)
                Close();
        }

        private void Close()
        {
            try
            {
                var receiving = Interlocked.Exchange(ref _receiving, 0L) > 0L;

                var receiveEventArgs = Interlocked.Exchange(ref _receiveEventArgs, null);
                if (receiveEventArgs != null)
                    ReleaseReceiveEventArgs(receiveEventArgs, receiving);

                using (var stream = Interlocked.Exchange(ref _outStream, null))
                    stream?.Close();

                using (var stream = Interlocked.Exchange(ref _netStream, null))
                    stream?.Close();
            }
            finally
            {
                CloseSocket();
            }
        }

        public void OnConnect()
        {
            if (_socket != null && _socket.Blocking)
            {
                _netStream = new NetworkStream(_socket, false);
                _outStream = new BufferedStream(_netStream, RpcConstants.WriteBufferSize);
            }
        }

        private void CloseSocket()
        {
            var socket = Interlocked.Exchange(ref _socket, null);
            if (socket != null)
            {
                try
                {
                    if ((socket is NativeSocket nativeSocket) &&
                        nativeSocket.Disposed)
                        return;

                    using (socket)
                    {
                        if (socket.IsConnected())
                        {
                            socket.Shutdown(SocketShutdown.Both);
                            socket.Close();
                        }
                    }
                }
                finally
                {
                    Interlocked.Exchange(ref _receiving, 0L);
                    OnDisconnect?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        private static void ReleaseReceiveEventArgs(SocketAsyncEventArgs receiveEventArgs, bool inUse)
        {
            if (receiveEventArgs != null)
            {
                if (inUse)

                receiveEventArgs.Completed -= OnReceiveCompleted;
                receiveEventArgs.UserToken = null;

                SocketAsyncEventArgsCache.Default.Release(receiveEventArgs);
            }
        }

        private static void OnReceiveCompleted(object sender, SocketAsyncEventArgs receiveEventArgs)
        {
            if (receiveEventArgs.LastOperation != SocketAsyncOperation.Receive)
            {
                ReleaseReceiveEventArgs(receiveEventArgs, false);
                throw new ArgumentException(Errors.ExpectingReceiveCompletedOperation);
            }

            if (receiveEventArgs.UserToken is RpcConnection receiveCtx)
            {
                Interlocked.Exchange(ref receiveCtx._receiving, 0L);

                if (!receiveCtx.Disposed)
                    receiveCtx.DoReceived(receiveEventArgs);
                else
                    ReleaseReceiveEventArgs(receiveEventArgs, false);
            }
        }

        private void DoReceived(SocketAsyncEventArgs receiveEventArgs)
        {
            Interlocked.Exchange(ref _receiving, 0L);

            var receiveContext = (RpcConnection)receiveEventArgs.UserToken;

            var continueReceiving = false;
            var socket = receiveContext.Connection;

            if (receiveEventArgs.BytesTransferred > 0 &&
                receiveEventArgs.SocketError == SocketError.Success)
            {
                try
                {
                    receiveContext.ProcessReceived(receiveEventArgs);
                    continueReceiving = !Disposed;
                }
                catch (Exception)
                { }
            }

            if (continueReceiving)
                StartReceiveAsync(socket, receiveEventArgs);
            else
                Close();
        }

        public bool StartReceiveAsync()
        {
            return StartReceiveAsync(_socket, _receiveEventArgs);
        }

        private bool StartReceiveAsync(Socket socket, SocketAsyncEventArgs receiveEventArgs)
        {
            if (socket?.IsConnected() ?? false &&
                receiveEventArgs != null &&
                Interlocked.CompareExchange(ref _receiving, 1L, 0L) == 0L)
            {
                try
                {
                    if (!socket.ReceiveAsync(receiveEventArgs))
                        DoReceived(receiveEventArgs);
                    return true;
                }
                catch (Exception)
                {
                    Interlocked.Exchange(ref _receiving, 0L);
                    Close();
                }
            }
            return false;
        }

        private void ProcessReceived(SocketAsyncEventArgs receiveEventArgs)
        {
            try
            {
                if (_buffer.OnReceiveData(receiveEventArgs.Buffer, receiveEventArgs.Offset, receiveEventArgs.BytesTransferred))
                {
                    RemoteMessage remoteMsg = null;
                    try
                    {
                        while (_buffer.TryGetMessage(out remoteMsg))
                            _messageHandler?.Invoke(remoteMsg, this);
                    }
                    catch (Exception e)
                    {
                        if (_responseHandler != null)
                        {
                            var message = remoteMsg.Message;
                            if ((message != null) && (message.MessageType == MessageType.FutureMessage))
                            {
                                var response = message.ToWireMessage(remoteMsg.To, remoteMsg.MessageId, e);
                                _responseHandler.Invoke(response, this);
                                return;
                            }
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
}
