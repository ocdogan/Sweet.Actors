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
        ActorSystem ActorSystem { get; }
    }

    internal class Process : Disposable, IProcess
    {
        private const int WaitDuration = 5000;
        private const object DefaultResponse = null;

        private static readonly Task Completed = Task.FromResult(0);

        private static ConcurrentDictionary<Pid, Process> _processRegistery = new ConcurrentDictionary<Pid, Process>();

        private Pid _pid;
        private string _name;
        private Context _ctx;

        private long _inProcess;
        private int _requestTimeoutMSec = -1;
        private int _sequentialInvokeLimit;

        private IActor _actor;
        private ActorSystem _actorSystem;
        private IErrorHandler _errorHandler;

        private readonly ManualResetEventSlim _resetEvent = new ManualResetEventSlim(false);

        private ConcurrentQueue<IMessage> _mailbox = new ConcurrentQueue<IMessage>();

        public Process(string name, ActorSystem actorSystem, IActor actor, ActorOptions options)
        {
            _name = name;

            _actor = actor;
            _actorSystem = actorSystem;

            _pid = new Pid(this);
            _ctx = new Context(this);

            _errorHandler = (options.ErrorHandler ?? actorSystem.Options.ErrorHandler) ?? DefaultErrorHandler.Instance;
            
            _requestTimeoutMSec = Math.Min(Math.Max(-1, GetRequestTimeoutMSec(actorSystem, options)), Constants.MaxRequestTimeoutMSec); 

            var sequentialInvokeLimit = Common.CheckSequentialInvokeLimit(GetSequentialInvokeLimit(actorSystem, options));
            _sequentialInvokeLimit = (sequentialInvokeLimit < 1) ?
                    Constants.DefaultSequentialInvokeLimit : sequentialInvokeLimit;

            if (options.InitialContextData != null)
                foreach (var kv in options.InitialContextData)
                    _ctx.SetData(kv.Key, kv.Value);

            _processRegistery.TryAdd(_pid, this);
        }

        public ActorSystem ActorSystem => _actorSystem;

        public IActor Actor { get => _actor; protected set => _actor = value; }

        public IContext Context { get { return _ctx; } }

        public Pid Pid => _pid;

        public string Name => _name;

        public int RequestTimeoutMSec => _requestTimeoutMSec;

        private int GetRequestTimeoutMSec(ActorSystem actorSystem, ActorOptions actorOptions)
        {
            var result = actorOptions.RequestTimeoutMSec;
            if (result == -1)
                result = actorSystem.Options.RequestTimeoutMSec;

            return result;
        }

        private int GetSequentialInvokeLimit(ActorSystem actorSystem, ActorOptions actorOptions)
        {
            var result = actorOptions.SequentialInvokeLimit;
            if (result < 1)
                result = actorSystem.Options.SequentialInvokeLimit;

            return result;
        }

        protected override void OnDispose(bool disposing)
        {
            Interlocked.Exchange(ref _inProcess, 0L);
            if (disposing)
            {
                _resetEvent.Reset();
                _processRegistery.TryRemove(_pid, out Process process);
            }
        }

        public Task Send(IMessage message)
        {
            ThrowIfDisposed();

            if (message == null)
                throw new ArgumentNullException(nameof(message));

            return SendInternal(message);
        }

        private Task SendInternal(IMessage message)
        {
            try
            {
                _mailbox.Enqueue(message);
                Schedule();

                return Completed;
            }
            catch (Exception e)
            {
                return Task.FromException(e);
            }
        }

        public Task Send(object message, IDictionary<string, string> header = null)
        {
            ThrowIfDisposed();
            return SendInternal(new Message(message, _ctx.Pid, header));
        }

        public Task<IFutureResponse> Request(object message, IDictionary<string, string> header = null)
        {
            ThrowIfDisposed();
            return RequestInternal<object>(message, header);
        }

        public Task<IFutureResponse> Request<T>(object message, IDictionary<string, string> header = null)
        {
            ThrowIfDisposed();
            return RequestInternal<T>(message, header);
        }

        private Task<IFutureResponse> RequestInternal<T>(object message, IDictionary<string, string> header)
        {
            try
            {
                var taskCompletor = new TaskCompletor<IFutureResponse>(_requestTimeoutMSec);

                _mailbox.Enqueue(new FutureMessage(message, taskCompletor, _ctx.Pid, header));
                Schedule();

                return taskCompletor.Task;
            }
            catch (Exception e)
            {
                return Task.FromResult<IFutureResponse>(new FutureError(e, _ctx.Pid));
            }
        }

        private void Schedule()
        {
            if (!Disposed)
            {
                if (Interlocked.CompareExchange(ref _inProcess, 1, 0) == 0)
                    Task.Factory.StartNew(ProcessMailbox);
                else if (!_resetEvent.IsSet)
                    _resetEvent.Set();
            }
        }

        private Task ProcessMailbox()
        {
            try
            {
                if (!Disposed)
                {
                    _resetEvent.Reset();

                    do
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
                                task.ContinueWith((previousTask) =>
                                {
                                    if (!Disposed)
                                        Schedule();
                                });
                        }
                    } while ((Interlocked.Read(ref _inProcess) != 1L) && _resetEvent.Wait(WaitDuration));
                }
            }
            finally
            {
                if (!Disposed)
                {
                    Interlocked.Exchange(ref _inProcess, 0L);
                    _resetEvent.Reset();

                    if (!_mailbox.IsEmpty)
                        Schedule();
                }
            }
            return Completed;
        }

        protected virtual Task ProcessMessage(IMessage message)
        {
            var future = (message as FutureMessage);
            try
            {
                if (future?.IsCanceled ?? false)
                {
                    future.Cancel();
                    return Completed;
                }

                if (message.Expired)
                {
                    future?.Cancel();
                    return Completed;
                }

                var t = SendToActor(_ctx, message);

                if (t.IsFaulted)
                    HandleProcessError(message, t.Exception);

                return t;
            }
            catch (Exception e)
            {
                HandleProcessError(message, e);

                try
                {
                    future?.RespondToWithError(e, _ctx.Pid);
                }
                catch (Exception)
                { }

                return Task.FromException(e);
            }
        }

        protected virtual Task SendToActor(IContext ctx, IMessage message)
        {
            return Actor.OnReceive(ctx, message);
        }

        protected virtual void HandleError(Exception e)
        {
            try
            {
                _errorHandler?.HandleError(_actorSystem, e);
            }
            catch (Exception)
            { }
        }

        protected virtual void HandleProcessError(IMessage message, Exception e)
        {
            try
            {
                _errorHandler?.HandleProcessError(_actorSystem, _pid, message, e);
            }
            catch (Exception)
            { }
        }
    }

    internal class FunctionCallProcess : Process, IActor
    {
        private Func<IContext, IMessage, Task> _receiveFunc;

        public FunctionCallProcess(string name, ActorSystem actorSystem,
                       Func<IContext, IMessage, Task> function, ActorOptions options)
                       : base(name, actorSystem, null, options)
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
                       RemoteAddress remoteAddress, ActorOptions options)
                       : base(name, actorSystem, null, options)
        { 
            _remoteAddress = remoteAddress;
            Actor = this;
        }

        public Task OnReceive(IContext ctx, IMessage message)
        {
            return ActorSystem?.RemoteManager?.Send(message, _remoteAddress, RequestTimeoutMSec) ??
                Task.FromException(new Exception(Errors.SystemIsNotConfiguredForToCallRemoteActors));
        }
    }
}
