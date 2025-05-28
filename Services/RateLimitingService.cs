using System;
using System.Threading;
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
                    
                    // For Render free tier, be more lenient with lock failures
                    if (Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null)
                    {
                        _logger.LogInformation($"Render environment detected. Allowing request despite lock failure for {endpoint}");
                        return true;
                    }
                    
                    // If we can't acquire lock, allow the request if it's for critical endpoints
                    if (endpoint.Contains("/api/health") || 
                        endpoint.Contains("/api/auth/disconnect") || 
                        endpoint.Contains("/api/auth/logout") ||
                        endpoint.Contains("/api/auth/check-username") ||
                        endpoint.Contains("/api/auth/create-user") ||
                        endpoint.Contains("/api/auth/register") ||
                        endpoint.Contains("/api/test/availability"))  // Add test availability as critical
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
                        // Use helper method with 5 second timeout for Upstash
                        var data = await GetStringWithTimeoutAsync(key, TimeSpan.FromSeconds(5));
                        attempts = new RateLimitData();

                        if (!string.IsNullOrEmpty(data))
                        {
                            attempts = JsonConvert.DeserializeObject<RateLimitData>(data) ?? new RateLimitData();
                        }
                    }
                    catch (Exception cacheEx)
                    {
                        _logger.LogError(cacheEx, $"Error accessing distributed cache for rate limiting. Allowing request.");
                        return true; // If cache read fails, allow the request
                    }

                    // Clean up old attempts
                    var cutoffTime = DateTime.UtcNow.Subtract(window);
                    attempts?.Timestamps?.RemoveAll(t => t < cutoffTime);

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

                        // Use helper method with 5 second timeout for Upstash
                        var success = await SetStringWithTimeoutAsync(key, JsonConvert.SerializeObject(attempts), options, TimeSpan.FromSeconds(5));
                        if (!success)
                        {
                            _logger.LogWarning($"Failed to update rate limit cache for {key}, but allowing request to proceed");
                        }
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
                string? data;
                try 
                {
                    // Use helper method with 3 second timeout for status checks
                    data = await GetStringWithTimeoutAsync(key, TimeSpan.FromSeconds(3));
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

                RateLimitData? attempts;
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
                attempts?.Timestamps?.RemoveAll(t => t < cutoffTime);
                
                if (attempts == null)
                {
                    return new RateLimitStatus
                    {
                        AttemptsRemaining = maxAttempts,
                        ResetsAt = DateTime.UtcNow.Add(window)
                    };
                }

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
            
            // For Render free tier, reduce timeout to avoid long waits during cold starts
            if (Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null)
            {
                timeout = TimeSpan.FromSeconds(1); // Shorter timeout for Render
                endTime = DateTime.UtcNow.Add(timeout);
            }
            
            while (DateTime.UtcNow < endTime)
            {
                attempts++;
                try
                {
                    // Use helper method with 2 second timeout for lock operations
                    var existingLock = await GetStringWithTimeoutAsync(lockKey, TimeSpan.FromSeconds(2));
                    if (string.IsNullOrEmpty(existingLock))
                    {
                        var options = new DistributedCacheEntryOptions
                        {
                            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(10)
                        };
                        var success = await SetStringWithTimeoutAsync(lockKey, lockValue, options, TimeSpan.FromSeconds(2));
                        if (success) return true;
                    }
                }
                catch (StackExchange.Redis.RedisConnectionException redisEx)
                {
                    // Specific handling for StackExchange.Redis connection exceptions
                    _logger.LogWarning("Redis connection unavailable for rate limiting. Allowing operation to continue without lock. Error: {Message}", redisEx.Message);
                    return true; // Allow operation when Redis is unavailable
                }
                catch (StackExchange.Redis.RedisTimeoutException timeoutEx)
                {
                    // Specific handling for Redis timeout exceptions
                    _logger.LogWarning("Redis timeout for rate limiting. Allowing operation to continue without lock. Error: {Message}", timeoutEx.Message);
                    return true; // Allow operation on timeout
                }
                catch (Exception ex)
                {
                    // For any other exception, check the message
                    if (ex.Message.Contains("Connection") || 
                        ex.Message.Contains("Timeout") || 
                        ex.Message.Contains("Redis") ||
                        ex.Message.Contains("backlog"))
                    {
                        _logger.LogWarning("Redis unavailable for rate limiting. Allowing operation to continue without lock. Error: {Message}", ex.Message);
                        return true; // Consider the lock acquired if Redis is unavailable
                    }
                    
                    _logger.LogError(ex, $"Unexpected error acquiring distributed lock (attempt {attempts})");
                    
                    // If we've tried multiple times and keep getting errors, assume Redis is down
                    if (attempts >= 2) // Reduced from 3 to 2 for faster failover
                    {
                        _logger.LogWarning("Multiple failures acquiring lock. Assuming Redis is unavailable and allowing operation.");
                        return true;
                    }
                }

                // Shorter exponential backoff for Render environment
                var baseDelay = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null ? 25 : 50;
                var delay = Math.Min(baseDelay * Math.Pow(2, attempts - 1), 500); // Max 500ms instead of 1000ms
                var jitter = new Random().Next((int)(delay * 0.8), (int)(delay * 1.2));
                await Task.Delay((int)jitter);
            }

            // If we timeout waiting for the lock, log but allow operation to continue
            _logger.LogWarning($"Timeout waiting for distributed lock after {attempts} attempts. Allowing operation to continue.");
            return true;
        }

        // Helper method to get string from cache with timeout
        private async Task<string?> GetStringWithTimeoutAsync(string key, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                return await _cache.GetStringAsync(key, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Cache GET operation timed out after {timeout.TotalMilliseconds}ms for key: {key}");
                return null;
            }
            catch (Exception ex) when (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || 
                                      ex.Message.Contains("backlog", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"Redis timeout on GET for key: {key}. Error: {ex.Message}");
                return null;
            }
        }

        // Helper method to set string in cache with timeout
        private async Task<bool> SetStringWithTimeoutAsync(string key, string value, DistributedCacheEntryOptions options, TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                await _cache.SetStringAsync(key, value, options, cts.Token);
                return true;
            }
            catch (OperationCanceledException)
            {
                _logger.LogWarning($"Cache SET operation timed out after {timeout.TotalMilliseconds}ms for key: {key}");
                return false;
            }
            catch (Exception ex) when (ex.Message.Contains("timeout", StringComparison.OrdinalIgnoreCase) || 
                                      ex.Message.Contains("backlog", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogWarning($"Redis timeout on SET for key: {key}. Error: {ex.Message}");
                return false;
            }
        }

        private async Task ReleaseLockAsync(string lockKey)
        {
            try
            {
                // Use cancellation token with 2 second timeout for lock release
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
                await _cache.RemoveAsync(lockKey, cts.Token);
            }
            catch (OperationCanceledException)
            {
                _logger.LogDebug("Lock release operation timed out for key: {LockKey}. This is non-critical.", lockKey);
            }
            catch (Exception ex)
            {
                // If it's a connection issue, just log at debug level to avoid filling logs
                if (ex.Message.Contains("Connection") || 
                    ex.Message.Contains("Timeout") || 
                    ex.Message.Contains("Redis") ||
                    ex.Message.Contains("backlog"))
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