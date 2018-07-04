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
using System.Threading;

namespace Sweet.Actors
{
    public enum CircuitState : long
    {
        Open,
        HalfOpen,
        Closed
    }

    public class CircuitPolicy
    {
        private int _treshold;
        private int _duration;

        private long _counter;
        private long _startTime;

        public CircuitPolicy(int treshold, TimeSpan duration)
        {
            _treshold = Math.Min(1, treshold);
            _duration = Math.Min(1, Convert.ToInt32(duration.TotalMilliseconds));
        }

        public int Treshold => _treshold;

        public TimeSpan Duration => TimeSpan.FromMilliseconds(_duration);

        internal bool IsValid()
        {
            if (Interlocked.Read(ref _counter) >= _treshold)
            {
                var startTime = Interlocked.Read(ref _startTime);
                return startTime == 0 ||
                    Environment.TickCount - startTime >= _duration;
            }
            return true;
        }

        internal void Increment()
        {
            if (Interlocked.Increment(ref _counter) == _treshold)
                Interlocked.Exchange(ref _startTime, Environment.TickCount);
        }

        internal void Reset()
        {
            Interlocked.Exchange(ref _counter, 0);
            Interlocked.Exchange(ref _startTime, 0);
        }
    }

    public class CircuitBreaker
    {
        private CircuitPolicy _failPolicy;
        private CircuitPolicy _successPolicy;
        private bool _raiseErrorOnClose;

        private long _state = (long)CircuitState.Open;

        public CircuitBreaker(CircuitPolicy failPolicy = null, CircuitPolicy successPolicy = null,
            bool raiseErrorOnClose = true)
        {
            _raiseErrorOnClose = raiseErrorOnClose;

            _failPolicy = failPolicy ?? new CircuitPolicy(3, TimeSpan.FromSeconds(1));
            _successPolicy = successPolicy ?? new CircuitPolicy(1, TimeSpan.FromSeconds(1));
        }

        public bool IsClosed => Interlocked.Read(ref _state) == (long)CircuitState.Closed;

        public bool IsOpen => Interlocked.Read(ref _state) != (long)CircuitState.Closed;

        public bool Execute(Action action)
        {
            if (action == null)
            {
                Failed();
                throw new ArgumentNullException(nameof(action));
            }

            if (IsClosed)
            {
                Failed();

                if (_raiseErrorOnClose)
                    throw new Exception(Errors.CircuitIsClosed);
                return false;
            }

            try
            {
                action();
                return true;
            }
            catch (Exception)
            {
                Failed();
                throw;
            }
        }

        private void Failed()
        {

        }
    }
}
