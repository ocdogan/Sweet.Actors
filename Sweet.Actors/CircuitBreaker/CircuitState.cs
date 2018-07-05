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
    internal abstract class CircuitState
    {
        private CircuitPolicy _policy;
        private CircuitBreaker _circuitBreaker;

        public CircuitState(CircuitBreaker circuitBreaker, CircuitPolicy policy)
        {
            _policy = policy;
            _circuitBreaker = circuitBreaker;
        }

        public abstract CircuitStatus Status { get; }

        protected CircuitPolicy Policy => _policy;

        protected CircuitBreaker CircuitBreaker => _circuitBreaker;

        public bool Execute(Action action)
        {
            try
            {
                if (!OnExecute(action))
                {
                    if (!_policy.ThrowErrors)
                        return false;
                    throw new Exception(CircuitErrors.CircuitExecutionError);
                }

                Succeeded();
                return true;
            }
            catch (Exception e)
            {
                Failed(e);
                if (_policy.ThrowErrors)
                    throw;
            }
            return false;
        }

        protected virtual bool OnExecute(Action action)
        {
            action();
            return true;
        }

        private void Failed(Exception exception)
        {
            try
            {
                OnFail(exception);
            }
            catch (Exception)
            { }
        }

        private void Succeeded()
        {
            try
            {
                OnSucceed();
            }
            catch (Exception)
            { }
        }

        public abstract void Entered();

        protected abstract void OnFail(Exception exception);
        
        protected abstract void OnSucceed();
    }
}
