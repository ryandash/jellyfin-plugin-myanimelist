using System;
using System.Collections.Generic;
using System.Threading;

namespace Jellyfin.Plugin.MyAnimeList.Providers.MyAnimeList.APIs.JikanSystem
{
    public class CacheExpiryScheduler : IDisposable
    {
        private readonly object _lock = new();
        private readonly SortedSet<long> _schedule = new();
        private readonly Timer _timer;
        private readonly Action _onExpire;

        public CacheExpiryScheduler(Action onExpire)
        {
            _onExpire = onExpire;
            _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
        }

        public void Schedule(long expiryTicks)
        {
            var runAt = expiryTicks + TimeSpan.FromMinutes(1).Ticks;

            lock (_lock)
            {
                _schedule.Add(runAt);
                ResetTimer();
            }
        }

        private void ResetTimer()
        {
            if (_schedule.Count == 0)
            {
                _timer.Change(Timeout.Infinite, Timeout.Infinite);
                return;
            }

            var next = _schedule.Min;

            var dueTicks = next - DateTime.UtcNow.Ticks;

            if (dueTicks <= 0)
            {
                _timer.Change(0, Timeout.Infinite);
                return;
            }

            var ms = (long)TimeSpan.FromTicks(dueTicks).TotalMilliseconds;

            if (ms > int.MaxValue)
                ms = int.MaxValue;

            _timer.Change(ms, Timeout.Infinite);
        }

        private void Tick()
        {
            var now = DateTime.UtcNow.Ticks;

            bool shouldRunCleanup = false;

            lock (_lock)
            {
                while (_schedule.Count > 0 && _schedule.Min <= now)
                {
                    _schedule.Remove(_schedule.Min);
                    shouldRunCleanup = true;
                }

                ResetTimer();
            }

            if (shouldRunCleanup)
            {
                _onExpire?.Invoke();
            }
        }

        public void Dispose()
        {
            _timer.Dispose();
        }
    }
}
