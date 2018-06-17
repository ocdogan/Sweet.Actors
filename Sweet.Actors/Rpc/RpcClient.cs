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
    public class RpcClient : Disposable, IRemoteClient
    {
        private class Request
        {
            public Aid To;
            public RpcMessageId Id;
            public IMessage Message;
            public TaskCompletionSource<object> TaskCompletionSource;
        }

        private const object DefaultResponse = null;
        private const int SequentialSendLimit = 100;

        private static readonly byte[] ProcessIdBytes = Common.ProcessId.ToBytes();

        private static readonly Task ProcessCompleted = Task.FromResult(0);

        private Socket _socket;
        private RpcClientSettings _settings;

        private Stream _netStream;
        private Stream _outStream;

        // States
        private int _closing;
        private long _inProcess;
        private long _status = RpcClientStatus.Closed;

        private byte[] _serializerKey = new byte[RpcConstants.SerializerRegistryNameLength];

        private int _remoteMessageId;

        private IRpcSerializer _serializer;

        private TaskCompletionSource<int> _connectionTcs;
        private ConcurrentQueue<Request> _requestQueue = new ConcurrentQueue<Request>();
        private ConcurrentDictionary<RpcMessageId, Request> _responseList = new ConcurrentDictionary<RpcMessageId, Request>();

        static RpcClient()
        {
            RpcSerializerRegistry.Register<DefaultRpcSerializer>(Constants.DefaultSerializerKey);
            RpcSerializerRegistry.Register<DefaultRpcSerializer>("wire");
        }

        public RpcClient(RpcClientSettings settings)
        {
            _settings = ((RpcClientSettings)settings?.Clone()) ?? new RpcClientSettings();
            InitSerializer(_settings.Serializer);
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

            ResetStreams();

            base.OnDispose(disposing);
            if (!disposing)
                TryToClose();
            else Close();
        }

        private void InitSerializer(string serializerName)
        {
            serializerName = serializerName?.Trim();
            if (String.IsNullOrEmpty(serializerName))
                serializerName = Constants.DefaultSerializerKey;

            _serializer = RpcSerializerRegistry.Get(serializerName);
            if (_serializer == null && serializerName != Constants.DefaultSerializerKey)
            {
                serializerName = Constants.DefaultSerializerKey;
                _serializer = RpcSerializerRegistry.Get(serializerName);
            }

            if (_serializer != null)
            {
                var sn = Encoding.UTF8.GetBytes(serializerName);
                Buffer.BlockCopy(sn, 0, _serializerKey, 0, Math.Min(_serializerKey.Length, sn.Length));
            }
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

                var remoteEP = _settings.EndPoint;

                Socket socket = null;
                try
                {
                    var family = remoteEP.AddressFamily;
                    if (family == AddressFamily.Unknown || family == AddressFamily.Unspecified)
                        family = IPAddress.Any.AddressFamily;

                    socket = new NativeSocket(family, SocketType.Stream, ProtocolType.Tcp);
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

        public Task Send(IMessage msg, Aid to)
        {
            ThrowIfDisposed();
            if (msg == null)
                return Task.FromException(new ArgumentNullException(nameof(msg)));

            var tcs = new TaskCompletionSource<object>();

            _requestQueue.Enqueue(new Request {
                Id = RpcMessageId.Next(),
                To = to,
                Message = msg,
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
                        var msg = request.Message;
                        isFutureCall = (msg.MessageType == MessageType.FutureMessage);

                        if (isFutureCall)
                        {
                            future = (FutureMessage)msg;
                            if (future.IsCanceled)
                            {
                                tcs.TrySetCanceled();
                                future.Cancel();

                                continue;
                            }
                        }

                        if (msg.Expired)
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
            var responses = Interlocked.Exchange(ref _responseList, new ConcurrentDictionary<RpcMessageId, Request>());
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

        protected virtual bool Write(RpcMessage msg, bool flush = true)
        {
            if ((Interlocked.Read(ref _inProcess) == Constants.True) && !Disposed)
            {
                var stream = _outStream;
                if (stream != null)
                {
                    var data = _serializer.Serialize(msg);
                    if (data != null)
                    {
                        var dataLen = data.Length;
                        if (dataLen > RpcConstants.MaxDataSize)
                            throw new Exception(Errors.MaxAllowedDataSizeExceeded);

                        /* Header */
                        // Header sign
                        stream.WriteByte(RpcConstants.HeaderSign);

                        // Process Id
                        stream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);
                        
                        // Message Id
                        var msgIdBytes = Interlocked.Increment(ref _remoteMessageId).ToBytes();
                        stream.Write(msgIdBytes, 0, msgIdBytes.Length);

                        // Serialization type
                        stream.Write(_serializerKey, 0, _serializerKey.Length);

                        // Frame count
                        var frameCount = (ushort)(dataLen > 0 ? ((dataLen / RpcConstants.FrameDataSize) + 1) : 0);

                        var frameCntBytes = frameCount.ToBytes();
                        stream.Write(frameCntBytes, 0, frameCntBytes.Length);

                        var offset = 0;

                        /* Frames */
                        for (ushort frameIndex = 0; frameIndex < frameCount; frameIndex++)
                        {
                            // Frame sign
                            stream.WriteByte(RpcConstants.FrameSign);

                            // Process Id
                            stream.Write(ProcessIdBytes, 0, ProcessIdBytes.Length);

                            // Message Id
                            stream.Write(msgIdBytes, 0, msgIdBytes.Length);

                            // Frame Id
                            var frameIdBytes = frameIndex.ToBytes();
                            stream.Write(frameIdBytes, 0, frameIdBytes.Length);

                            // Frame length
                            var frameDataLen = (ushort)Math.Min(RpcConstants.FrameDataSize, dataLen - offset);

                            var frameDataLenBytes = frameDataLen.ToBytes();
                            stream.Write(frameDataLenBytes, 0, frameDataLenBytes.Length);

                            if (frameDataLen > 0)
                            {
                                stream.Write(data, offset, frameDataLen);
                                offset += frameDataLen;
                            }
                        }

                        if (flush)
                            stream.Flush();

                        return true;
                    }
                }
            }
            return false;
        }

        private void Send(Request request, bool flush = true)
        {
            _responseList[request.Id] = request;
            try
            {
                var rpcMsg = request.Message.ToRpcMessage(request.To, request.Id);
                if (Write(rpcMsg, flush) && flush)
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
