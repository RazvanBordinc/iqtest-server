using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public interface ICacheService
    {
        Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null);
        void Remove(string key);
        void RemoveByPrefix(string prefix);
    }

    public class CacheService : ICacheService
    {
        private readonly IMemoryCache _cache;
        private readonly ILogger<CacheService> _logger;
        private readonly HashSet<string> _cacheKeys = new();
        private readonly object _lock = new();

        // Default cache durations
        public static readonly TimeSpan ShortCacheDuration = TimeSpan.FromMinutes(5);
        public static readonly TimeSpan MediumCacheDuration = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan LongCacheDuration = TimeSpan.FromHours(1);

        public CacheService(IMemoryCache cache, ILogger<CacheService> logger)
        {
            _cache = cache;
            _logger = logger;
        }

        public async Task<T> GetOrCreateAsync<T>(string key, Func<Task<T>> factory, TimeSpan? expiration = null)
        {
            if (_cache.TryGetValue<T>(key, out var cachedValue))
            {
                _logger.LogDebug("Cache hit for key: {Key}", key);
                return cachedValue;
            }

            _logger.LogDebug("Cache miss for key: {Key}", key);
            
            var value = await factory();
            
            var cacheOptions = new MemoryCacheEntryOptions
            {
                AbsoluteExpirationRelativeToNow = expiration ?? MediumCacheDuration,
                SlidingExpiration = TimeSpan.FromMinutes(5)
            };

            _cache.Set(key, value, cacheOptions);
            
            lock (_lock)
            {
                _cacheKeys.Add(key);
            }

            return value;
        }

        public void Remove(string key)
        {
            _cache.Remove(key);
            lock (_lock)
            {
                _cacheKeys.Remove(key);
            }
            _logger.LogDebug("Removed cache key: {Key}", key);
        }

        public void RemoveByPrefix(string prefix)
        {
            List<string> keysToRemove;
            lock (_lock)
            {
                keysToRemove = _cacheKeys.Where(k => k.StartsWith(prefix)).ToList();
            }

            foreach (var key in keysToRemove)
            {
                Remove(key);
            }
            
            _logger.LogDebug("Removed {Count} cache keys with prefix: {Prefix}", keysToRemove.Count, prefix);
        }
    }

    public static class CacheKeys
    {
        public const string LeaderboardPrefix = "leaderboard:";
        public const string TestTypePrefix = "testtype:";
        public const string QuestionsPrefix = "questions:";
        public const string UserRankPrefix = "userrank:";

        public static string GlobalLeaderboard(int page, int pageSize) => $"{LeaderboardPrefix}global:{page}:{pageSize}";
        public static string TestLeaderboard(int testTypeId, int page, int pageSize) => $"{LeaderboardPrefix}test:{testTypeId}:{page}:{pageSize}";
        public static string UserRanking(int userId) => $"{UserRankPrefix}{userId}";
        public static string Questions(int testTypeId) => $"{QuestionsPrefix}{testTypeId}";
        public static string AllTestTypes => $"{TestTypePrefix}all";
    }
}