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
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public abstract class MetricsValueBase
    {
        protected struct BucketItem
        {
            public int Tick;
            public double Value;
        }

		protected const int DefaultTimeFrameSec = 60;

		protected int _calculating;
		protected int _tickedInCalculation;

		protected int _count;
		protected double _value;

		protected int _frameStart;
		protected int _timeFrameMSec = DefaultTimeFrameSec;
		protected ConcurrentBag<BucketItem> _ticks = new ConcurrentBag<BucketItem>();

        public MetricsValueBase(int timeFrameSeconds = DefaultTimeFrameSec)
        {
            _timeFrameMSec = Math.Max(1, timeFrameSeconds) * 1000;
        }

        public MetricsValueBase(string name, int timeFrameSeconds = DefaultTimeFrameSec)
            : this(timeFrameSeconds)
        {
            Name = name;
        }

        public string Name { get; }

        public int TimeFrameMSec => _timeFrameMSec;

        public int Count => _count;

        public double Value
        {
            get
            {
                Interlocked.MemoryBarrier();
                var result = _value;

                Interlocked.MemoryBarrier();
                var actualStart = Environment.TickCount - _timeFrameMSec;

                Interlocked.MemoryBarrier();
                if (actualStart > _frameStart)
                    Calculate(actualStart);

                return result;
            }
        }

        public double Tick(double value)
        {
            Interlocked.MemoryBarrier();
            var tick = Environment.TickCount;
            var frameStart = tick - _timeFrameMSec;

            Interlocked.MemoryBarrier();
            var count = Interlocked.Increment(ref _count);

            Interlocked.MemoryBarrier();
            var result = Tick(value, count, _value);

            _ticks.Add(new BucketItem { Tick = tick, Value = value });

            if (count == 1)
                Interlocked.Exchange(ref _frameStart, tick);
            else if (!Calculate(frameStart))
				Interlocked.Exchange(ref _tickedInCalculation, Constants.True);

            return result;
        }

		protected abstract double Tick(double value, int count, double current);

        protected double Increment(double value)
        {
            while (true)
            {
                var current = _value;
                var newValue = current + value;

				if (Interlocked.CompareExchange(ref _value, newValue, current) == current)
                    return newValue;
            }
        }

        private bool Calculate(int frameStart)
        {
            if (Common.CompareAndSet(ref _calculating, false, true))
            {
                Task.Factory.StartNew(() =>
                {
                    var prevFrameStart = _frameStart;

                    var head = 0;
                    var count = 0;
                    var newValue = 0d;
                    var changed = false;
                    try
                    {
                        for (var i = _ticks.Count - 1; i > -1; i--)
                        {
                            if (!_ticks.TryPeek(out BucketItem item))
                                break;

                            if (item.Tick >= frameStart)
                            {
                                head = item.Tick;

                                count++;
                                newValue = Calculate(count, newValue, item);

                                continue;
                            }

                            if (!_ticks.TryTake(out item))
                                break;

                            changed = true;
                        }
                    }
                    catch (Exception)
                    { }
                    finally
                    {
                        if (changed &&
                            Interlocked.CompareExchange(ref _frameStart, head, prevFrameStart) == prevFrameStart)
                        {
                            Interlocked.Exchange(ref _value, newValue);
                            Interlocked.Exchange(ref _count, count);
                        }

						Interlocked.Exchange(ref _calculating, Constants.False);

                        if (Common.CompareAndSet(ref _tickedInCalculation, true, false))
                            Calculate(Environment.TickCount - _timeFrameMSec);
                    }
                });
                return true;
            }
            return false;
        }

		protected abstract double Calculate(int count, double newValue, BucketItem item);
    }
}
