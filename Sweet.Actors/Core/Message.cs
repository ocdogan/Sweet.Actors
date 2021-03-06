﻿#region License
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
        bool IsEmpty { get; }
        bool Expired { get; }
        int? TimeoutMSec { get; }

        void Expire();
    }

    internal class Message : IMessage
    {
        public static readonly Message Empty = new Message(new object(), Aid.Unknown);

        private static readonly IReadOnlyDictionary<string, string> _defaultHeader =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>());

        private Aid _from;
        private object _data;
        private bool _isEmpty;
        private bool _expired;
        private int _creationTime;
		private int? _timeoutMSec = 0;
        private IReadOnlyDictionary<string, string> _header = _defaultHeader;

		public Message(object data, Aid from = null, 
            IDictionary<string, string> header = null, int? timeoutMSec = null)
        {
            _data = data;
            _from = from ?? Aid.Unknown;

            _isEmpty = (data == null);

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

        public object Data => _data;

        public IReadOnlyDictionary<string, string> Header => _header;

        public Aid From => _from;

        public virtual MessageType MessageType => MessageType.Default;

        public int? TimeoutMSec => _timeoutMSec;

        public bool IsEmpty => _isEmpty;

        public bool Expired
        {
            get
            {
                if (!_expired)
                {
                    _expired = _timeoutMSec.HasValue &&
                       _timeoutMSec > 0 && (Environment.TickCount - _creationTime) >= _timeoutMSec;
                }
                return _expired;
            }
        }
        public void Expire()
        {
            _expired = true;
        }
    }

    public delegate void TimeoutEventHandler(object sender, TaskCompletionStatus status);

    public interface IFutureMessage : IMessage
    {
        event TimeoutEventHandler OnTimeout;

        bool IsCanceled { get; }
        bool IsCompleted { get; }
        bool IsFaulted { get; }

        Task<IFutureResponse> Task { get; }

        void Cancel();
        void Respond(object response, Aid from = null, IDictionary<string, string> header = null);
        void RespondWithError(Exception e, Aid from = null, IDictionary<string, string> header = null);
    }

    internal class FutureMessage : Message, IFutureMessage
    {
        private int _registered;
        private int? _timeoutMSec;

        protected readonly TaskCompletionSource<IFutureResponse> _tcs;

        public event TimeoutEventHandler OnTimeout;

        public FutureMessage(object data, Aid from, 
            IDictionary<string, string> header = null, int? timeoutMSec = null)
			: base(data, from, header, timeoutMSec)
        {
            _tcs = new TaskCompletionSource<IFutureResponse>();

            _timeoutMSec = Common.CheckMessageTimeout(timeoutMSec);
            if (_timeoutMSec.HasValue)
            {
                _registered = 1;
                TimeoutHandler.TryRegister(this, DoTimedOut, _timeoutMSec.Value);
            }
        }

        public override MessageType MessageType => MessageType.FutureMessage;

        public virtual bool IsCanceled => _tcs.Task.IsCanceled;

        public virtual bool IsCompleted => _tcs.Task.IsCompleted;

        public virtual bool IsFaulted => _tcs.Task.IsFaulted;

        public Task<IFutureResponse> Task => _tcs.Task;

        public virtual void Respond(object response, Aid from = null, IDictionary<string, string> header = null)
        {
            try
            {
                Unregister();
                _tcs.TrySetResult(response == null ?
                        new FutureResponse(from, header) :
                        new FutureResponse(response, from, header));
            }
            catch (Exception e)
            {
                _tcs.TrySetResult(new FutureError(e, from, header));
            }
        }

        public virtual void RespondWithError(Exception e, Aid from = null, IDictionary<string, string> header = null)
        {
            Unregister();
            _tcs.TrySetResult(new FutureError(e, from, header));
        }

        public void Cancel()
        {
            Unregister();

            var task = _tcs?.Task;
            if (!(task?.IsCompleted ?? true))
                _tcs.TrySetCanceled();
        }

        private void DoTimedOut()
        {
            var status = TaskStatus.RanToCompletion;
            try
            {
                Interlocked.Exchange(ref _registered, 0);

                Expire();

                status = _tcs?.Task.Status ?? TaskStatus.RanToCompletion;
                if (!(status == TaskStatus.RanToCompletion ||
                    status == TaskStatus.Canceled || status == TaskStatus.Faulted))
                {
                    Cancel();
                    status = _tcs?.Task.Status ?? TaskStatus.RanToCompletion;
                }
            }
            finally
            {
                OnTimeout?.Invoke(this, ToTaskCompletionStatus(status));
            }
        }

        private static TaskCompletionStatus ToTaskCompletionStatus(TaskStatus status)
        {
            switch (status)
            {
                case TaskStatus.Canceled:
                    return TaskCompletionStatus.Canceled;
                case TaskStatus.Created:
                    return TaskCompletionStatus.Created;
                case TaskStatus.Faulted:
                    return TaskCompletionStatus.Failed;
                default:
                    return TaskCompletionStatus.Running;
            }
        }

        public void Unregister()
        {
            if (Interlocked.CompareExchange(ref _registered, 0, 1) == 1)
                TimeoutHandler.Unregister(this);
        }
    }

    public interface IFutureResponse : IMessage
    { }

    internal class FutureResponse : Message, IFutureResponse
    {
        public static readonly FutureResponse Unknown = new FutureResponse(new object(), Aid.Unknown);

        public FutureResponse(Aid from = null, IDictionary<string, string> header = null, int timeoutMSec = 0)
			: base(null, from, header, timeoutMSec)
        { }

        public FutureResponse(object data, Aid from = null, IDictionary<string, string> header = null,
                                int timeoutMSec = 0)
			: base(data, from, header, timeoutMSec)
        { }

        public override MessageType MessageType => MessageType.FutureResponse;
    }

    public interface IFutureError : IFutureResponse
    {
        bool IsFaulted { get; }
        Exception Exception { get; }
    }

    internal class FutureError : FutureResponse, IFutureError
    {
        public static readonly FutureError MessageExpired = new FutureError(new Exception(Errors.MessageExpired), Aid.Unknown);

        public FutureError(Exception e, Aid from = null, IDictionary<string, string> header = null)
            : base(e, from, header)
        { }

        public override MessageType MessageType => MessageType.FutureError;

        public Exception Exception => Data as Exception;

        public bool IsFaulted => Data is Exception;
    }
}
