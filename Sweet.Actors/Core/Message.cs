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
        int TimeoutMSec { get; }
    }

    internal class Message : IMessage
    {
        public static readonly Message Empty = new Message(new object(), Aid.Unknown);

        private static readonly IReadOnlyDictionary<string, string> _defaultHeader =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        private int _creationTime;
		private int _timeoutMSec = 0;
        private IReadOnlyDictionary<string, string> _header = _defaultHeader;

		public Message(object data, Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = 0)
        {
            Data = data;
            From = from ?? Aid.Unknown;

            if (header != null)
            {
                if (header is IReadOnlyDictionary<string, string> roHeader)
                    _header = roHeader;
                else
                    _header = new ReadOnlyDictionary<string, string>(header);
            }

            _creationTime = Environment.TickCount;
            _timeoutMSec = Common.CheckMessageTimeout(timeoutMSec);
        }

        public object Data { get; }

        public IReadOnlyDictionary<string, string> Header => _header;

        public Aid From { get; }

        public virtual MessageType MessageType => MessageType.Default;

        public int TimeoutMSec => _timeoutMSec;

        public bool Expired => _timeoutMSec > 0 && 
		        (Environment.TickCount - _creationTime) >= _timeoutMSec;
    }

    public interface IFutureMessage : IMessage
    {
        Type ResponseType { get; }

        bool IsCanceled { get; }
        bool IsCompleted { get; }
        bool IsFaulted { get; }

        void Cancel();
    }

    internal abstract class FutureMessage : Message, IFutureMessage
    {
        private Type _responseType;
        protected readonly TaskCompletor<IFutureResponse> _taskCompletor;

        public FutureMessage(object data, Type responseType,
                               TaskCompletor<IFutureResponse> taskCompletor,
                               Aid from, IDictionary<string, string> header = null)
			: base(data, from, header, taskCompletor.TimeoutMSec)
        {
            _responseType = responseType;
            _taskCompletor = taskCompletor ?? new TaskCompletor<IFutureResponse>();
        }

        public Type ResponseType => _responseType;

        public override MessageType MessageType => MessageType.FutureMessage;

        public virtual bool IsCanceled => _taskCompletor.IsCanceled;

        public virtual bool IsCompleted => _taskCompletor.IsCompleted;

        public virtual bool IsFaulted => _taskCompletor.IsFaulted;

        internal abstract void Respond(object response, Aid from = null, IDictionary<string, string> header = null);

        internal abstract void RespondToWithError(Exception e, Aid from = null, IDictionary<string, string> header = null);

        public virtual void Cancel()
        {
            _taskCompletor.TrySetCanceled();
        }
    }

    internal class FutureMessage<T> : FutureMessage, IFutureMessage
    {
        public FutureMessage(object data, 
                            TaskCompletor<IFutureResponse> taskCompletor,
                            Aid from = null, IDictionary<string, string> header = null)
			: base(data, typeof(T), taskCompletor, from, header)
        {
        }

        internal override void Respond(object response, Aid from = null, IDictionary<string, string> header = null)
        {
            try
            {
                _taskCompletor.TrySetResult(response == null ? 
                        new FutureResponse<T>(from, header) :
                        new FutureResponse<T>((T)response, from, header));
            }
            catch (Exception e)
            {
                _taskCompletor.TrySetResult(new FutureError<T>(e, from, header));
            }
        }

        internal override void RespondToWithError(Exception e, Aid from = null, IDictionary<string, string> header = null)
        {
            _taskCompletor.TrySetResult(new FutureError<T>(e, from, header));
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

        public FutureResponse(Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = 0)
			: base(default(T), from, header, timeoutMSec)
        {
            _isEmpty = true;
        }

        public FutureResponse(T data, Aid from = null, IDictionary<string, string> header = null,
                                int timeoutMSec = 0)
			: base(data, from, header, timeoutMSec)
        { }

        public override MessageType MessageType => MessageType.FutureResponse;

        public bool IsEmpty => _isEmpty;
    }

    internal class FutureResponse : FutureResponse<object>, IFutureResponse<object>
    {
        public FutureResponse(Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = 0)
            : base(null, from, header, timeoutMSec)
        {
            _isEmpty = true;
        }

        public FutureResponse(object data, Aid from = null, IDictionary<string, string> header = null,
                                int timeoutMSec = 0)
            : base(data, from, header, timeoutMSec)
        { }
    }

    public interface IFutureError : IMessage
    {
        bool IsFaulted { get; }
        Exception Exception { get; }
    }

    internal class FutureError<T> : FutureResponse<T>, IFutureError
    {
        private Exception _error;

        public FutureError(Exception e, Aid from = null, IDictionary<string, string> header = null)
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
