using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace IqTest_server.Services
{
    public class RateLimitingService
    {
        private readonly IDistributedCache _cache;
        private readonly ILogger<RateLimitingService> _logger;

        public RateLimitingService(IDistributedCache cache, ILogger<RateLimitingService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<bool> CheckRateLimitAsync(string clientId, string endpoint, int maxAttempts, TimeSpan window)
        {
            var key = $"rate_limit:{endpoint}:{clientId}";
            var lockKey = $"{key}:lock";

            try
            {
                // Use distributed lock to prevent race conditions
                var lockAcquired = await TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    _logger.LogWarning("Failed to acquire lock for rate limiting check");
                    return false; // Block request if we can't acquire lock
                }

                try
                {
                    var data = await _cache.GetStringAsync(key);
                    var attempts = new RateLimitData();

                    if (!string.IsNullOrEmpty(data))
                    {
                        attempts = JsonConvert.DeserializeObject<RateLimitData>(data);
                    }

                    // Clean up old attempts
                    var cutoffTime = DateTime.UtcNow.Subtract(window);
                    attempts.Timestamps.RemoveAll(t => t < cutoffTime);

                    // Check if we've exceeded the limit
                    if (attempts.Timestamps.Count >= maxAttempts)
                    {
                        _logger.LogWarning($"Rate limit exceeded for {clientId} on {endpoint}. Attempts: {attempts.Timestamps.Count}");
                        return false;
                    }

                    // Add new attempt
                    attempts.Timestamps.Add(DateTime.UtcNow);

                    // Save back to cache
                    var options = new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = window
                    };

                    await _cache.SetStringAsync(key, JsonConvert.SerializeObject(attempts), options);
                    return true;
                }
                finally
                {
                    await ReleaseLockAsync(lockKey);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error checking rate limit for {clientId} on {endpoint}");
                return true; // Allow request on error to avoid blocking legitimate users
            }
        }

        public async Task<RateLimitStatus> GetRateLimitStatusAsync(string clientId, string endpoint, int maxAttempts, TimeSpan window)
        {
            var key = $"rate_limit:{endpoint}:{clientId}";

            try
            {
                var data = await _cache.GetStringAsync(key);
                if (string.IsNullOrEmpty(data))
                {
                    return new RateLimitStatus
                    {
                        AttemptsRemaining = maxAttempts,
                        ResetsAt = DateTime.UtcNow.Add(window)
                    };
                }

                var attempts = JsonConvert.DeserializeObject<RateLimitData>(data);
                
                // Clean up old attempts
                var cutoffTime = DateTime.UtcNow.Subtract(window);
                attempts.Timestamps.RemoveAll(t => t < cutoffTime);

                var remainingAttempts = Math.Max(0, maxAttempts - attempts.Timestamps.Count);
                var oldestAttempt = attempts.Timestamps.Count > 0 ? attempts.Timestamps[0] : DateTime.UtcNow;
                var resetsAt = oldestAttempt.Add(window);

                return new RateLimitStatus
                {
                    AttemptsRemaining = remainingAttempts,
                    ResetsAt = resetsAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting rate limit status for {clientId} on {endpoint}");
                return new RateLimitStatus
                {
                    AttemptsRemaining = maxAttempts,
                    ResetsAt = DateTime.UtcNow.Add(window)
                };
            }
        }

        private async Task<bool> TryAcquireLockAsync(string lockKey, TimeSpan timeout)
        {
            var lockValue = Guid.NewGuid().ToString();
            var endTime = DateTime.UtcNow.Add(timeout);

            while (DateTime.UtcNow < endTime)
            {
                try
                {
                    var existingLock = await _cache.GetStringAsync(lockKey);
                    if (string.IsNullOrEmpty(existingLock))
                    {
                        var options = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
                        };
                        await _cache.SetStringAsync(lockKey, lockValue, options);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error acquiring distributed lock");
                }

                await Task.Delay(50);
            }

            return false;
        }

        private async Task ReleaseLockAsync(string lockKey)
        {
            try
            {
                await _cache.RemoveAsync(lockKey);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error releasing distributed lock");
            }
        }
    }

    public class RateLimitData
    {
        public List<DateTime> Timestamps { get; set; } = new List<DateTime>();
    }

    public class RateLimitStatus
    {
        public int AttemptsRemaining { get; set; }
        public DateTime ResetsAt { get; set; }
    }
}