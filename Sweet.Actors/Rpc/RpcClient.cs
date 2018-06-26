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
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    internal class RpcClient : Disposable, IRemoteClient
    {
        private const object DefaultResponse = null;

        private const int BulkSendLength = 10;
        private const int SequentialSendLimit = 100;

        private static readonly Task Completed = Task.FromResult(0);

        private Socket _socket;
        private RpcClientOptions _options;

        private Stream _netStream;
        private Stream _outStream;

        private long _waitingToTransmit;

        // States
        private int _closing;
        private long _inProcess;
        private long _status = RpcClientStatus.Closed;

        private RpcMessageWriter _writer;

        private TaskCompletionSource<int> _connectionTcs;
        private ConcurrentQueue<RemoteRequest> _requestQueue = new ConcurrentQueue<RemoteRequest>();

        static RpcClient()
        {
            RpcSerializerRegistry.Register<DefaultRpcSerializer>(Constants.DefaultSerializerKey);
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("wire");
        }

        public RpcClient(RpcClientOptions options)
        {
            _options = options?.Clone() ?? RpcClientOptions.Default;
            _writer = new RpcMessageWriter(_options.Serializer);
        }

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
                    if (_socket.IsConnected())
                        return true;

                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);
                }
                return false;
            }
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _inProcess, Constants.False);
            using (var writer = Interlocked.Exchange(ref _writer, null))
            { }

            ResetStreams();

            base.OnDispose(disposing);
            if (!disposing)
                TryToClose();
            else Close();
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
                    ResetStreams();

                    var socket = Interlocked.Exchange(ref _socket, null);
                    Close(socket);

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

        public Task Connect()
        {
            if ((_closing != Constants.True) &&
                Common.CompareAndSet(ref _status, RpcClientStatus.Closed, RpcClientStatus.Connecting))
            {
                var tcs = new TaskCompletionSource<int>();

                var oldTcs = Interlocked.Exchange(ref _connectionTcs, tcs);
                if (oldTcs != null)
                    oldTcs.TrySetCanceled();

                var remoteEP = _options.EndPoint;

                Socket socket = null;
                try
                {
                    var addressFamily = remoteEP.AddressFamily;
                    if (addressFamily == AddressFamily.Unknown || addressFamily == AddressFamily.Unspecified)
                        addressFamily = IPAddress.Any.AddressFamily;

                    socket = new NativeSocket(addressFamily, SocketType.Stream, ProtocolType.Tcp);
                    Configure(socket);

                    socket.ConnectAsync(remoteEP)
                        .ContinueWith((previousTask) =>
                        {
                            try
                            {
                                if (previousTask.IsFaulted || previousTask.IsCanceled || !socket.IsConnected())
                                {
                                    Close(socket);
                                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);

                                    return;
                                }

                                var old = Interlocked.Exchange(ref _socket, socket);
                                Close(old);

                                UpdateStreams(socket);
                                Interlocked.Exchange(ref _status, RpcClientStatus.Connected);

                                tcs.TrySetResult(0);
                            }
                            catch (Exception e)
                            {
                                tcs.TrySetException(e);
                            }
                        });
                }
                catch (Exception)
                {
                    ResetStreams();
                    Interlocked.Exchange(ref _status, RpcClientStatus.Closed);

                    Close(socket);
                    throw;
                }
            }
            return _connectionTcs?.Task ?? Task.FromResult(0);
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

        private void UpdateStreams(Socket socket)
        {
            var netStream = new NetworkStream(socket, false);
            var outStream = new BufferedStream(netStream, RpcConstants.WriteBufferSize);

            Interlocked.Exchange(ref _waitingToTransmit, 0L);

            SetStream(ref _outStream, outStream);
            SetStream(ref _netStream, netStream);
        }

        private void ResetStreams()
        {
            Interlocked.Exchange(ref _waitingToTransmit, 0L);

            SetStream(ref _outStream, null);
            SetStream(ref _netStream, null);
        }

        private void SetStream(ref Stream old, Stream @new)
        {
            try
            {
                if (@new != old)
                {
                    using (var prev = Interlocked.Exchange(ref old, @new))
                    {
                        if (prev != null)
                            prev.Close();
                    }
                }
            }
            catch (Exception)
            { }
        }

        public Task Send(RemoteRequest request)
        {
            ThrowIfDisposed();

            if (request == null)
                return Task.FromException(new ArgumentNullException(nameof(request)));

            _requestQueue.Enqueue(request);

            Schedule();
            return request.TaskCompletor.Task;
        }

        private void Schedule()
        {
            if (Common.CompareAndSet(ref _inProcess, false, true))
            {
                Task.Factory.StartNew(ProcessRequestQueue);
            }
        }

        private Task ProcessRequestQueue()
        {
            if (Disposed)
                return Completed;

            try
            {
                Connect().Wait();

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
                    if (task.IsFaulted)
                        continue;

                    if (!task.IsCompleted)
                        task.ContinueWith((previousTask) => {
                            if (!Disposed)
                                Schedule();
                        });
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inProcess, Constants.False);
                if (_requestQueue.Count > 0)
                    Schedule();
            }
            return Completed;
        }

        private Task ProcessRequest(RemoteRequest request, bool flush = true)
        {
            var message = request.Message;
            var future = (message as FutureMessage);
            try
            {
                if (future?.IsCanceled ?? false)
                {
                    request.TaskCompletor.TrySetCanceled();
                    future.Cancel();

                    return Completed;
                }

                if (message.Expired)
                {
                    var error = new Exception(Errors.MessageExpired);

                    request.TaskCompletor.TrySetException(error);
                    future?.RespondToWithError(error, future.From);

                    return Completed;
                }

                if (!Transmit(request, flush) && flush)
                {
                    request.TaskCompletor.TrySetCanceled();
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
        }

        private void TryToFlush()
        {
            try
            {
                if (Interlocked.Exchange(ref _waitingToTransmit, 0L) > 0L &&
                    !Disposed && (_outStream?.CanWrite ?? false))
                    _outStream?.Flush();
            }
            catch (Exception)
            { }
        }

        private void HandleError(RemoteRequest request, Exception e)
        {
            try
            {
                request.TaskCompletor.TrySetException(e);
            }
            catch (Exception)
            { }
        }

        private bool Transmit(RemoteRequest request, bool flush)
        {
            if ((Interlocked.Read(ref _inProcess) == Constants.True) && !Disposed)
            {
                var message = request.Message;
                if (WriteMessage(message.ToWireMessage(request.To, request.MessageId), flush))
                {
                    BeginReceive();
                    return true;
                }
            }
            return false;
        }

        protected virtual bool WriteMessage(WireMessage message, bool flush = true)
        {
            var result = false;
            var waitingCount = 0L;
            try
            {
                waitingCount = Interlocked.Increment(ref _waitingToTransmit);
                result = _writer.Write(_outStream, message, flush);
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
            return result;
        }

        private void BeginReceive()
        { }
    }
}
