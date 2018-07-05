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

namespace Sweet.Actors
{
    internal class ClosedState : CircuitState
    {
        private readonly object _syncRoot = new object();

        private LinkedList<int> _failures = new LinkedList<int>();

        public ClosedState(CircuitBreaker circuitBreaker, CircuitPolicy policy)
            : base(circuitBreaker, policy)
        { }

        public override CircuitStatus Status => CircuitStatus.Closed;

        public override void Entered()
        {
            lock (_syncRoot)
            {
                _failures.Clear();
            }
        }

        protected override void OnFailure(Exception exception)
        {
            var thresholdExceeded = false;
            lock (_syncRoot)
            {
                var tickTime = Environment.TickCount;
                UpdateWindow(tickTime);

                _failures.AddLast(tickTime);

                if (_failures.Count >= Policy.FailureCountToOpen)
                {
                    thresholdExceeded = true;
                    _failures.Clear();
                }
            }

            if (thresholdExceeded)
                CircuitBreaker.SwitchToState(CircuitStatus.HalfOpen);
        }

        private void UpdateWindow(int windowEndTime)
        {
            var windowStartTime = windowEndTime - Policy.FailureTrackWindow;
            while (_failures.Count > 0)
            {
                if (_failures.First.Value < windowStartTime)
                    _failures.RemoveFirst();
            }
        }

        protected override void OnSucceed()
        { }
    }
}
