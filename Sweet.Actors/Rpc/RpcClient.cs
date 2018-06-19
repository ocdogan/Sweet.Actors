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
        private class Request
        {
            public Aid To;
            public WireMessageId Id;
            public IMessage Message;
            public TaskCompletionSource<object> TaskCompletionSource;
        }

        private const object DefaultResponse = null;
        private const int SequentialSendLimit = 100;

        private static readonly Task ProcessCompleted = Task.FromResult(0);

        private Socket _socket;
        private RpcClientOptions _options;

        private Stream _netStream;
        private Stream _outStream;

        // States
        private int _closing;
        private long _inProcess;
        private long _status = RpcClientStatus.Closed;

        private RpcMessageWriter _writer;

        private TaskCompletionSource<int> _connectionTcs;
        private ConcurrentQueue<Request> _requestQueue = new ConcurrentQueue<Request>();
        private ConcurrentDictionary<WireMessageId, Request> _responseList = new ConcurrentDictionary<WireMessageId, Request>();

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
                        .ContinueWith((t) =>
                        {
                            try
                            {
                                if (t.IsFaulted || t.IsCanceled || !socket.IsConnected())
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

            SetStream(ref _outStream, outStream);
            SetStream(ref _netStream, netStream);
        }

        private void ResetStreams()
        {
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

        public Task Send(IMessage message, Aid to)
        {
            ThrowIfDisposed();

            if (message == null)
                return Task.FromException(new ArgumentNullException(nameof(message)));

            var tcs = new TaskCompletionSource<object>();

            _requestQueue.Enqueue(new Request {
                Id = WireMessageId.Next(),
                To = to,
                Message = message,
                TaskCompletionSource = tcs
            });

            Schedule();
            return tcs.Task;
        }

        private void Schedule()
        {
            if (Common.CompareAndSet(ref _inProcess, false, true))
            {
                Task.Factory.StartNew(ProcessQueue);
            }
        }

        private Task ProcessQueue()
        {
            if (Disposed)
                return ProcessCompleted;

            try
            {
                Connect().Wait();

                for (var i = 0; i < SequentialSendLimit; i++)
                {
                    if ((Interlocked.Read(ref _inProcess) != Constants.True) ||
                        !_requestQueue.TryDequeue(out Request request))
                        break;

                    var tcs = request.TaskCompletionSource;

                    var isFutureCall = false;
                    FutureMessage future = null;
                    try
                    {
                        var message = request.Message;
                        isFutureCall = (message.MessageType == MessageType.FutureMessage);

                        if (isFutureCall)
                        {
                            future = (FutureMessage)message;
                            if (future.IsCanceled)
                            {
                                tcs.TrySetCanceled();
                                future.Cancel();

                                continue;
                            }
                        }

                        if (message.Expired)
                        {
                            var error = new Exception(Errors.MessageExpired);

                            tcs.TrySetException(error);
                            if (isFutureCall)
                                future.RespondToWithError(error, future.From);

                            continue;
                        }

                        Send(request);
                    }
                    catch (Exception e)
                    {
                        HandleError(request, e);

                        if (isFutureCall)
                            future.RespondToWithError(e, future.From);

                        return Task.FromException(e);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inProcess, Constants.False);
                if (_requestQueue.Count > 0)
                    Schedule();
            }
            return ProcessCompleted;
        }

        private void HandleError(Request request, Exception e)
        {
            try
            {
                request.TaskCompletionSource.TrySetException(e);
            }
            catch (Exception)
            { }
        }

        protected void CancelWaitingResponses()
        {
            var responses = Interlocked.Exchange(ref _responseList, new ConcurrentDictionary<WireMessageId, Request>());
            if (responses.Count > 0)
            {
                var ids = responses.Keys.ToList();
                foreach (var id in ids)
                {
                    if (_responseList.TryGetValue(id, out Request request))
                    {
                        try
                        {
                            request.TaskCompletionSource.TrySetCanceled();
                            if (request.Message is IFutureMessage future)
                                future.Cancel();
                        }
                        catch (Exception)
                        { }
                    }
                }
            }
        }

        protected virtual bool Write(WireMessage message, bool flush = true)
        {
            if ((Interlocked.Read(ref _inProcess) == Constants.True) && !Disposed)
                return _writer.Write(_outStream, message, flush);
            return false;
        }

        private void Send(Request request, bool flush = true)
        {
            _responseList[request.Id] = request;
            try
            {
                var wireMsg = request.Message.ToWireMessage(request.To, request.Id);
                if (Write(wireMsg, flush) && flush)
                    BeginReceive();
            }
            catch (Exception e)
            {
                try
                {
                    if (e is SocketException)
                    {
                        CancelWaitingResponses();
                        throw;
                    }
                }
                finally
                {
                    request.TaskCompletionSource.TrySetException(e);
                }
            }
        }

        private void BeginReceive()
        { }
    }
}
