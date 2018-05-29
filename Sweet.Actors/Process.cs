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
    internal class Process
    {
        private const object DefaultResponse = null;

        private static readonly Task Sended = Task.FromResult(0);
        private static readonly Task ProcessCompleted = Task.FromResult(0);

        private Pid _pid;
        private Context _ctx;
        private long _inProcess;
        private int _sequentialInvokeLimit;
        private ConcurrentQueue<Message> _mailbox = new ConcurrentQueue<Message>();

        public Process(ActorSystem system, IActor actor, Address address,
                       int sequentialInvokeLimit = Constants.DefaultSequentialInvokeLimit,
                       IDictionary<string, object> initialContextData = null)
        {
            Actor = actor;
			System = system;

            _pid = new Pid(this);
            _ctx = new Context(this, address);

            _sequentialInvokeLimit = Common.ValidateSequentialInvokeLimit(sequentialInvokeLimit);
            _sequentialInvokeLimit = (_sequentialInvokeLimit < 1) ?
                    Constants.DefaultSequentialInvokeLimit : _sequentialInvokeLimit;

            if (initialContextData != null)
				foreach (var kv in initialContextData)
					_ctx.SetData(kv.Key, kv.Value);
        }

		public ActorSystem System { get; } 

        public IActor Actor { get; }

        public IContext Context { get { return _ctx; } }

        public Pid Pid => _pid;

		public Task Send(object message, IDictionary<string, string> header = null)
        {
			if (message != null)
			{
				try
				{
					_mailbox.Enqueue(new Message(message, _ctx.Address, header));
					StartNewProcess();
				}
				catch (Exception e)
				{
					return Task.FromException(e);
				}
			}
            return Sended;
        }

		public Task<IFutureResponse<T>> Request<T>(object message, IDictionary<string, string> header = null, int timeoutMSec = -1)
		{
			if (message != null)
            {
				try
				{                    
					var cts = timeoutMSec > 0 ? new CancellationTokenSource(timeoutMSec) : null;
					var tcs = cts != null ? new TaskCompletionSource<IFutureResponse<T>>(cts.Token) :
						new TaskCompletionSource<IFutureResponse<T>>();

					_mailbox.Enqueue(new FutureMessage<T>(message, cts, tcs, _ctx.Address, header));
                    StartNewProcess();
                    
    				return tcs.Task;
				}
                catch (Exception e)
                {
					return Task.FromResult<IFutureResponse<T>>(new FutureError<T>(e, _ctx.Address));
                }
            }
			return Task.FromResult<IFutureResponse<T>>(new FutureResponse<T>(default(T), _ctx.Address));
		}

        private void StartNewProcess()
        {
            if (Interlocked.CompareExchange(ref _inProcess, Common.True, Common.False) == Common.False)
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
                    if ((Interlocked.Read(ref _inProcess) != Common.True) ||
                        !_mailbox.TryDequeue(out Message msg))
                        break;

                    FutureMessage future = null;
                    var isFutureCall = (msg.MessageType == MessageType.FutureMessage);

                    try
                    {
                        if (isFutureCall)
                        {
                            future = (FutureMessage)msg;
                            if (future.IsCanceled)
                            {
                                future.Cancel();
                                continue;
                            }
                        }

                        Actor.OnReceive(_ctx, msg);

                        if (isFutureCall && 
                            !(future.IsCompleted || future.IsCanceled || future.IsFaulted))
                            future.Respond(DefaultResponse, _ctx.Address);
                    }
                    catch (Exception e)
                    {
                        if (isFutureCall && (future != null))
                            future.RespondToWithError(e, _ctx.Address);

                        return Task.FromException(e);
                    }
                }
            }
            finally
            {
                Interlocked.Exchange(ref _inProcess, Common.False);
                if (_mailbox.Count > 0)
                    StartNewProcess();
            }
            return ProcessCompleted;
        }
    }
}
