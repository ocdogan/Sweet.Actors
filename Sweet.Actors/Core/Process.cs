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
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public interface IProcess
    {
        IActor Actor { get; }
        IContext Context { get; }
        Pid Pid { get; }
        ActorSystem System { get; }
    }

    internal class Process : Disposable, IProcess
    {
        private const object DefaultResponse = null;

        private static readonly Task Completed = Task.FromResult(0);

        private static ConcurrentDictionary<Pid, Process> _processRegistery = new ConcurrentDictionary<Pid, Process>();

        private Pid _pid;
        private string _name;
        private Context _ctx;
        private long _inProcess;
        private int _sequentialInvokeLimit;
        private IErrorHandler _errorHandler;

        private ConcurrentQueue<IMessage> _mailbox = new ConcurrentQueue<IMessage>();

        public Process(string name, ActorSystem actorSystem, 
                       IActor actor, IErrorHandler errorHandler = null,
                       int sequentialInvokeLimit = Constants.DefaultSequentialInvokeLimit,
                       IDictionary<string, object> initialContextData = null)
        {
            _name = name;

            Actor = actor;
            System = actorSystem;

            _pid = new Pid(this);
            _ctx = new Context(this);

            _errorHandler = errorHandler ?? DefaultErrorHandler.Instance;

            _sequentialInvokeLimit = Common.ValidateSequentialInvokeLimit(sequentialInvokeLimit);
            _sequentialInvokeLimit = (_sequentialInvokeLimit < 1) ?
                    Constants.DefaultSequentialInvokeLimit : _sequentialInvokeLimit;

            if (initialContextData != null)
                foreach (var kv in initialContextData)
                    _ctx.SetData(kv.Key, kv.Value);

            _processRegistery.TryAdd(_pid, this);
        }

        public ActorSystem System { get; }

        public IActor Actor { get; protected set; }

        public IContext Context { get { return _ctx; } }

        public Pid Pid => _pid;

        public string Name => _name;

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _inProcess, 0L);
            if (disposing)
                _processRegistery.TryRemove(_pid, out Process process);

            base.OnDispose(disposing);
        }

        public Task Send(IMessage message, int timeoutMSec = -1)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return SendInternal(message, timeoutMSec);
        }

        private Task SendInternal(IMessage message, int timeoutMSec)
        {
            try
            {
                _mailbox.Enqueue(message);
                StartProcessTask();

                return Completed;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        public Task Send(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
        {
            ThrowIfDisposed();
            return SendInternal(new Message(message, _ctx.Pid, header), timeoutMSec);
        }

        public Task<IFutureResponse> Request(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
        {
            ThrowIfDisposed();
            return RequestInternal<object>(message, header, timeoutMSec);
        }

        public Task<IFutureResponse> Request<T>(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
        {
            ThrowIfDisposed();
            return RequestInternal<T>(message, header, timeoutMSec);
        }

        private Task<IFutureResponse> RequestInternal<T>(object message, IDictionary<string, string> header, int timeoutMSec)
        {
            try
            {
                CancellationTokenSource cts = null;
                TaskCompletionSource<IFutureResponse> tcs;

                if (timeoutMSec < 1)
                    tcs = new TaskCompletionSource<IFutureResponse>();
                else
                {
                    cts = new CancellationTokenSource();
                    tcs = new TaskCompletionSource<IFutureResponse>(cts.Token);

                    TimeoutHandler.TryRegister(cts, timeoutMSec);
                }

                _mailbox.Enqueue(new FutureMessage<T>(message, cts, tcs, _ctx.Pid, header, timeoutMSec));
                StartProcessTask();

                return tcs.Task;
            }
            catch (Exception e)
            {
                return Task.FromResult<IFutureResponse>(new FutureError<T>(e, _ctx.Pid));
            }
        }

        private void StartProcessTask()
        {
            if (Interlocked.CompareExchange(ref _inProcess, 1L, 0L) == 0L && !Disposed)
            {
                Task.Factory.StartNew(ProcessMailbox);
            }
        }

        private Task ProcessMailbox()
        {
            try
            {
                for (var i = 0; i < _sequentialInvokeLimit; i++)
                {
                    if ((Interlocked.Read(ref _inProcess) != 1L) ||
                        !_mailbox.TryDequeue(out IMessage message))
                        break;

                    var task = ProcessMessage(message);
                    if (task.IsFaulted)
                        continue;

                    if (!task.IsCompleted)
                        task.ContinueWith((previousTask) => {
                            if (!Disposed)
                                StartProcessTask();
                        });
                }
            }
            finally
            {
				Interlocked.Exchange(ref _inProcess, 0L);
                if (!Disposed && _mailbox.Count > 0)
                    StartProcessTask();
            }
            return Completed;
        }

        protected virtual Task ProcessMessage(IMessage message)
        {
            var isFutureCall = false;
            FutureMessage future = null;
            try
            {
                isFutureCall = (message.MessageType == MessageType.FutureMessage);

                if (isFutureCall)
                {
                    future = (FutureMessage)message;
                    if (future.IsCanceled || message.Expired)
                    {
                        future.Cancel();
                        return Completed;
                    }
                }
                else if (message.Expired)
                    return Completed;

                var t = SendToActor(_ctx, message);

                if (t.IsFaulted)
                    HandleError(message, t.Exception);

                return t;
            }
            catch (Exception e)
            {
                HandleError(message, e);

                if (isFutureCall && (future != null))
                {
                    try
                    {
                        future.RespondToWithError(e, _ctx.Pid);
                    }
                    catch (Exception)
                    { }
                }

                return Task.FromException(e);
            }
        }

        protected virtual Task SendToActor(IContext ctx, IMessage message)
        {
            return Actor.OnReceive(ctx, message);
        }

        protected virtual void HandleError(IMessage message, Exception e)
        {
            try
            {
                _errorHandler?.HandleError(this, message, e);
            }
            catch (Exception)
            { }
        }
    }

    internal class FunctionCallProcess : Process, IActor
    {
        private Func<IContext, IMessage, Task> _receiveFunc;

        public FunctionCallProcess(string name, ActorSystem actorSystem,
                       Func<IContext, IMessage, Task> function, 
                       IErrorHandler errorHandler = null,
                       int sequentialInvokeLimit = Constants.DefaultSequentialInvokeLimit,
                       IDictionary<string, object> initialContextData = null)
                       : base(name, actorSystem, null, 
                            errorHandler, sequentialInvokeLimit, initialContextData)
        { 
            _receiveFunc = function;
            Actor = this;
        }

        public Func<IContext, IMessage, Task> ReceiveFunc => _receiveFunc;
        
        public Task OnReceive(IContext ctx, IMessage message)
        {
            return _receiveFunc(ctx, message);
        }
    }

    internal class RemoteProcess : Process, IActor
    {
        private RemoteAddress _remoteAddress;

        public RemoteProcess(string name, ActorSystem actorSystem,
                       RemoteAddress remoteAddress, IErrorHandler errorHandler = null,
                       int sequentialInvokeLimit = Constants.DefaultSequentialInvokeLimit,
                       IDictionary<string, object> initialContextData = null)
                       : base(name, actorSystem, null, 
                            errorHandler, sequentialInvokeLimit, initialContextData)
        { 
            _remoteAddress = remoteAddress;
            Actor = this;
        }

        public Task OnReceive(IContext ctx, IMessage message)
        {
            var remoteMngr = System?.RemoteManager;
            if (remoteMngr == null)
                return Task.FromException(new Exception(Errors.SystemIsNotConfiguredForToCallRemoteActors));

            return remoteMngr.Send(message, _remoteAddress);
        }
    }
}
