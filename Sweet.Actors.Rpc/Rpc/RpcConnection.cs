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

namespace Sweet.Actors.Rpc
{
    internal class RpcConnection : Disposable, IRpcConnection
    {
        private static readonly LingerOption NoLingerState = new LingerOption(true, 0);

        public event EventHandler OnDisconnect;

        private class AsyncReceiveBuffer : Disposable
        {
            private int _length;
            private byte[] _buffer;
            private int _synchronousCompletionCount;

            public AsyncReceiveBuffer()
            {
                _buffer = ByteArrayCache.Default.Acquire();
                _length = _buffer?.Length ?? 0;
            }

            public byte[] Buffer => _buffer;

            public int Count { get; set; }

            public int Length => _length;

            public int SynchronousCompletionCount => _synchronousCompletionCount;

            public int SynchronousCompletion()
            {
                return Interlocked.Increment(ref _synchronousCompletionCount);
            }

            public void ResetSynchronousCompletion()
            {
                Interlocked.Exchange(ref _synchronousCompletionCount, 0);
            }

            protected override void OnDispose(bool disposing)
            {
                if (disposing)
                    ByteArrayCache.Default.Release(_buffer);
            }
        }

        private class AsyncReceiveState : IDisposable
        {
            private long _completed;
            private Socket _socket;
            private RpcConnection _connection;
            private AsyncReceiveBuffer _buffer;

            public AsyncReceiveState(RpcConnection connection, 
                AsyncReceiveBuffer buffer, Socket socket)
            {
                _buffer = buffer;
                _socket = socket;
                _connection = connection;
            }

            public AsyncReceiveBuffer Buffer => _buffer;

            public Socket Socket => _socket;

            public RpcConnection Connection => _connection;

            public bool IsCompleted => (Interlocked.Read(ref _completed) != 0L);

            public bool TryToComplete(out AsyncReceiveBuffer buffer)
            {
                buffer = null;
                if (Interlocked.CompareExchange(ref _completed, 1L, 0L) == 0L)
                {
                    buffer = _buffer;
                    return true;
                }
                return false;
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _completed, 1L);

                Interlocked.Exchange(ref _buffer, null);
                Interlocked.Exchange(ref _socket, null);
                Interlocked.Exchange(ref _connection, null);
            }
        }

        private static int IdSeed;
        private const int SynchronousCompletionTreshold = 10;

        private int _id;
        private long _receiving;
        private long _inReceiveCycle;

        private Stream _netStream;
        private Stream _outStream;

        private object _state;
        private Socket _socket;
        private IPEndPoint _remoteEndPoint;

        private AsyncReceiveBuffer _asyncReceiveBuffer;
        private readonly RpcReceiveBuffer _rpcReceiveBuffer;

        private Func<WireMessage, IRpcConnection, Task> _responseHandler;
        private Func<RemoteMessage, IRpcConnection, Task> _messageHandler;

        public RpcConnection(object state, Socket socket, 
            Func<RemoteMessage, IRpcConnection, Task> messageHandler,
            Func<WireMessage, IRpcConnection, Task> responseHandler)
        {
            _id = Interlocked.Increment(ref IdSeed);
            _state = state;

            _socket = socket;
            _socket.LingerState = NoLingerState;

            _remoteEndPoint = (_socket?.RemoteEndPoint as IPEndPoint);
            _rpcReceiveBuffer = new RpcReceiveBuffer();

            _messageHandler = messageHandler;
            _responseHandler = responseHandler;
        }

        public int Id => _id;

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
                using (Interlocked.Exchange(ref _asyncReceiveBuffer, null)) { }

                using (var stream = Interlocked.Exchange(ref _outStream, null))
                    stream?.Close();

                using (var stream = Interlocked.Exchange(ref _netStream, null))
                    stream?.Close();
            }
            catch (Exception)
            { }
            finally
            {
                Interlocked.Exchange(ref _inReceiveCycle, 0L);
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
                catch (Exception)
                { }
                finally
                {
                    Interlocked.Exchange(ref _inReceiveCycle, 0L);
                    Interlocked.Exchange(ref _receiving, 0L);

                    OnDisconnect?.Invoke(this, EventArgs.Empty);
                }
            }
        }

        public bool Receive()
        {
            if (Interlocked.CompareExchange(ref _receiving, 1L, 0L) == 0L)
            {
                if (BeginReceive(_asyncReceiveBuffer))
                    return true;

                Interlocked.Exchange(ref _receiving, 0L);
                return false;
            }
            return true;
        }

        private bool BeginReceive(AsyncReceiveBuffer asyncReceiveBuffer)
        {
            if (Interlocked.CompareExchange(ref _inReceiveCycle, 1L, 0L) != 0L)
                return false;

            try
            {
                if (asyncReceiveBuffer is null)
                    asyncReceiveBuffer = (_asyncReceiveBuffer = new AsyncReceiveBuffer());

                var socket = _socket;
                if (!socket.IsConnected())
                {
                    Interlocked.Exchange(ref _inReceiveCycle, 0L);
                    return false;
                }

                var asyncResult = socket.BeginReceive(asyncReceiveBuffer.Buffer, 0, asyncReceiveBuffer.Length, SocketFlags.None,
                    out SocketError errorCode, OnReceiveCompleted, 
                    new AsyncReceiveState(this, asyncReceiveBuffer, socket));

                if (errorCode.IsSocketError())
                    throw new SocketException((int)errorCode);

                if (asyncResult?.CompletedSynchronously ?? false)
                    DoReceiveCompleted(asyncResult, true);

                return true;
            }
            catch (Exception)
            {
                Interlocked.Exchange(ref _inReceiveCycle, 0L);
                Interlocked.Exchange(ref _receiving, 0L);

                Close();
            }
            return false;
         }

        private static void OnReceiveCompleted(IAsyncResult asyncResult)
        {
            if (asyncResult.CompletedSynchronously)
                return;

            DoReceiveCompleted(asyncResult, false);
        }

        private static void DoReceiveCompleted(IAsyncResult asyncResult, bool calledSynchronously)
        {
            if (asyncResult.AsyncState is AsyncReceiveState state)
            {
                using (state)
                {
                    if (state.TryToComplete(out AsyncReceiveBuffer asyncReceiveBuffer) &&
                        (asyncReceiveBuffer != null))
                    {
                        var connection = state.Connection;
                        try
                        {
                            asyncReceiveBuffer.Count = state.Socket.EndReceive(asyncResult, out SocketError errorCode);
                            if (errorCode.IsSocketError())
                                throw new SocketException((int)errorCode);

                            DoReceived(connection, asyncReceiveBuffer, calledSynchronously);
                        }
                        catch (Exception)
                        {
                            if (!calledSynchronously && !(connection?.Disposed ?? true))
                            {
                                Interlocked.Exchange(ref connection._inReceiveCycle, 0L);
                                Interlocked.Exchange(ref connection._receiving, 0L);

                                connection.Close();
                            }

                            if (calledSynchronously)
                                throw;
                        }
                    }
                }
            }
        }

        private static void DoReceived(RpcConnection connection, AsyncReceiveBuffer asyncReceiveBuffer, bool calledSynchronously = false)
        {
            if (!(connection?.Disposed ?? true))
            {
                var bytesReceived = asyncReceiveBuffer.Count;
                if (bytesReceived > 0)
                {
                    connection.ProcessReceived(asyncReceiveBuffer.Buffer, bytesReceived);
                    if (connection.Disposed)
                        return;

                    if (calledSynchronously &&
                        asyncReceiveBuffer.SynchronousCompletion() > SynchronousCompletionTreshold)
                    {
                        asyncReceiveBuffer.ResetSynchronousCompletion();

                        ThreadPool.QueueUserWorkItem((waitCallback) =>
                        {
                            Interlocked.Exchange(ref connection._inReceiveCycle, 0L);
                            connection.BeginReceive(asyncReceiveBuffer);
                        });
                        return;
                    }

                    if (!calledSynchronously)
                        asyncReceiveBuffer.ResetSynchronousCompletion();

                    Interlocked.Exchange(ref connection._inReceiveCycle, 0L);
                    connection.BeginReceive(asyncReceiveBuffer);

                    return;
                }

                connection.Close();
            }
        }

        private void ProcessReceived(byte[] buffer, int bytesReceived)
        {
            try
            {
                if (_rpcReceiveBuffer.OnReceiveData(buffer, 0, bytesReceived))
                {
                    while (_rpcReceiveBuffer.TryGetMessage(out RemoteMessage message))
                    {
                        try
                        {
                            _messageHandler?.Invoke(message, this);
                        }
                        catch (Exception e)
                        {
                            RespondWithError(message, e);
                        }
                    }
                }
            }
            catch (Exception)
            {
                _rpcReceiveBuffer.Dispose();
                throw;
            }
        }

        private bool RespondWithError(RemoteMessage message, Exception e)
        {
            var handler = _responseHandler;
            if (handler != null)
            {
                var realMessage = message.Message;
                if ((realMessage != null) && (realMessage.MessageType == MessageType.FutureMessage))
                {
                    var response = realMessage.ToWireMessage(message.To, message.MessageId, e);
                    handler.Invoke(response, this);

                    return true;
                }
            }
            return false;
        }
    }
}
