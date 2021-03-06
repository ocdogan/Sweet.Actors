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

namespace Sweet.Actors
{
    internal abstract class CircuitState : ICircuitState
    {
        private CircuitPolicy _policy;
        private ICircuitInvoker _invoker;
        private CircuitBreaker _circuitBreaker;

        public CircuitState(CircuitBreaker circuitBreaker, CircuitPolicy policy, ICircuitInvoker invoker)
        {
            _policy = policy;
            _invoker = invoker;
            _circuitBreaker = circuitBreaker;
        }

        public abstract CircuitStatus Status { get; }

        protected CircuitPolicy Policy => _policy;

        protected CircuitBreaker CircuitBreaker => _circuitBreaker;

        protected virtual bool OnExecute(Action action)
        {
            if (_invoker != null)
                return _invoker.Execute(action);

            action();
            return true;
        }

        protected virtual T OnExecute<T>(Func<T> function, out bool success)
        {
            if (_invoker != null)
                return _invoker.Execute(function, out success);

            success = true;
            return function();
        }

        private void Failed(Action<Exception> failureAction, Exception exception)
        {
            try
            {
                failureAction(exception);
            }
            catch (Exception)
            { }
        }

        private void Succeeded()
        {
            try
            {
                OnSuccess();
            }
            catch (Exception)
            { }
        }

        public abstract void Entered();

        protected abstract void OnFailure(Exception exception);

        protected abstract void OnSuccess();

        public bool Execute(Action action)
        {
            try
            {
                if (!OnExecute(action))
                {
                    if (!_policy.ThrowErrors)
                    {
                        Failed(OnFailure, null);
                        Failed(CircuitBreaker.OnFailure, null);

                        return false;
                    }
                    
                    throw new Exception(CircuitErrors.CircuitExecutionError);
                }

                Succeeded();
                return true;
            }
            catch (Exception e)
            {
                Failed(OnFailure, e);
                Failed(CircuitBreaker.OnFailure, e);

                if (_policy.ThrowErrors)
                    throw;
            }
            return false;
        }

        public T Execute<T>(Func<T> function, out bool success)
        {
            success = false;
            try
            {
                var result = OnExecute(function, out success);
                if (!success)
                {
                    if (_policy.ThrowErrors)
                        throw new Exception(CircuitErrors.CircuitExecutionError);

                    Failed(OnFailure, null);
                    Failed(CircuitBreaker.OnFailure, null);

                    return default(T);
                }

                Succeeded();
                return result;
            }
            catch (Exception e)
            {
                success = false;

                Failed(OnFailure, e);
                Failed(CircuitBreaker.OnFailure, e);

                if (_policy.ThrowErrors)
                    throw;
            }
            return default(T);
        }
    }
}
