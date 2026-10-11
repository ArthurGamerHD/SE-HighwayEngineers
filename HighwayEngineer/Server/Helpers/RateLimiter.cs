using System;
using System.Collections.Generic;

namespace HighwayEngineer.Server.Helpers
{
    /// <summary>
    /// Per-player token buckets: up to <see cref="_burst"/> requests at once, then one
    /// more every <see cref="_refillSeconds"/>. Rejections cost nothing, so a flood of
    /// requests is turned away before any heavy work happens.
    /// </summary>
    public sealed class RateLimiter
    {
        struct Bucket
        {
            public double Tokens;
            public long UpdatedTicks;
        }

        readonly Dictionary<ulong, Bucket> _buckets = new Dictionary<ulong, Bucket>();
        readonly int _burst;
        readonly double _refillSeconds;

        public RateLimiter(int burst, double refillSeconds)
        {
            _burst = Math.Max(1, burst);
            _refillSeconds = Math.Max(0.001, refillSeconds);
        }

        /// <summary>Takes one token; false with the wait until the next one when empty.</summary>
        public bool TryTake(ulong player, out float retryAfterSeconds)
        {
            long now = DateTime.UtcNow.Ticks;
            Bucket bucket;
            if (!_buckets.TryGetValue(player, out bucket))
                bucket = new Bucket { Tokens = _burst, UpdatedTicks = now };
            double elapsed = (now - bucket.UpdatedTicks) / (double)TimeSpan.TicksPerSecond;
            bucket.Tokens = Math.Min(_burst, bucket.Tokens + elapsed / _refillSeconds);
            bucket.UpdatedTicks = now;
            if (bucket.Tokens >= 1.0)
            {
                bucket.Tokens -= 1.0;
                _buckets[player] = bucket;
                retryAfterSeconds = 0f;
                return true;
            }
            _buckets[player] = bucket;
            retryAfterSeconds = (float)((1.0 - bucket.Tokens) * _refillSeconds);
            return false;
        }

        public void Forget(ulong player)
        {
            _buckets.Remove(player);
        }
    }
}
