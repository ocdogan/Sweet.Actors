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
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public enum MessageType
    {
        Default = 0,
        FutureMessage = 1,
        FutureResponse = 2,
        FutureError = 3
    }

    public interface IMessage
    {
        object Data { get; }
        IReadOnlyDictionary<string, string> Header { get; }
        Aid From { get; }
        MessageType MessageType { get; }
		bool Expired { get; }
    }

    public class Message : IMessage
    {
        public static readonly Message Empty = new Message(new object(), Aid.Unknown);

		private int _creationTime;
		private int _timeoutMSec = -1;
        private IReadOnlyDictionary<string, string> _header;
        private static readonly IReadOnlyDictionary<string, string> _defaultHeader =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

		internal Message(object data, Aid from = null, IDictionary<string, string> header = null, 
		                 int timeoutMSec = -1)
        {
            Data = data;
            From = from ?? Aid.Unknown;
            _header = (header != null) ? new ReadOnlyDictionary<string, string>(header) : _defaultHeader;

			_timeoutMSec = timeoutMSec;

			if (_timeoutMSec < -1)
                _timeoutMSec = -1;
			else if (_timeoutMSec > 0)
				_creationTime = Environment.TickCount;
        }

        public object Data { get; }

        public IReadOnlyDictionary<string, string> Header => _header;

        public Aid From { get; }

        public virtual MessageType MessageType => MessageType.Default;

		public bool Expired => _timeoutMSec > 0 && 
		        (Environment.TickCount - _creationTime) >= _timeoutMSec;
    }

    public interface IFutureMessage : IMessage
    {
        Type ResponseType { get; }

        bool IsCanceled { get; }
        bool IsCompleted { get; }
        bool IsFaulted { get; }

        int TimeoutMSec { get; }

        void Cancel();
    }

    internal abstract class FutureMessage : Message, IFutureMessage
    {
        internal FutureMessage(object data, Type responseType,
                               Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = -1)
			: base(data, from, header, timeoutMSec)
        {
            ResponseType = responseType;
            TimeoutMSec = timeoutMSec;
        }

        public int TimeoutMSec { get; }

        public Type ResponseType { get; }

        public override MessageType MessageType => MessageType.FutureMessage;

        public abstract bool IsCanceled { get; }

        public abstract bool IsCompleted { get; }

        public abstract bool IsFaulted { get; }

        internal abstract void Respond(object response, Aid from = null, IDictionary<string, string> header = null);

        internal abstract void RespondToWithError(Exception e, Aid from = null, IDictionary<string, string> header = null);

        public abstract void Cancel();
    }

    internal class FutureMessage<T> : FutureMessage, IFutureMessage
    {
        private CancellationTokenSource _cts;
        private TaskCompletionSource<IFutureResponse<T>> _tcs;

        internal FutureMessage(object data,
                               CancellationTokenSource cancellationTokenSource,
                               TaskCompletionSource<IFutureResponse<T>> taskCompletionSource,
                               Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = -1)
			: base(data, typeof(T), from, header, timeoutMSec)
        {
            _cts = cancellationTokenSource ?? (timeoutMSec > 0 ? new CancellationTokenSource(timeoutMSec) : new CancellationTokenSource());
            _tcs = taskCompletionSource ?? new TaskCompletionSource<IFutureResponse<T>>(_cts);
        }

        public override MessageType MessageType => MessageType.FutureMessage;

        public override bool IsCanceled => _tcs.Task.IsCanceled || ((_cts != null) && _cts.IsCancellationRequested);

        public override bool IsCompleted => _tcs.Task.IsCompleted;

        public override bool IsFaulted => _tcs.Task.IsFaulted;

        internal override void Respond(object response, Aid from = null, IDictionary<string, string> header = null)
        {
            try
            {
                _tcs.SetResult(response == null ? new FutureResponse<T>(from, header) :
                               new FutureResponse<T>((T)response, from, header));
            }
            catch (Exception e)
            {
                _tcs.SetResult(new FutureError<T>(e, from, header));
            }
        }

        internal override void RespondToWithError(Exception e, Aid from = null, IDictionary<string, string> header = null)
        {
            _tcs.SetResult(new FutureError<T>(e, from, header));
        }

        public override void Cancel()
        {
            if (_cts != null)
                _tcs.TrySetCanceled(_cts.Token);
            else _tcs.TrySetCanceled();
        }
    }
    
    public interface IFutureResponse : IMessage
    {
        bool IsEmpty { get; }
    }

    public interface IFutureResponse<T> : IFutureResponse
    { }

    internal class FutureResponse<T> : Message, IFutureResponse<T>, IFutureResponse
    {
        protected bool _isEmpty;

		internal FutureResponse(Aid from = null, IDictionary<string, string> header = null,
                         int timeoutMSec = -1)
			: base(default(T), from, header, timeoutMSec)
        {
            _isEmpty = true;
        }

        internal FutureResponse(T data,
		                        Aid from = null, IDictionary<string, string> header = null,
                                int timeoutMSec = -1)
			: base(data, from, header, timeoutMSec)
        { }

        public override MessageType MessageType => MessageType.FutureResponse;

        public bool IsEmpty => _isEmpty;
    }

    public interface IFutureError : IMessage
    {
        bool IsFaulted { get; }
        Exception Exception { get; }
    }

    internal class FutureError<T> : FutureResponse<T>, IFutureError
    {
        private Exception _error;

        internal FutureError(Exception e,
                                Aid from = null, IDictionary<string, string> header = null)
            : base(default(T), from, header)
        {
            _error = e;
            _isEmpty = true;
        }

        public override MessageType MessageType => MessageType.FutureError;

        public Exception Exception => _error;

        public bool IsFaulted => _error != null;
    }
}
