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

namespace Sweet.Actors
{
    public abstract class OptionsBase<T>
        where T: OptionsBase<T>
    {
        private string _name;
        private IErrorHandler _errorHandler;
        private int _requestTimeoutMSec = 0;
        private int _sequentialInvokeLimit = -1;

        protected OptionsBase(string name)
        {
            _name = name?.Trim();
            if (String.IsNullOrEmpty(_name))
                _name = Constants.DefaultActorSystemName;
        }

        public T UsingRequestTimeoutMSec(int requestTimeoutMSec = 0)
        {
            _requestTimeoutMSec = Math.Min(Math.Max(-1, requestTimeoutMSec), Constants.MaxRequestTimeoutMSec);
            return (T)this;
        }

        public T UsingSequentialInvokeLimit(int sequentialInvokeLimit)
        {
            _sequentialInvokeLimit = Common.CheckSequentialInvokeLimit(sequentialInvokeLimit);
            return (T)this;
        }

        public T UsingErrorHandler(IErrorHandler errorHandler)
        {
            _errorHandler = errorHandler;
            return (T)this;
        }

        public T UsingErrorHandler(Action<IProcess, IMessage, Exception> errorHandler)
        {
            _errorHandler = (errorHandler == null) ? null : new ErrorHandlerAction(errorHandler);
            return (T)this;
        }

        public string Name => _name;

        public IErrorHandler ErrorHandler => _errorHandler;

        public int RequestTimeoutMSec => _requestTimeoutMSec;

        public int SequentialInvokeLimit => _sequentialInvokeLimit;
    }
}
