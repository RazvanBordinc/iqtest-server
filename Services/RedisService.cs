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

        public RedisService(
            IConnectionMultiplexer redis,
            ILogger<RedisService> logger)
        {
            _redis = redis;
            _logger = logger;
            _database = redis.GetDatabase();
        }

        public async Task<bool> SetAsync<T>(string key, T value, TimeSpan? expiry = null)
        {
            try
            {
                var jsonData = JsonSerializer.Serialize(value);
                return await _database.StringSetAsync(key, jsonData, expiry ?? _defaultExpiry);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error setting value in Redis for key: {Key}", key);
                return false;
            }
        }

        public async Task<T> GetAsync<T>(string key)
        {
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
                return default;
            }
        }

        public async Task<bool> RemoveAsync(string key)
        {
            try
            {
                return await _database.KeyDeleteAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error removing key from Redis: {Key}", key);
                return false;
            }
        }

        public async Task<bool> KeyExistsAsync(string key)
        {
            try
            {
                return await _database.KeyExistsAsync(key);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if key exists in Redis: {Key}", key);
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
        /// Delete all keys matching a pattern
        /// </summary>
        /// <param name="pattern">The pattern to match (e.g., "questions:*")</param>
        /// <returns>Task</returns>
        public async Task DeleteKeysByPatternAsync(string pattern)
        {
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
                throw;
            }
        }

        /// <summary>
        /// Delete all keys in the Redis database
        /// </summary>
        /// <returns>Task</returns>
        public async Task DeleteAllKeysAsync()
        {
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
                throw;
            }
        }
    }
}