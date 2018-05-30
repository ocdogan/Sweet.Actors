using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Sweet.Actors
{
    public class MetricsCounter
    {
        private const int DefaultTimeFrameSec = 60;

        private int _calculating;
        private int _tickedInCalculation;

        private long _value;
        private int _frameStart;
        private int _timeFrameMSec = DefaultTimeFrameSec;
        private ConcurrentQueue<int> _ticks = new ConcurrentQueue<int>();

        public MetricsCounter(int timeFrameSeconds = DefaultTimeFrameSec)
        {
            _timeFrameMSec = Math.Max(1, timeFrameSeconds) * 1000;
        }

        public MetricsCounter(string name, int timeFrameSeconds = DefaultTimeFrameSec)
            : this(timeFrameSeconds)
        {
            Name = name;
        }

        public string Name { get; }

        public int TimeFrameMSec => _timeFrameMSec;

        public long Value
        {
            get
            {
                var result = Interlocked.Read(ref _value);
                if (result > 0)
                {
                    Interlocked.MemoryBarrier();
                    var actualStart = Environment.TickCount - _timeFrameMSec;

                    Interlocked.MemoryBarrier();
                    if (actualStart > _frameStart)
                        Calculate(actualStart);
                }
                return result;
            }
        }

        public long Tick()
        {
            var tick = Environment.TickCount;
            var frameStart = tick - _timeFrameMSec;

            var result = Interlocked.Increment(ref _value);
            _ticks.Enqueue(tick);

            if (result == 1)
                Interlocked.Exchange(ref _frameStart, tick);
            else if (!Calculate(frameStart))
                Interlocked.Exchange(ref _tickedInCalculation, Common.True);

            return result;
        }

        private bool Calculate(int frameStart)
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
                            if (value >= frameStart)
                            {
                                head = value;
                                continue;
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
                        Interlocked.Exchange(ref _frameStart, head);

                        Interlocked.Exchange(ref _calculating, Common.False);

                        if (Common.CompareAndSet(ref _tickedInCalculation, true, false))
                            Calculate(Environment.TickCount - _timeFrameMSec);
                    }
                });
                return true;
            }
            return false;
        }
    }
}
