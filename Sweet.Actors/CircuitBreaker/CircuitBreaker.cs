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
    public class CircuitBreaker
    {
        private CircuitState _closedState;
        private CircuitState _halfOpenState;
        private CircuitState _openState;

        private CircuitPolicy _policy;
        private CircuitState _currentState;

        private Action<CircuitBreaker, Exception> _onFailure;
        private Action<CircuitBreaker, CircuitStatus> _onStateChange;

        private readonly object _syncRoot = new object();

        public CircuitBreaker(CircuitPolicy policy = null, 
            Action<CircuitBreaker, Exception> onFailure = null, 
            Action<CircuitBreaker, CircuitStatus> onStateChange = null)
        {
            _onFailure = onFailure;
            _onStateChange = onStateChange;

            _policy = policy ?? CircuitPolicy.DefaultPolicy;

            _closedState = new ClosedState(this, _policy);
            _halfOpenState = new HalfOpenState(this, _policy);
            _openState = new OpenState(this, _policy);

            _currentState = _closedState;
        }

        public bool IsClosed => _currentState.Status == CircuitStatus.Closed;

        public bool IsOpen => _currentState.Status != CircuitStatus.Closed;

        internal void OnFailure(Exception exception)
        {
            _onFailure?.Invoke(this, exception);
        }

        internal void SwitchToState(CircuitStatus status)
        {
            var newState = _currentState;
            switch (status)
            {
                case CircuitStatus.HalfOpen:
                    newState = _halfOpenState;
                    break;
                case CircuitStatus.Open:
                    newState = _openState;
                    break;
                case CircuitStatus.Closed:
                    newState = _closedState;
                    break;
            }

            var changed = false;
            lock (_syncRoot)
            {
                if (_currentState != newState)
                {
                    _currentState = newState;
                    changed = true;

                    newState.Entered();
                }
            }

            if (changed)
            {
                try
                {
                    _onStateChange?.Invoke(this, status);
                }
                catch (Exception)
                { }
            }
        }

        public bool Execute(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return _currentState.Execute(action);
        }

        public TResult Execute<TResult>(Func<TResult> function, out bool success)
        {
            if (function == null)
                throw new ArgumentNullException(nameof(function));
            return _currentState.Execute(function, out success);
        }

        public bool Execute<T>(Action<T> action, T value)
        {
            return Execute(() => action(value));
        }

        public bool Execute<T1, T2>(Action<T1, T2> action, T1 value1, T2 value2)
        {
            return Execute(() => action(value1, value2));
        }

        public bool Execute<T1, T2, T3>(Action<T1, T2, T3> action, T1 value1, T2 value2, T3 value3)
        {
            return Execute(() => action(value1, value2, value3));
        }

        public bool Execute<T1, T2, T3, T4>(Action<T1, T2, T3, T4> action, T1 value1, T2 value2,
            T3 value3, T4 value4)
        {
            return Execute(() => action(value1, value2, value3, value4));
        }

        public bool Execute<T1, T2, T3, T4, T5>(Action<T1, T2, T3, T4, T5> action, T1 value1, T2 value2,
            T3 value3, T4 value4, T5 value5)
        {
            return Execute(() => action(value1, value2, value3, value4, value5));
        }

        public bool Execute<T1, T2, T3, T4, T5, T6>(Action<T1, T2, T3, T4, T5, T6> action, T1 value1, T2 value2,
            T3 value3, T4 value4, T5 value5, T6 value6)
        {
            return Execute(() => action(value1, value2, value3, value4, value5, value6));
        }

        public bool Execute<T1, T2, T3, T4, T5, T6, T7>(Action<T1, T2, T3, T4, T5, T6, T7> action, T1 value1, T2 value2,
            T3 value3, T4 value4, T5 value5, T6 value6, T7 value7)
        {
            return Execute(() => action(value1, value2, value3, value4, value5, value6, value7));
        }

        public TResult Execute<T1, TResult>(Func<T1, TResult> function, T1 value1, out bool success)
        {
            return Execute(() => function(value1), out success);
        }

        public TResult Execute<T1, T2, TResult>(Func<T1, T2, TResult> function, T1 value1, T2 value2, out bool success)
        {
            return Execute(() => function(value1, value2), out success);
        }

        public TResult Execute<T1, T2, T3, TResult>(Func<T1, T2, T3, TResult> function, T1 value1, T2 value2,
            T3 value3, out bool success)
        {
            return Execute(() => function(value1, value2, value3), out success);
        }

        public TResult Execute<T1, T2, T3, T4, TResult>(Func<T1, T2, T3, T4, TResult> function, T1 value1, T2 value2,
            T3 value3, T4 value4, out bool success)
        {
            return Execute(() => function(value1, value2, value3, value4), out success);
        }

        public TResult Execute<T1, T2, T3, T4, T5, TResult>(Func<T1, T2, T3, T4, T5, TResult> function, T1 value1, T2 value2,
            T3 value3, T4 value4, T5 value5, out bool success)
        {
            return Execute(() => function(value1, value2, value3, value4, value5), out success);
        }

        public TResult Execute<T1, T2, T3, T4, T5, T6, TResult>(Func<T1, T2, T3, T4, T5, T6, TResult> function, T1 value1, T2 value2,
            T3 value3, T4 value4, T5 value5, T6 value6, out bool success)
        {
            return Execute(() => function(value1, value2, value3, value4, value5, value6), out success);
        }
    }
}
