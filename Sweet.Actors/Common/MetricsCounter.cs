using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class MetricsCounter
    {
        private const int DefaultTimeWindowSec = 60;

        private int _calculating;
        private int _tickedInCalculation;

        private long _value;
        private int _timeWindowMSec = DefaultTimeWindowSec;
        private ConcurrentQueue<int> _ticks = new ConcurrentQueue<int>();

        public MetricsCounter(int timeWindowSeconds = DefaultTimeWindowSec)
        {
            _timeWindowMSec = Math.Max(1, timeWindowSeconds) * 1000;
        }

        public MetricsCounter(string name, int timeWindowSeconds = DefaultTimeWindowSec)
            : this(timeWindowSeconds)
        {
            Name = name;
        }

        public string Name { get; }

        public long Value => Interlocked.Read(ref _value);

        public long Tick()
        {
            var tick = Environment.TickCount;
            var windowStart = tick - _timeWindowMSec;

            var result = Interlocked.Increment(ref _value);
            _ticks.Enqueue(tick);

            if ((result > 1) && !Calculate(windowStart))
                Interlocked.Exchange(ref _tickedInCalculation, Common.True);

            return result;
        }

        private bool Calculate(int windowStart)
        {
            if (Common.CompareAndSet(ref _calculating, false, true))
            {
                Task.Factory.StartNew(() =>
                {
                    try
                    {
                        while (_ticks.TryPeek(out int value))
                        {
                            if (value >= windowStart ||
                                !_ticks.TryDequeue(out value))
                                break;

                            Interlocked.Decrement(ref _value);
                        }
                    }
                    catch (Exception)
                    { }
                    finally
                    {
                        Interlocked.Exchange(ref _calculating, Common.False);
                        if (Common.CompareAndSet(ref _tickedInCalculation, true, false))
                            Recalculate();
                    }
                });
                return true;
            }
            return false;
        }

        public long Recalculate()
        {
            Calculate(Environment.TickCount - _timeWindowMSec);
            return Interlocked.Read(ref _value);
        }
    }
}
