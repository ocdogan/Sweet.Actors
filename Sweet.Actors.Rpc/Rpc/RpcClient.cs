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
using System.Collections;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors.Rpc
{
    internal class RpcClient : Processor<RemoteRequest>, IRemoteClient
    {
        #region Constants

        private const object DefaultResponse = null;

        private const int DefaultBulkSendLength = 100;
        private const int SequentialSendLimit = 500;
        private const int ConnectionErrorTreshold = 5;
        private const int ConnectionRefuseTimeMSec = 10000;

        #endregion Constants

        private static int IdSeed;

        private static readonly Exception ConnectionError = new Exception(RpcErrors.CannotConnectToRemoteEndPoint);
        private static readonly Task ConnectionErrorTask = Task.FromException(ConnectionError);

        private static readonly RemoteRequest[] EmptyRequests = new RemoteRequest[0];

        private NativeSocket _socket;
        private object _socketLock = new object();

        private RpcClientOptions _options;
        private Func<RemoteMessage, Task> _onResponse;

        private int _id;
        private long _waitingToTransmit;

        private long _sequentialProcessCount;
        private int _bulkSendLength = DefaultBulkSendLength;

        // States
        private int _closing;
        private long _status = RpcClientStatus.Closed;

        private RpcMessageWriter _writer;
        private RpcConnection _connection;

        private CircuitBreaker _circuitForConnection;

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

            _bulkSendLength = _options.BulkSendLength;
            if (_bulkSendLength < 1)
                _bulkSendLength = DefaultBulkSendLength;
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
            base.OnDispose(disposing);
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
            if (_closing == Constants.True)
                return Completed;

            var currentStatus = Interlocked.Read(ref _status);

            if (currentStatus != RpcClientStatus.Connecting &&
                Interlocked.CompareExchange(ref _status, RpcClientStatus.Connecting, currentStatus) == currentStatus)
            {
                Socket socket = null;
                try
                {
                    socket = GetClientSocket();
                    if (socket.IsConnected())
                    {
                        Interlocked.CompareExchange(ref _status, RpcClientStatus.Connected, RpcClientStatus.Connecting);
                        return Completed;
                    }

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

            Enqueue(request);

            return request.Completor.Task;
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

        protected override Task InitProcessCycle(out bool @continue)
        {
            @continue = true;

            bool success;
            var socket = _circuitForConnection.Execute(WaitToConnect, out success);
            if (!success || !socket.IsConnected())
            {
                @continue = false;
                Thread.Sleep(10);
                return Task.FromException(ConnectionError);
            }
            return Completed;
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

        protected override void ProcessItems()
        {
            for (var i = 0; i < SequentialInvokeLimit; i++)
            {
                if (!Processing() || IsEmpty())
                    break;

                var requests = PrepareMessagesToProcess();
                if ((requests?.Count ?? 0) == 0)
                    break;

                if (!Transmit(requests, true))
                {
                    TryToFlush();
                    CancelRequests(requests);
                }
            }
        }

        private IList PrepareMessagesToProcess()
        {
            IList result = EmptyRequests;

            var count = 0;
            for (var i = 0; i < _bulkSendLength; i++)
            {
                if (!TryDequeue(out RemoteRequest item))
                    break;

                var task = PrepareItem(item, out bool canSend);
                if (!canSend || task.IsFaulted || task.IsCanceled)
                    continue;

                switch (count++)
                {
                    case 0:
                        result = new RemoteRequest[] { item };
                        break;
                    case 1:
                        {
                            var firstItem = result[0];

                            result = new List<RemoteRequest>(Math.Min(Count(), _bulkSendLength));

                            result.Add(firstItem);
                            result.Add(item);
                            break;
                        }
                    default:
                        result.Add(item);
                        break;
                }
            }
            return result;
        }

        private void CancelRequests(IList requests)
        {
            foreach (var item in requests)
            {
                var request = (RemoteRequest)item;
                var future = request.IsFuture ? (FutureMessage)request.Message : null;
                try
                {
                    request.Completor.TrySetCanceled();
                    future?.Cancel();
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
                }
            }
        }

        protected virtual Task PrepareItem(RemoteRequest request, out bool canSend)
        {
            canSend = true;

            var message = request.Message;
            var future = request.IsFuture ? (FutureMessage)message : null;
            try
            {
                if (future?.IsCanceled ?? false)
                {
                    canSend = false;

                    request.Completor.TrySetCanceled();
                    future?.Cancel();

                    return Canceled;
                }

                if (message.Expired)
                {
                    canSend = false;

                    var error = new Exception(Errors.MessageExpired);

                    request.Completor.TrySetException(error);
                    future?.RespondToWithError(error, future.From);

                    return Canceled;
                }

                return Completed;
            }
            catch (Exception e)
            {
                canSend = false;
                HandleError(request, e);

                try
                {
                    future?.RespondToWithError(e, future.From);
                }
                catch (Exception)
                { }

                return Task.FromException(e);
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

        private bool Transmit(IList requests, bool flush)
        {
            if (Processing() && !Disposed)
            {
                var requestCnt = requests?.Count ?? 0;
                if (requestCnt > 0)
                {
                    var messages = new WireMessage[requestCnt];
                    for (var i = 0; i < requestCnt; i++)
                    {
                        var request = (RemoteRequest)requests[i];
                        messages[i] = request.Message.ToWireMessage(request.To, request.MessageId);
                    }

                    if (WriteMessage(messages, flush))
                    {
                        BeginReceive();
                        return true;
                    }
                }
            }
            return false;
        }

        protected virtual bool WriteMessage(WireMessage[] messages, bool flush = true)
        {
            var result = false;

            var messagesCount = messages?.Length ?? 0;
            if (messagesCount > 0)
            {
                var waitingCount = 0L;
                try
                {
                    waitingCount = Interlocked.Add(ref _waitingToTransmit, messagesCount);
                    result = _writer.Write(_connection?.Out, messages, flush);
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
