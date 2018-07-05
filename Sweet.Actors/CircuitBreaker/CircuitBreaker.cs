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
        private static readonly CircuitPolicy DefaultPolicy = new CircuitPolicy(3, TimeSpan.FromSeconds(10000), 
            TimeSpan.FromSeconds(1), 2, true);

        private CircuitState _closedState;
        private CircuitState _halfOpenState;
        private CircuitState _openState;

        private CircuitPolicy _policy;
        private CircuitState _currentState;

        private readonly object _syncRoot = new object();

        public CircuitBreaker(CircuitPolicy policy)
        {
            _policy = policy ?? DefaultPolicy;

            _closedState = new ClosedState(this, _policy);
            _halfOpenState = new HalfOpenState(this, _policy);
            _openState = new OpenState(this, _policy);

            _currentState = _closedState;
        }

        public bool IsClosed => _currentState.Status == CircuitStatus.Closed;

        public bool IsOpen => _currentState.Status != CircuitStatus.Closed;

        internal void SwitchToState(CircuitStatus newStatus)
        {
            var newState = _currentState;
            switch (newStatus)
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

            lock (_syncRoot)
            {
                if (_currentState != newState)
                {
                    _currentState = newState;
                    newState.Entered();
                }
            }
        }

        public bool Execute(Action action)
        {
            if (action == null)
                throw new ArgumentNullException(nameof(action));
            return _currentState.Execute(action);
        }
    }
}
