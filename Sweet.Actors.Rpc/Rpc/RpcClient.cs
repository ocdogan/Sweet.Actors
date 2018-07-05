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

namespace Sweet.Actors.Rpc
{
    internal class RpcClient : Disposable, IRemoteClient
    {
        #region Constants

        private const object DefaultResponse = null;

        private const int BulkSendLength = 10;
        private const int SequentialSendLimit = 500;
        private const int ConnectionErrorTreshold = 5;
        private const int ConnectionRefuseTimeMSec = 10000;

        #endregion Constants

        private static int IdSeed;
        private static readonly Task Completed = Task.FromResult(0);

        private static readonly Exception ConnectionError = new Exception(RpcErrors.CannotConnectToRemoteEndPoint);
        private static readonly Task ConnectionErrorTask = Task.FromException(ConnectionError);

        private NativeSocket _socket;
        private object _socketLock = new object();

        private RpcClientOptions _options;
        private Func<RemoteMessage, Task> _onResponse;

        private int _id;
        private long _waitingToTransmit;

        // States
        private int _closing;
        private long _inProcess;
        private long _status = RpcClientStatus.Closed;

        private RpcMessageWriter _writer;
        private RpcConnection _connection;

        private CircuitBreaker _circuitForConnection;

        private ConcurrentQueue<RemoteRequest> _requestQueue = new ConcurrentQueue<RemoteRequest>();

        static RpcClient()
        {
            RpcSerializerRegistry.Register<DefaultRpcSerializer>(Constants.DefaultSerializerKey);
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("wire");
        }

        public RpcClient(Func<RemoteMessage, Task> onResponse, RpcClientOptions options)
        {
            _id = Interlocked.Increment(ref IdSeed);

            _circuitForConnection = new CircuitBreaker(
                policy: new CircuitPolicy(2, TimeSpan.FromSeconds(10000), TimeSpan.FromSeconds(10), 2, false),
                invoker: new ChainedInvoker(ConnectionValidator),
                onFailure: (circuitBreaker, exception) => {
                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);
                });

            _onResponse = onResponse;
            _options = options?.Clone() ?? RpcClientOptions.Default;
            _writer = new RpcMessageWriter(_options.Serializer);
        }

        public int Id => _id;

        public long Status
        {
            get
            {
                Interlocked.MemoryBarrier();
                if (_closing == Constants.True)
                    return RpcClientStatus.Closing;
                return Interlocked.Read(ref _status);
            }
        }

        public bool Connected
        {
            get
            {
                if (_status == RpcClientStatus.Connected)
                {
                    if (_socket?.IsConnected() ?? false)
                        return true;

                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);
                }
                return false;
            }
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _inProcess, Constants.False);
            if (disposing)
            {
                using (Interlocked.Exchange(ref _writer, null)) { }
                using (Interlocked.Exchange(ref _connection, null)) { }

                Close();
                return;
            }

            TryToClose();
        }

        private bool TryToClose()
        {
            try
            {
                Close();
                return true;
            }
            catch (Exception)
            { }
            return false;
        }

        public void Close()
        {
            if (Common.CompareAndSet(ref _closing, false, true))
            {
                try
                {
                    using (Interlocked.Exchange(ref _connection, null)) { }

                    Close(Interlocked.Exchange(ref _socket, null));

                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);
                }
                finally
                {
                    Interlocked.Exchange(ref _closing, Constants.False);
                }
            }
        }

        private static void Close(Socket socket)
        {
            if (socket != null)
                using (socket)
                {
                    if (socket.IsConnected())
                    {
                        socket.Shutdown(SocketShutdown.Both);
                        socket.Close();
                    }
                }
        }

        protected virtual Socket GetClientSocket()
        {
            var result = _socket;
            if (result == null || result.Disposed)
            {
                lock (_socketLock)
                {
                    result = _socket;
                    if (result?.Disposed ?? false)
                    {
                        Interlocked.Exchange(ref _socket, null);
                        result = null;
                    }

                    if (result == null)
                    {
                        var remoteEP = _options.EndPoint;
                        var addressFamily = remoteEP.AddressFamily;

                        if (addressFamily == AddressFamily.Unknown || 
                            addressFamily == AddressFamily.Unspecified)
                            addressFamily = IPAddress.Any.AddressFamily;

                        Configure(result = new NativeSocket(addressFamily, SocketType.Stream, ProtocolType.Tcp));

                        using (Interlocked.Exchange(ref _connection, new RpcConnection(this, result, HandleResponse, null)))
                        { }

                        Close(Interlocked.Exchange(ref _socket, result));
                    }
                }
            }
            return result;
        }

        public Task Connect()
        {
            if (_closing != Constants.True &&
                Interlocked.CompareExchange(ref _status, RpcClientStatus.Connecting, RpcClientStatus.Closed) == RpcClientStatus.Closed)
            {
                Socket socket = null;
                try
                {
                    socket = GetClientSocket();
                    if (!socket.IsConnected())
                    {
                        var remoteEP = _options.EndPoint;

                        var connectionTimeoutMSec = _options.ConnectionTimeoutMSec;
                        if (connectionTimeoutMSec < 0)
                            connectionTimeoutMSec = RpcConstants.MaxConnectionTimeout;

                        return socket.ConnectAsync(remoteEP, connectionTimeoutMSec)
                            .ContinueWith((previousTask) =>
                            {
                                if (previousTask.IsFaulted || previousTask.IsCanceled || !socket.IsConnected())
                                {
                                    Close(socket);
                                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);
                                    return;
                                }

                                Interlocked.Exchange(ref _status, RpcClientStatus.Connected);
                                _connection?.OnConnect();
                            });
                    }
                }
                catch (Exception e)
                {
                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);

                    Close(socket);
                    return Task.FromException(e);
                }
            }
            return Completed;
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

        public Task Send(RemoteRequest request)
        {
            ThrowIfDisposed();

            if (request == null)
                return Task.FromException(new ArgumentNullException(nameof(request)));

            _requestQueue.Enqueue(request);

            Schedule();
            return request.Completor.Task;
        }

        private void Schedule()
        {
            if (Interlocked.CompareExchange(ref _inProcess, Constants.True, Constants.False) == Constants.False)
            {
                Task.Factory.StartNew(ProcessRequestQueue);
            }
        }

        private Socket WaitToConnect()
        {
            var t = Connect();

            t.Wait();
            if (t.IsFaulted)
                return null;

            var socket = GetClientSocket();
            if (!socket.IsConnected())
                return null;

            return socket;
        }

        private static object ConnectionValidator((object @result, bool success) prev, out bool success)
        {
            if (!prev.success || !((Socket)prev.@result).IsConnected())
            {
                success = false;
                return null;
            }

            success = true;
            return prev.@result;
        }

        private Task ProcessRequestQueue()
        {
            try
            {
                if (Disposed)
                    return Completed;

                bool success;
                var socket = _circuitForConnection.Execute(WaitToConnect, out success);
                if (!success || !socket.IsConnected())
                    return Task.FromException(ConnectionError);

                var seqProcessCount = 0;
                for (var i = 0; i < SequentialSendLimit; i++)
                {
                    if ((Interlocked.Read(ref _inProcess) != Constants.True) ||
                        !_requestQueue.TryDequeue(out RemoteRequest request))
                        break;

                    seqProcessCount++;

                    var flush = (seqProcessCount == BulkSendLength) || (_requestQueue.Count == 0);
                    if (flush)
                        seqProcessCount = 0;

                    var task = ProcessRequest(request, flush);
                    if (task.IsFaulted || task.IsCanceled)
                        continue;

                    if (!task.IsCompleted)
                        task.ContinueWith((previousTask) =>
                        {
                            if (!Disposed)
                                Schedule();
                        });
                }
            }
            catch (Exception e)
            {
                HandleError(null, e);
                return Task.FromException(e);
            }
            finally
            {
                Interlocked.Exchange(ref _inProcess, Constants.False);
                if (!_requestQueue.IsEmpty)
                    Schedule();
            }
            return Completed;
        }

        private Task ProcessRequest(RemoteRequest request, bool flush = true)
        {
            var message = request.Message;
            var future = request.IsFuture ? (FutureMessage)message : null;
            try
            {
                if (future?.IsCanceled ?? false)
                {
                    request.Completor.TrySetCanceled();
                    future.Cancel();

                    return Completed;
                }

                if (message.Expired)
                {
                    var error = new Exception(Errors.MessageExpired);

                    request.Completor.TrySetException(error);
                    future?.RespondToWithError(error, future.From);

                    return Completed;
                }

                if (!Transmit(request, flush))
                {
                    request.Completor.TrySetCanceled();
                    future.Cancel();

                    TryToFlush();
                }

                return Completed;
            }
            catch (Exception e)
            {
                HandleError(request, e);

                try
                {
                    future?.RespondToWithError(e, future.From);
                }
                catch (Exception)
                { }

                TryToFlush();

                return Task.FromException(e);
            }
            finally
            {
                AsyncEventPool.Run(request.Completed);
            }
        }

        private void TryToFlush()
        {
            try
            {
                if (Interlocked.Exchange(ref _waitingToTransmit, 0L) > 0L && !Disposed)
                {
                    var outStream = _connection?.Out;
                    if (outStream != null && (outStream?.CanWrite ?? false))
                        outStream?.Flush();
                }
            }
            catch (Exception)
            { }
        }

        private void HandleError(RemoteRequest request, Exception e)
        {
            try
            {
                request?.Completor.TrySetException(e);
            }
            catch (Exception)
            { }
        }

        private bool Transmit(RemoteRequest request, bool flush)
        {
            if ((Interlocked.Read(ref _inProcess) == Constants.True) && !Disposed &&
                WriteMessage(request.Message.ToWireMessage(request.To, request.MessageId), flush))
            {
                BeginReceive();
                return true;
            }
            return false;
        }

        protected virtual bool WriteMessage(WireMessage message, bool flush = true)
        {
            var result = false;
            if (message != null)
            {
                var waitingCount = 0L;
                try
                {
                    waitingCount = Interlocked.Increment(ref _waitingToTransmit);
                    result = _writer.Write(_connection?.Out, message, flush);
                }
                finally
                {
                    if (result && flush)
                    {
                        var currentCount = Interlocked.Add(ref _waitingToTransmit, -waitingCount);
                        if (currentCount < 0)
                            Interlocked.Add(ref _waitingToTransmit, currentCount);
                    }
                }
            }
            return result;
        }

        private void BeginReceive()
        {
            if (!Disposed)
            {
                var connection = _connection;
                if (connection != null && !connection.Receiving)
                    ThreadPool.QueueUserWorkItem((asyncResult) => connection.Receive());
            }
        }

        protected virtual Task HandleResponse(RemoteMessage response, IRpcConnection rpcConnection)
        {
            ThrowIfDisposed();
            return _onResponse?.Invoke(response) ?? Completed;
        }
    }
}
