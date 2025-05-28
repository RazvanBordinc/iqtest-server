using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Microsoft.Extensions.Caching.Distributed;
using QuestionSetItem = IqTest_server.Services.GithubService.QuestionSetItem;

namespace IqTest_server.Services
{
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisService> _logger;
        private readonly LoggingService _loggingService;
        private readonly IDatabase _database;
        private readonly IConfiguration _configuration;
        private readonly TimeSpan _defaultExpiry = TimeSpan.FromDays(7); // Cache for a week by default
        private readonly bool _isUpstash;
        private readonly string _redisConnectionString;

        private bool _isRedisAvailable = true;

        public RedisService(
            IConnectionMultiplexer redis,
            ILogger<RedisService> logger,
            LoggingService loggingService,
            IConfiguration configuration)
        {
            _redis = redis;
            _logger = logger;
            _loggingService = loggingService;
            _configuration = configuration;
            
            if (redis == null)
            {
                _logger.LogError("Redis connection is null - Redis operations will fail");
                _isRedisAvailable = false;
                return;
            }
            
            try
            {
                _database = redis.GetDatabase();
                // Test connection
                var ping = _database.Ping();
                _logger.LogInformation("RedisService constructor: Redis connection established successfully. Ping: {Ping}ms", ping.TotalMilliseconds);
                _isRedisAvailable = true;
                
                // Determine if we're using Upstash Redis
                _redisConnectionString = Environment.GetEnvironmentVariable("REDIS_URL") ?? 
                                       _configuration["Redis:ConnectionString"] ?? 
                                       "localhost:6379";
                _isUpstash = _redisConnectionString.Contains("upstash") || _redisConnectionString.Contains("rediss://");
                
                // Log Redis connection details
                var redisEndpoints = _redis.GetEndPoints().Select(ep => ep.ToString()).ToList();
                
                _loggingService.LogInfo("Redis service initialized", new Dictionary<string, object>
                {
                    { "isUpstash", _isUpstash },
                    // Don't log full connection string for security
                    { "connectionType", _isUpstash ? "Upstash Redis" : "Standard Redis" },
                    { "endpoints", string.Join(", ", redisEndpoints) },
                    { "clientName", _redis.ClientName },
                    { "ping", ping.TotalMilliseconds }
                });
            }
            catch (Exception ex)
            {
                _isRedisAvailable = false;
                _logger.LogWarning(ex, "Failed to connect to Redis. The system will operate with reduced functionality.");
                // Create a dummy database to avoid null reference exceptions
                _database = redis.GetDatabase(-1);
                
                _redisConnectionString = _configuration["Redis:ConnectionString"] ?? "localhost:6379";
                _isUpstash = _redisConnectionString.Contains("upstash") || _redisConnectionString.Contains("rediss://");
            }
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis SetAsync because Redis is not available for key: {Key}", key);
                    return false;
                }
            }
            
            var stopwatch = Stopwatch.StartNew();
            var expiryToUse = expiry ?? _defaultExpiry;
            var valueType = typeof(T).Name;
            var valueSize = 0;
            
            try
            {
                var jsonData = JsonSerializer.Serialize(value);
                valueSize = jsonData?.Length ?? 0;
                
                // Log Redis operation
                _loggingService.LogDebug($"Redis SET: {key}", new Dictionary<string, object>
                {
                    { "operation", "SET" },
                    { "key", key },
                    { "valueType", valueType },
                    { "valueSize", valueSize },
                    { "expiry", expiryToUse.TotalSeconds },
                    { "isUpstash", _isUpstash }
                });
                
                var result = await _database.StringSetAsync(key, jsonData, expiryToUse);
                
                stopwatch.Stop();
                
                // Log successful operation
                _loggingService.LogDebug($"Redis SET completed: {key}", new Dictionary<string, object>
                {
                    { "operation", "SET" },
                    { "key", key }, 
                    { "success", result },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                // Log error with detailed information
                _loggingService.LogError($"Redis SET error: {key}", ex, new Dictionary<string, object>
                {
                    { "operation", "SET" },
                    { "key", key },
                    { "valueType", valueType },
                    { "valueSize", valueSize },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                _logger.LogError(ex, "Error setting value in Redis for key: {Key}", key);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return false;
            }
        }

        public async Task<T> GetAsync<T>(string key, System.Threading.CancellationToken cancellationToken = default)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis GetAsync because Redis is not available for key: {Key}", key);
                    return default;
                }
            }
            
            var stopwatch = Stopwatch.StartNew();
            var returnType = typeof(T).Name;
            
            try
            {
                // Check if operation is canceled
                cancellationToken.ThrowIfCancellationRequested();
                
                // Log Redis operation
                _logger.LogInformation("RedisService: Starting GET operation for key {Key}, returnType: {ReturnType}", key, returnType);
                _loggingService.LogDebug($"Redis GET: {key}", new Dictionary<string, object>
                {
                    { "operation", "GET" },
                    { "key", key },
                    { "returnType", returnType },
                    { "isUpstash", _isUpstash }
                });
                
                // Create a task for the Redis operation
                var getTask = _database.StringGetAsync(key);
                
                // Add a timeout to the Redis operation
                TimeSpan operationTimeout = TimeSpan.FromSeconds(2); // 2-second timeout
                
                // Wait for either the Redis operation or the timeout/cancellation
                Task completedTask;
                if (cancellationToken != default)
                {
                    completedTask = await Task.WhenAny(getTask, Task.Delay(operationTimeout, cancellationToken));
                }
                else
                {
                    completedTask = await Task.WhenAny(getTask, Task.Delay(operationTimeout));
                }
                
                // If Redis operation wasn't the first to complete
                if (completedTask != getTask)
                {
                    stopwatch.Stop();
                    _logger.LogWarning("Redis GET operation timed out for key: {Key} after {Elapsed}ms", 
                        key, stopwatch.ElapsedMilliseconds);
                    
                    _loggingService.LogWarning($"Redis GET timeout: {key}", new Dictionary<string, object>
                    {
                        { "operation", "GET" },
                        { "key", key },
                        { "durationMs", stopwatch.ElapsedMilliseconds },
                        { "timedOut", true }
                    });
                    
                    // Consider Redis unavailable after multiple timeouts
                    return default;
                }
                
                // Redis operation completed, get the result
                var value = await getTask;
                stopwatch.Stop();
                
                if (value.IsNullOrEmpty)
                {
                    // Log cache miss
                    _logger.LogInformation("RedisService: GET miss for key {Key} - value not found", key);
                    _loggingService.LogDebug($"Redis GET miss: {key}", new Dictionary<string, object>
                    {
                        { "operation", "GET" },
                        { "key", key }, 
                        { "found", false },
                        { "durationMs", stopwatch.ElapsedMilliseconds },
                        { "isUpstash", _isUpstash }
                    });
                    
                    return default;
                }

                var result = JsonSerializer.Deserialize<T>(value);
                var valueSize = value.ToString().Length;
                
                // Log cache hit
                _logger.LogInformation("RedisService: GET hit for key {Key} - value found, size: {Size} bytes", key, valueSize);
                _loggingService.LogDebug($"Redis GET hit: {key}", new Dictionary<string, object>
                {
                    { "operation", "GET" },
                    { "key", key }, 
                    { "found", true },
                    { "valueSize", valueSize },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                return result;
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                stopwatch.Stop();
                _logger.LogWarning("Redis GET operation was canceled for key: {Key}", key);
                return default;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                // Log error with detailed information
                _loggingService.LogError($"Redis GET error: {key}", ex, new Dictionary<string, object>
                {
                    { "operation", "GET" },
                    { "key", key },
                    { "returnType", returnType },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                _logger.LogError(ex, "Error getting value from Redis for key: {Key}", key);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return default;
            }
        }

        public async Task<bool> RemoveAsync(string key)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis RemoveAsync because Redis is not available for key: {Key}", key);
                    return false;
                }
            }
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // Log Redis operation
                _loggingService.LogDebug($"Redis DELETE: {key}", new Dictionary<string, object>
                {
                    { "operation", "DELETE" },
                    { "key", key },
                    { "isUpstash", _isUpstash }
                });
                
                var result = await _database.KeyDeleteAsync(key);
                stopwatch.Stop();
                
                // Log result
                _loggingService.LogDebug($"Redis DELETE completed: {key}", new Dictionary<string, object>
                {
                    { "operation", "DELETE" },
                    { "key", key },
                    { "success", result },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                // Log error
                _loggingService.LogError($"Redis DELETE error: {key}", ex, new Dictionary<string, object>
                {
                    { "operation", "DELETE" },
                    { "key", key },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                _logger.LogError(ex, "Error removing key from Redis: {Key}", key);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return false;
            }
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis KeyExistsAsync because Redis is not available for key: {Key}", key);
                    return false;
                }
            }
            
            var stopwatch = Stopwatch.StartNew();
            
            try
            {
                // Log Redis operation
                _loggingService.LogDebug($"Redis EXISTS: {key}", new Dictionary<string, object>
                {
                    { "operation", "EXISTS" },
                    { "key", key },
                    { "isUpstash", _isUpstash }
                });
                
                var result = await _database.KeyExistsAsync(key);
                stopwatch.Stop();
                
                // Log result
                _loggingService.LogDebug($"Redis EXISTS completed: {key}", new Dictionary<string, object>
                {
                    { "operation", "EXISTS" },
                    { "key", key },
                    { "exists", result },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                return result;
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                
                // Log error
                _loggingService.LogError($"Redis EXISTS error: {key}", ex, new Dictionary<string, object>
                {
                    { "operation", "EXISTS" },
                    { "key", key },
                    { "durationMs", stopwatch.ElapsedMilliseconds },
                    { "isUpstash", _isUpstash }
                });
                
                _logger.LogError(ex, "Error checking if key exists in Redis: {Key}", key);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return false;
            }
        }

        // Methods for storing/retrieving question sets
        public async Task<bool> StoreQuestionSetAsync(string testTypeId, DateTime generationDate, List<QuestionSetItem> questions)
        {
            try
            {
                string key = GetQuestionSetKey(testTypeId, generationDate);
                return await SetAsync(key, questions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error storing question set in Redis for test type: {TestTypeId}", testTypeId);
                return false;
            }
        }

        public async Task<List<QuestionSetItem>> GetQuestionSetAsync(string testTypeId, DateTime date)
        {
            try
            {
                string key = GetQuestionSetKey(testTypeId, date);
                return await GetAsync<List<QuestionSetItem>>(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question set from Redis for test type: {TestTypeId}", testTypeId);
                return new List<QuestionSetItem>(); // Return empty list instead of null
            }
        }

        public async Task<List<QuestionSetItem>> GetLatestQuestionSetAsync(string testTypeId)
        {
            try
            {
                // Try to get today's question set first
                var today = DateTime.UtcNow.Date;
                var questions = await GetQuestionSetAsync(testTypeId, today);

                // If not found, try yesterday
                if (questions == null || questions.Count == 0)
                {
                    questions = await GetQuestionSetAsync(testTypeId, today.AddDays(-1));
                }

                return questions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving latest question set for test type: {TestTypeId}", testTypeId);
                return new List<QuestionSetItem>(); // Return empty list instead of null
            }
        }

        private string GetQuestionSetKey(string testTypeId, DateTime date)
        {
            return $"questions:{testTypeId}:{date.ToString("yyyyMMdd")}";
        }
        
        /// <summary>
        /// Attempts to check if Redis is available and reconnect if it was previously unavailable
        /// </summary>
        private async Task TryReconnectIfNeededAsync()
        {
            // If Redis is already available, do nothing
            if (_isRedisAvailable) return;
            
            try
            {
                // Try to ping Redis
                var ping = await _database.PingAsync();
                _logger.LogInformation("Redis connection reestablished. Ping: {Ping}ms", ping.TotalMilliseconds);
                _isRedisAvailable = true;
            }
            catch (Exception ex)
            {
                // Redis is still unavailable, log at debug level to avoid spamming logs
                _logger.LogDebug(ex, "Attempted to reconnect to Redis but it is still unavailable");
            }
        }

        /// <summary>
        /// Delete all keys matching a pattern
        /// </summary>
        /// <param name="pattern">The pattern to match (e.g., "questions:*")</param>
        /// <returns>Number of keys deleted</returns>
        public async Task<int> DeleteKeysByPatternAsync(string pattern)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis DeleteKeysByPatternAsync because Redis is not available for pattern: {Pattern}", pattern);
                    return 0;
                }
            }
            
            try
            {
                var endpoints = _redis.GetEndPoints();
                int totalDeleted = 0;
                
                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected || server.IsReplica) continue;

                    var keys = new List<RedisKey>();
                    await foreach (var key in server.KeysAsync(pattern: pattern))
                    {
                        keys.Add(key);
                    }

                    if (keys.Count > 0)
                    {
                        await _database.KeyDeleteAsync(keys.ToArray());
                        totalDeleted += keys.Count;
                        _logger.LogInformation($"Deleted {keys.Count} keys matching pattern: {pattern}");
                    }
                }
                
                return totalDeleted;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting keys by pattern: {Pattern}", pattern);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return 0;
            }
        }

        /// <summary>
        /// Delete all keys in the Redis database
        /// </summary>
        /// <returns>Task</returns>
        public async Task DeleteAllKeysAsync()
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis DeleteAllKeysAsync because Redis is not available");
                    return;
                }
            }
            
            try
            {
                var endpoints = _redis.GetEndPoints();
                int totalKeysDeleted = 0;

                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected || server.IsReplica) continue;

                    var keys = new List<RedisKey>();
                    await foreach (var key in server.KeysAsync(pattern: "*"))
                    {
                        keys.Add(key);
                    }

                    if (keys.Count > 0)
                    {
                        await _database.KeyDeleteAsync(keys.ToArray());
                        totalKeysDeleted += keys.Count;
                    }
                }

                _logger.LogInformation($"Successfully deleted all {totalKeysDeleted} keys from Redis");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting all keys from Redis");
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                // Don't throw the exception, just log it
            }
        }

        /// <summary>
        /// Flush the entire Redis database
        /// </summary>
        /// <returns>Task</returns>
        public async Task FlushDatabaseAsync()
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis FlushDatabaseAsync because Redis is not available");
                    return;
                }
            }
            
            try
            {
                var endpoints = _redis.GetEndPoints();
                foreach (var endpoint in endpoints)
                {
                    var server = _redis.GetServer(endpoint);
                    if (!server.IsConnected || server.IsReplica) continue;
                    
                    // Flush the database
                    await server.FlushDatabaseAsync();
                    _logger.LogInformation("Successfully flushed Redis database");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error flushing Redis database");
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                // Don't throw the exception, just log it
            }
        }
    }
}