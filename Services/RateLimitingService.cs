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

            // Quickly check if the client ID contains "user:" (authenticated user)
            // If it's an authenticated user, bypass rate limiting for specific endpoints
            if (clientId.StartsWith("user:") && 
                (endpoint.Contains("/api/auth/disconnect") || 
                 endpoint.Contains("/api/auth/logout") || 
                 endpoint.Contains("/api/auth/check-username") ||
                 endpoint.Contains("/api/auth/refresh-token")))
            {
                _logger.LogInformation($"Bypassing rate limit for authenticated user {clientId} on {endpoint}");
                return true; // Allow authenticated users to access these endpoints without rate limiting
            }

            try
            {
                // Use distributed lock to prevent race conditions
                var lockAcquired = await TryAcquireLockAsync(lockKey, TimeSpan.FromSeconds(5));
                if (!lockAcquired)
                {
                    _logger.LogWarning("Failed to acquire lock for rate limiting check");
                    
                    // If we can't acquire lock, allow the request if it's for critical endpoints
                    if (endpoint.Contains("/api/health") || 
                        endpoint.Contains("/api/auth/disconnect") || 
                        endpoint.Contains("/api/auth/logout") ||
                        endpoint.Contains("/api/auth/check-username") ||
                        endpoint.Contains("/api/auth/create-user") ||
                        endpoint.Contains("/api/auth/register"))
                    {
                        _logger.LogInformation($"Bypassing rate limit for critical endpoint {endpoint} due to lock acquisition failure");
                        return true;
                    }
                    
                    return false; // Block request if we can't acquire lock for non-critical endpoints
                }

                try
                {
                    RateLimitData attempts;
                    
                    try 
                    {
                        var data = await _cache.GetStringAsync(key);
                        attempts = new RateLimitData();

                        if (!string.IsNullOrEmpty(data))
                        {
                            attempts = JsonConvert.DeserializeObject<RateLimitData>(data);
                        }
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogError(cacheEx, $"Error accessing distributed cache for rate limiting. Allowing request.");
                        return true; // If cache read fails, allow the request
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
                    try 
                    {
                        var options = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = window
                        };

                        await _cache.SetStringAsync(key, JsonConvert.SerializeObject(attempts), options);
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogError(cacheEx, $"Error writing to distributed cache for rate limiting. Continuing without caching.");
                        // Allow the request to proceed even if we can't update the cache
                    }
                    
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
            
            // If it's a critical endpoint or authenticated user for specific operations, 
            // return a more generous rate limit status
            if ((clientId.StartsWith("user:") && 
                 (endpoint.Contains("/api/auth/disconnect") || 
                  endpoint.Contains("/api/auth/logout") || 
                  endpoint.Contains("/api/auth/check-username") ||
                  endpoint.Contains("/api/auth/refresh-token"))) ||
                endpoint.Contains("/api/health"))
            {
                return new RateLimitStatus
                {
                    AttemptsRemaining = maxAttempts, // Always show max attempts remaining
                    ResetsAt = DateTime.UtcNow.Add(window)
                };
            }

            try
            {
                string data;
                try 
                {
                    data = await _cache.GetStringAsync(key);
                }
                catch (Exception cacheEx)
                {
                    _logger.LogError(cacheEx, $"Error accessing distributed cache for rate limit status. Using default values.");
                    return new RateLimitStatus
                    {
                        AttemptsRemaining = maxAttempts,
                        ResetsAt = DateTime.UtcNow.Add(window)
                    };
                }
                
                if (string.IsNullOrEmpty(data))
                {
                    return new RateLimitStatus
                    {
                        AttemptsRemaining = maxAttempts,
                        ResetsAt = DateTime.UtcNow.Add(window)
                    };
                }

                RateLimitData attempts;
                try
                {
                    attempts = JsonConvert.DeserializeObject<RateLimitData>(data);
                }
                catch (Exception jsonEx)
                {
                    _logger.LogError(jsonEx, $"Error deserializing rate limit data. Using default values.");
                    return new RateLimitStatus
                    {
                        AttemptsRemaining = maxAttempts,
                        ResetsAt = DateTime.UtcNow.Add(window)
                    };
                }
                
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
            int attempts = 0;
            
            while (DateTime.UtcNow < endTime)
            {
                attempts++;
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
                    // Special handling for Redis connection failures
                    if (ex.Message.Contains("Connection") || 
                        ex.Message.Contains("Timeout") || 
                        ex.Message.Contains("Redis"))
                    {
                        _logger.LogError(ex, "Redis connection error acquiring distributed lock. Allowing operation to continue without lock.");
                        return true; // Consider the lock acquired if Redis is unavailable
                    }
                    
                    _logger.LogError(ex, $"Error acquiring distributed lock (attempt {attempts})");
                    
                    // If we've tried multiple times and keep getting errors, assume Redis is down
                    if (attempts >= 3)
                    {
                        _logger.LogWarning("Multiple failures acquiring lock. Assuming Redis is unavailable and allowing operation.");
                        return true;
                    }
                }

                // Exponential backoff with jitter to reduce contention
                var delay = Math.Min(50 * Math.Pow(2, attempts - 1), 1000);
                var jitter = new Random().Next((int)(delay * 0.8), (int)(delay * 1.2));
                await Task.Delay((int)jitter);
            }

            // If we timeout waiting for the lock, log but allow operation to continue
            _logger.LogWarning($"Timeout waiting for distributed lock after {attempts} attempts. Allowing operation to continue.");
            return true;
        }

        private async Task ReleaseLockAsync(string lockKey)
        {
            try
            {
                await _cache.RemoveAsync(lockKey);
            }
            catch (Exception ex)
            {
                // If it's a connection issue, just log at debug level to avoid filling logs
                if (ex.Message.Contains("Connection") || 
                    ex.Message.Contains("Timeout") || 
                    ex.Message.Contains("Redis"))
                {
                    _logger.LogDebug(ex, "Redis connection error releasing distributed lock. This is non-critical.");
                }
                else
                {
                    _logger.LogError(ex, "Error releasing distributed lock");
                }
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