using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Collections.Generic;
using QuestionSetItem = IqTest_server.Services.GithubService.QuestionSetItem;

namespace IqTest_server.Services
{
    public class RedisService
    {
        private readonly IConnectionMultiplexer _redis;
        private readonly ILogger<RedisService> _logger;
        private readonly IDatabase _database;
        private readonly TimeSpan _defaultExpiry = TimeSpan.FromDays(7); // Cache for a week by default

        private bool _isRedisAvailable = true;

        public RedisService(
            IConnectionMultiplexer redis,
            ILogger<RedisService> logger)
        {
            _redis = redis;
            _logger = logger;
            
            try
            {
                _database = redis.GetDatabase();
                // Test connection
                var ping = _database.Ping();
                _logger.LogInformation("Redis connection established successfully. Ping: {Ping}ms", ping.TotalMilliseconds);
            }
            catch (Exception ex)
            {
                _isRedisAvailable = false;
                _logger.LogWarning(ex, "Failed to connect to Redis. The system will operate with reduced functionality.");
                // Create a dummy database to avoid null reference exceptions
                _database = redis.GetDatabase(-1);
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
            
            try
            {
                var jsonData = JsonSerializer.Serialize(value);
                return await _database.StringSetAsync(key, jsonData, expiry ?? _defaultExpiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value in Redis for key: {Key}", key);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                return false;
            }
        }

        public async Task<T> GetAsync<T>(string key)
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
            
            try
            {
                var value = await _database.StringGetAsync(key);
                if (value.IsNullOrEmpty)
                {
                    return default;
                }

                return JsonSerializer.Deserialize<T>(value);
            }
            catch (Exception ex)
            {
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
            
            try
            {
                return await _database.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
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
            
            try
            {
                return await _database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
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
                return null;
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
                return null;
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
        /// <returns>Task</returns>
        public async Task DeleteKeysByPatternAsync(string pattern)
        {
            // Try to reconnect if Redis was previously unavailable
            if (!_isRedisAvailable)
            {
                await TryReconnectIfNeededAsync();
                
                if (!_isRedisAvailable)
                {
                    _logger.LogDebug("Skipping Redis DeleteKeysByPatternAsync because Redis is not available for pattern: {Pattern}", pattern);
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

                    var keys = new List<RedisKey>();
                    await foreach (var key in server.KeysAsync(pattern: pattern))
                    {
                        keys.Add(key);
                    }

                    if (keys.Count > 0)
                    {
                        await _database.KeyDeleteAsync(keys.ToArray());
                        _logger.LogInformation($"Deleted {keys.Count} keys matching pattern: {pattern}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error deleting keys by pattern: {Pattern}", pattern);
                _isRedisAvailable = false; // Mark Redis as unavailable after error
                // Don't throw the exception, just log it
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
    }
}