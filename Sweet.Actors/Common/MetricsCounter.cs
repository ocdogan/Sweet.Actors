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
        private int _windowStart;
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

        public long Value
        {
            get
            {
                var result = Interlocked.Read(ref _value);
                if (result > 0)
                {
                    Interlocked.MemoryBarrier();
                    var actualStart = Environment.TickCount - _timeWindowMSec;

                    Interlocked.MemoryBarrier();
                    if (actualStart > _windowStart)
                        Calculate(actualStart);
                }
                return result;
            }
        }

        public long Tick()
        {
            var tick = Environment.TickCount;
            var windowStart = tick - _timeWindowMSec;

            var result = Interlocked.Increment(ref _value);
            _ticks.Enqueue(tick);

            if (result == 1)
                Interlocked.Exchange(ref _windowStart, tick);
            else if (!Calculate(windowStart))
                Interlocked.Exchange(ref _tickedInCalculation, Common.True);

            return result;
        }

        private bool Calculate(int windowStart)
        {
            if (Common.CompareAndSet(ref _calculating, false, true))
            {
                Task.Factory.StartNew(() =>
                {
                    var head = 0;
                    try
                    {
                        while (_ticks.TryPeek(out int value))
                        {
                            if (value >= windowStart)
                            {
                                head = value;
                                break;
                            }

                            if (!_ticks.TryDequeue(out value))
                                break;

                            Interlocked.Decrement(ref _value);
                        }
                    }
                    catch (Exception)
                    { }
                    finally
                    {
                        Interlocked.Exchange(ref _windowStart, head);

                        Interlocked.Exchange(ref _calculating, Common.False);

                        if (Common.CompareAndSet(ref _tickedInCalculation, true, false))
                            Calculate(Environment.TickCount - _timeWindowMSec);
                    }
                });
                return true;
            }
            return false;
        }
    }
}
