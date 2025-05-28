using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IqTest_server.Data;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.Configuration;
using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IqTest_server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Roles = "Admin")] // Ensure only admins can access these endpoints
    public class MaintenanceController : BaseController
    {
        private readonly ApplicationDbContext _context;
        private readonly RedisService _redisService;
        private readonly ICacheService _cacheService;
        private readonly IConfiguration _configuration;

        public MaintenanceController(
            ApplicationDbContext context,
            RedisService redisService,
            ICacheService cacheService,
            IConfiguration configuration,
            ILogger<MaintenanceController> logger) : base(logger)
        {
            _context = context;
            _redisService = redisService;
            _cacheService = cacheService;
            _configuration = configuration;
        }

        /// <summary>
        /// Enable testing mode - unlock all tests without clearing cache
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("enable-testing-mode")]
        [AllowAnonymous] // Temporarily allow anonymous access for testing
        public async Task<IActionResult> EnableTestingMode()
        {
            try
            {
                _logger.LogInformation("Enabling testing mode - unlocking all tests");
                
                var actions = new List<string>();
                
                // Override test availability in Redis for anonymous users
                var testTypes = new[] { "mixed", "word-logic", "memory", "number-logic" };
                
                foreach (var testType in testTypes)
                {
                    try
                    {
                        // Set test availability override for anonymous users (userId = 0)
                        var overrideKey = $"test_override:0:{testType}";
                        await _redisService.SetAsync(overrideKey, true, TimeSpan.FromHours(1));
                        
                        actions.Add($"Unlocked test: {testType}");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Could not unlock test {TestType}", testType);
                    }
                }
                
                // Set global testing mode flag
                await _redisService.SetAsync("testing_mode_enabled", true, TimeSpan.FromHours(24));
                actions.Add("Set global testing mode flag");
                
                return Ok(new { 
                    success = true, 
                    message = "Testing mode enabled - all tests unlocked for 1 hour",
                    actions = actions,
                    validUntil = DateTime.UtcNow.AddHours(1),
                    timestamp = DateTime.UtcNow
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error enabling testing mode");
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to enable testing mode", 
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Clean ALL cache including questions, rate limiting, test attempts, leaderboards, etc.
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("clear-all-cache")]
        [AllowAnonymous] // Temporarily allow anonymous access for testing
        public async Task<IActionResult> ClearAllCache()
        {
            try
            {
                _logger.LogInformation("Starting complete cache purge for testing");
                
                var clearedItems = new System.Collections.Generic.Dictionary<string, int>();
                
                // 1. Clear ALL Redis keys
                try
                {
                    // Questions and test data
                    clearedItems["questions"] = await _redisService.DeleteKeysByPatternAsync("questions:*");
                    clearedItems["question_sets"] = await _redisService.DeleteKeysByPatternAsync("question_set:*");
                    
                    // Test attempts and user data
                    clearedItems["test_attempts"] = await _redisService.DeleteKeysByPatternAsync("test_attempt:*");
                    clearedItems["user_data"] = await _redisService.DeleteKeysByPatternAsync("user:*");
                    
                    // Rate limiting keys
                    clearedItems["rate_limits"] = await _redisService.DeleteKeysByPatternAsync("rate_limit:*");
                    clearedItems["api_limits"] = await _redisService.DeleteKeysByPatternAsync("api:*");
                    
                    // Leaderboard data
                    clearedItems["leaderboards"] = await _redisService.DeleteKeysByPatternAsync("leaderboard:*");
                    clearedItems["rankings"] = await _redisService.DeleteKeysByPatternAsync("ranking:*");
                    
                    // Session and auth data
                    clearedItems["sessions"] = await _redisService.DeleteKeysByPatternAsync("session:*");
                    clearedItems["auth_tokens"] = await _redisService.DeleteKeysByPatternAsync("token:*");
                    
                    // Any other patterns
                    clearedItems["cache_general"] = await _redisService.DeleteKeysByPatternAsync("cache:*");
                    clearedItems["temp_data"] = await _redisService.DeleteKeysByPatternAsync("temp:*");
                    
                    _logger.LogInformation("Redis cache cleared: {ClearedItems}", 
                        System.Text.Json.JsonSerializer.Serialize(clearedItems));
                }
                catch (Exception redisEx)
                {
                    _logger.LogWarning(redisEx, "Error clearing some Redis keys, continuing...");
                }
                
                // 2. Clear ALL in-memory cache
                try
                {
                    // Clear specific prefixes
                    _cacheService.RemoveByPrefix(CacheKeys.QuestionsPrefix);
                    _cacheService.RemoveByPrefix(CacheKeys.TestTypePrefix);
                    _cacheService.RemoveByPrefix(CacheKeys.LeaderboardPrefix);
                    _cacheService.RemoveByPrefix(CacheKeys.UserRankPrefix);
                    
                    // Clear specific keys
                    _cacheService.Remove(CacheKeys.AllTestTypes);
                    
                    // Clear any other cached items by common prefixes
                    _cacheService.RemoveByPrefix("test");
                    _cacheService.RemoveByPrefix("user");
                    _cacheService.RemoveByPrefix("auth");
                    _cacheService.RemoveByPrefix("api");
                    _cacheService.RemoveByPrefix("rate");
                    
                    _logger.LogInformation("In-memory cache cleared successfully");
                }
                catch (Exception memEx)
                {
                    _logger.LogWarning(memEx, "Error clearing some in-memory cache items, continuing...");
                }
                
                // 3. Try to flush all Redis databases (if permissions allow)
                try
                {
                    await _redisService.FlushDatabaseAsync();
                    _logger.LogInformation("Redis database flushed completely");
                }
                catch (Exception flushEx)
                {
                    _logger.LogWarning(flushEx, "Could not flush Redis database (might not have permissions)");
                }
                
                var totalCleared = clearedItems.Values.Sum();
                
                return Ok(await ClearAllCacheInternal());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during complete cache purge");
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to clear all cache", 
                    error = ex.Message 
                });
            }
        }


        /// <summary>
        /// Refresh questions from GitHub by clearing cache and pre-fetching new questions
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("refresh-questions")]
        [AllowAnonymous] // Temporarily allow anonymous access for testing
        public async Task<IActionResult> RefreshQuestionsFromGitHub()
        {
            try
            {
                _logger.LogInformation("Starting questions refresh from GitHub");
                
                var result = new Dictionary<string, object>();
                var testTypes = new[] { "number-logic", "word-logic", "memory", "mixed" };
                
                // Step 1: Aggressively clear ALL caches
                _logger.LogInformation("Aggressively clearing all question-related caches");
                
                // Clear Redis patterns
                await _redisService.DeleteKeysByPatternAsync("questions:*");
                await _redisService.DeleteKeysByPatternAsync("question_set:*");
                await _redisService.DeleteKeysByPatternAsync("answers:*");
                
                // Clear specific Redis keys for each test type
                var numericTestTypeIds = new[] { 1, 2, 3, 4 }; // number-logic, word-logic, memory, mixed
                foreach (var id in numericTestTypeIds)
                {
                    await _redisService.DeleteAsync($"questions:{id}");
                    await _redisService.DeleteAsync($"answers:{id}");
                }
                
                // Clear in-memory cache
                _cacheService.RemoveByPrefix(CacheKeys.QuestionsPrefix);
                _cacheService.RemoveByPrefix(CacheKeys.TestTypePrefix);
                _cacheService.RemoveByPrefix("answers:");
                _cacheService.Remove(CacheKeys.AllTestTypes);
                
                // Try to force garbage collection to ensure memory is freed
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                
                result["cacheCleared"] = true;
                result["cacheTypes"] = new[] { "Redis questions:*", "Redis question_set:*", "Redis answers:*", "In-memory cache", "Forced GC" };
                
                // Step 2: Fetch fresh questions from GitHub for each test type
                var githubService = HttpContext.RequestServices.GetRequiredService<GithubService>();
                var questionService = HttpContext.RequestServices.GetRequiredService<QuestionService>();
                
                var testTypeResults = new Dictionary<string, object>();
                
                foreach (var testType in testTypes)
                {
                    try
                    {
                        _logger.LogInformation("Fetching questions for test type: {TestType}", testType);
                        
                        // Add a small delay to ensure cache clearing is complete
                        await Task.Delay(100);
                        
                        // Get expected question count
                        var expectedCount = testType switch
                        {
                            "number-logic" => 24,
                            "word-logic" => 28,
                            "memory" => 20,
                            "mixed" => 40,
                            _ => 20
                        };
                        
                        // Force fetch from GitHub (bypassing cache completely)
                        var githubQuestions = await githubService.GetQuestionsAsync(testType, expectedCount, forceRefresh: true);
                        
                        // Force refresh through QuestionService as well (this ensures consistent caching)
                        var serviceQuestions = await questionService.GetQuestionsByTestTypeIdAsync(testType, forceRefresh: true);
                        var serviceQuestionsList = serviceQuestions.ToList();
                        
                        // Get some sample questions to verify content
                        var firstQuestion = githubQuestions?.FirstOrDefault()?.Question?.Text?.Substring(0, Math.Min(50, githubQuestions?.FirstOrDefault()?.Question?.Text?.Length ?? 0)) ?? "No questions";
                        var lastQuestion = githubQuestions?.LastOrDefault()?.Question?.Text?.Substring(0, Math.Min(50, githubQuestions?.LastOrDefault()?.Question?.Text?.Length ?? 0)) ?? "No questions";
                        
                        testTypeResults[testType] = new
                        {
                            githubCount = githubQuestions?.Count ?? 0,
                            serviceCount = serviceQuestionsList.Count,
                            success = githubQuestions?.Count > 0 && serviceQuestionsList.Count > 0,
                            expectedCount = expectedCount,
                            firstQuestion = firstQuestion,
                            lastQuestion = lastQuestion,
                            timestamp = DateTime.UtcNow
                        };
                        
                        _logger.LogInformation("Test type {TestType}: GitHub={GitHubCount}, Service={ServiceCount}", 
                            testType, githubQuestions?.Count ?? 0, serviceQuestionsList.Count);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error refreshing questions for test type: {TestType}", testType);
                        testTypeResults[testType] = new
                        {
                            error = ex.Message,
                            success = false
                        };
                    }
                }
                
                result["testTypes"] = testTypeResults;
                result["refreshCompleted"] = DateTime.UtcNow;
                
                // Summary
                var successfulTypes = testTypeResults.Values.Count(t => ((dynamic)t).success == true);
                result["summary"] = new
                {
                    totalTestTypes = testTypes.Length,
                    successfulRefreshes = successfulTypes,
                    allSuccessful = successfulTypes == testTypes.Length
                };
                
                _logger.LogInformation("Questions refresh completed. {SuccessCount}/{TotalCount} test types refreshed successfully", 
                    successfulTypes, testTypes.Length);
                
                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during questions refresh");
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to refresh questions from GitHub", 
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Clean questions cache to force refresh from GitHub
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("clear-questions-cache")]
        [AllowAnonymous] // Temporarily allow anonymous access for testing
        public async Task<IActionResult> ClearQuestionsCache()
        {
            try
            {
                _logger.LogInformation("Clearing questions cache to force GitHub refresh");
                
                // Delete all cached question sets from Redis
                await _redisService.DeleteKeysByPatternAsync("questions:*");
                await _redisService.DeleteKeysByPatternAsync("question_set:*");
                
                // Clear in-memory cache for questions and test types
                _cacheService.RemoveByPrefix(CacheKeys.QuestionsPrefix);
                _cacheService.RemoveByPrefix(CacheKeys.TestTypePrefix);
                _cacheService.Remove(CacheKeys.AllTestTypes);
                
                _logger.LogInformation("Questions cache cleared successfully from both Redis and memory cache");
                
                return Ok(new { 
                    success = true, 
                    message = "Questions cache successfully cleared. Next request will fetch fresh questions from GitHub." 
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing questions cache");
                return StatusCode(500, new { 
                    success = false, 
                    message = "Failed to clear questions cache", 
                    error = ex.Message 
                });
            }
        }

        /// <summary>
        /// Clean Redis cache by deleting question cache
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("clean-redis")]
        public async Task<IActionResult> CleanRedisCache()
        {
            try
            {
                _logger.LogInformation("Starting Redis cache cleanup");
                
                // Delete all cached question sets
                await _redisService.DeleteKeysByPatternAsync("questions:*");
                
                // Delete any other patterns as needed
                // For example:
                // await _redisService.DeleteKeysByPatternAsync("user:*");
                
                _logger.LogInformation("Redis cache cleanup completed successfully");
                
                return Ok(new { success = true, message = "Redis question cache successfully cleaned" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during Redis cache cleanup");
                return StatusCode(500, new { success = false, message = "Failed to clean Redis cache", error = ex.Message });
            }
        }
        
        /// <summary>
        /// Clean all Redis cache by deleting all keys
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("clean-redis-all")]
        public async Task<IActionResult> CleanAllRedisCache()
        {
            try
            {
                _logger.LogInformation("Starting complete Redis cache cleanup");
                
                // Delete all keys in Redis
                await _redisService.DeleteAllKeysAsync();
                
                _logger.LogInformation("Complete Redis cache cleanup finished successfully");
                
                return Ok(new { success = true, message = "All Redis keys successfully deleted" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during complete Redis cache cleanup");
                return StatusCode(500, new { success = false, message = "Failed to clean all Redis keys", error = ex.Message });
            }
        }

        /// <summary>
        /// Clean SQL database old test data
        /// </summary>
        /// <param name="olderThanDays">Remove test data older than specified days (default: 90)</param>
        /// <returns>Result of the operation</returns>
        [HttpPost("clean-test-data")]
        public async Task<IActionResult> CleanTestData([FromQuery] int olderThanDays = 90)
        {
            try
            {
                if (olderThanDays < 1)
                {
                    return BadRequest(new { success = false, message = "The olderThanDays parameter must be at least 1" });
                }

                _logger.LogInformation($"Starting SQL database cleanup for test data older than {olderThanDays} days");
                
                var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
                
                // Begin transaction to ensure data consistency
                using var transaction = await _context.Database.BeginTransactionAsync();
                
                try
                {
                    // Delete old answers first (child records)
                    var oldAnswers = await _context.Answers
                        .Where(a => a.TestResult.CompletedAt < cutoffDate)
                        .ToListAsync();
                    
                    _context.Answers.RemoveRange(oldAnswers);
                    
                    // Delete old test results (parent records)
                    var oldTestResults = await _context.TestResults
                        .Where(tr => tr.CompletedAt < cutoffDate)
                        .ToListAsync();
                    
                    _context.TestResults.RemoveRange(oldTestResults);
                    
                    // Save changes
                    var deletedAnswers = oldAnswers.Count;
                    var deletedTestResults = oldTestResults.Count;
                    
                    await _context.SaveChangesAsync();
                    
                    // Commit transaction
                    await transaction.CommitAsync();
                    
                    _logger.LogInformation($"SQL database cleanup completed. Deleted {deletedAnswers} answers and {deletedTestResults} test results");
                    
                    return Ok(new { 
                        success = true, 
                        message = "Test data successfully cleaned", 
                        deletedItems = new {
                            answers = deletedAnswers,
                            testResults = deletedTestResults
                        }
                    });
                }
                catch (Exception ex)
                {
                    // If any error occurs, roll back the transaction
                    await transaction.RollbackAsync();
                    throw new Exception("Transaction failed, rolling back changes", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during SQL database cleanup");
                return StatusCode(500, new { success = false, message = "Failed to clean SQL database", error = ex.Message });
            }
        }

        /// <summary>
        /// Clean orphaned data from the database
        /// </summary>
        /// <returns>Result of the operation</returns>
        [HttpPost("clean-orphaned-data")]
        public async Task<IActionResult> CleanOrphanedData()
        {
            try
            {
                _logger.LogInformation("Starting orphaned data cleanup");
                
                using var transaction = await _context.Database.BeginTransactionAsync();
                
                try
                {
                    // Find and delete orphaned answers (without a valid test result)
                    var orphanedAnswers = await _context.Answers
                        .Where(a => !_context.TestResults.Any(tr => tr.Id == a.TestResultId))
                        .ToListAsync();
                    
                    _context.Answers.RemoveRange(orphanedAnswers);
                    
                    // Find and delete orphaned leaderboard entries (without a valid user)
                    var orphanedLeaderboardEntries = await _context.LeaderboardEntries
                        .Where(le => !_context.Users.Any(u => u.Id == le.UserId))
                        .ToListAsync();
                    
                    _context.LeaderboardEntries.RemoveRange(orphanedLeaderboardEntries);
                    
                    // Save changes
                    var deletedAnswers = orphanedAnswers.Count;
                    var deletedLeaderboardEntries = orphanedLeaderboardEntries.Count;
                    
                    await _context.SaveChangesAsync();
                    
                    // Commit transaction
                    await transaction.CommitAsync();
                    
                    _logger.LogInformation($"Orphaned data cleanup completed. Deleted {deletedAnswers} orphaned answers and {deletedLeaderboardEntries} orphaned leaderboard entries");
                    
                    return Ok(new { 
                        success = true, 
                        message = "Orphaned data successfully cleaned", 
                        deletedItems = new {
                            orphanedAnswers = deletedAnswers,
                            orphanedLeaderboardEntries = deletedLeaderboardEntries
                        }
                    });
                }
                catch (Exception ex)
                {
                    // If any error occurs, roll back the transaction
                    await transaction.RollbackAsync();
                    throw new Exception("Transaction failed, rolling back changes", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during orphaned data cleanup");
                return StatusCode(500, new { success = false, message = "Failed to clean orphaned data", error = ex.Message });
            }
        }

        /// <summary>
        /// Perform complete system cleanup (Redis + SQL)
        /// </summary>
        /// <param name="olderThanDays">Remove test data older than specified days (default: 90)</param>
        /// <returns>Result of the operation</returns>
        [HttpPost("clean-all")]
        public async Task<IActionResult> CleanAll([FromQuery] int olderThanDays = 90)
        {
            try
            {
                if (olderThanDays < 1)
                {
                    return BadRequest(new { success = false, message = "The olderThanDays parameter must be at least 1" });
                }

                _logger.LogInformation($"Starting complete system cleanup (Redis + SQL, data older than {olderThanDays} days)");
                
                // Clean all Redis keys
                await _redisService.DeleteAllKeysAsync();
                
                var cutoffDate = DateTime.UtcNow.AddDays(-olderThanDays);
                
                // Begin transaction for SQL operations
                using var transaction = await _context.Database.BeginTransactionAsync();
                
                try
                {
                    // Delete old answers
                    var oldAnswers = await _context.Answers
                        .Where(a => a.TestResult.CompletedAt < cutoffDate)
                        .ToListAsync();
                    
                    _context.Answers.RemoveRange(oldAnswers);
                    
                    // Delete old test results
                    var oldTestResults = await _context.TestResults
                        .Where(tr => tr.CompletedAt < cutoffDate)
                        .ToListAsync();
                    
                    _context.TestResults.RemoveRange(oldTestResults);
                    
                    // Find and delete orphaned answers
                    var orphanedAnswers = await _context.Answers
                        .Where(a => !_context.TestResults.Any(tr => tr.Id == a.TestResultId))
                        .ToListAsync();
                    
                    _context.Answers.RemoveRange(orphanedAnswers);
                    
                    // Find and delete orphaned leaderboard entries
                    var orphanedLeaderboardEntries = await _context.LeaderboardEntries
                        .Where(le => !_context.Users.Any(u => u.Id == le.UserId))
                        .ToListAsync();
                    
                    _context.LeaderboardEntries.RemoveRange(orphanedLeaderboardEntries);
                    
                    // Save changes
                    var deletedAnswers = oldAnswers.Count;
                    var deletedTestResults = oldTestResults.Count;
                    var deletedOrphanedAnswers = orphanedAnswers.Count;
                    var deletedOrphanedLeaderboardEntries = orphanedLeaderboardEntries.Count;
                    
                    await _context.SaveChangesAsync();
                    
                    // Commit transaction
                    await transaction.CommitAsync();
                    
                    _logger.LogInformation($"Complete system cleanup completed: Deleted {deletedAnswers} answers, {deletedTestResults} test results, " +
                                          $"{deletedOrphanedAnswers} orphaned answers, and {deletedOrphanedLeaderboardEntries} orphaned leaderboard entries");
                    
                    return Ok(new { 
                        success = true, 
                        message = "Complete system cleanup was successful", 
                        deletedItems = new {
                            answers = deletedAnswers,
                            testResults = deletedTestResults,
                            orphanedAnswers = deletedOrphanedAnswers,
                            orphanedLeaderboardEntries = deletedOrphanedLeaderboardEntries
                        }
                    });
                }
                catch (Exception ex)
                {
                    // If any error occurs, roll back the transaction
                    await transaction.RollbackAsync();
                    throw new Exception("Transaction failed, rolling back changes", ex);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during complete system cleanup");
                return StatusCode(500, new { success = false, message = "Failed to perform complete system cleanup", error = ex.Message });
            }
        }

        /// <summary>
        /// Internal helper method to clear all cache
        /// </summary>
        private async Task<object> ClearAllCacheInternal()
        {
            var clearedItems = new System.Collections.Generic.Dictionary<string, int>();
            
            // Clear ALL Redis keys
            try
            {
                // Questions and test data
                clearedItems["questions"] = await _redisService.DeleteKeysByPatternAsync("questions:*");
                clearedItems["question_sets"] = await _redisService.DeleteKeysByPatternAsync("question_set:*");
                
                // Test attempts and user data
                clearedItems["test_attempts"] = await _redisService.DeleteKeysByPatternAsync("test_attempt:*");
                clearedItems["user_data"] = await _redisService.DeleteKeysByPatternAsync("user:*");
                
                // Rate limiting keys
                clearedItems["rate_limits"] = await _redisService.DeleteKeysByPatternAsync("rate_limit:*");
                clearedItems["api_limits"] = await _redisService.DeleteKeysByPatternAsync("api:*");
                
                // Leaderboard data
                clearedItems["leaderboards"] = await _redisService.DeleteKeysByPatternAsync("leaderboard:*");
                clearedItems["rankings"] = await _redisService.DeleteKeysByPatternAsync("ranking:*");
                
                // Session and auth data
                clearedItems["sessions"] = await _redisService.DeleteKeysByPatternAsync("session:*");
                clearedItems["auth_tokens"] = await _redisService.DeleteKeysByPatternAsync("token:*");
                
                // Any other patterns
                clearedItems["cache_general"] = await _redisService.DeleteKeysByPatternAsync("cache:*");
                clearedItems["temp_data"] = await _redisService.DeleteKeysByPatternAsync("temp:*");
                
                _logger.LogInformation("Redis cache cleared: {ClearedItems}", 
                    System.Text.Json.JsonSerializer.Serialize(clearedItems));
            }
            catch (Exception redisEx)
            {
                _logger.LogWarning(redisEx, "Error clearing some Redis keys, continuing...");
            }
            
            // Clear ALL in-memory cache
            try
            {
                // Clear specific prefixes
                _cacheService.RemoveByPrefix(CacheKeys.QuestionsPrefix);
                _cacheService.RemoveByPrefix(CacheKeys.TestTypePrefix);
                _cacheService.RemoveByPrefix(CacheKeys.LeaderboardPrefix);
                _cacheService.RemoveByPrefix(CacheKeys.UserRankPrefix);
                
                // Clear specific keys
                _cacheService.Remove(CacheKeys.AllTestTypes);
                
                // Clear any other cached items by common prefixes
                _cacheService.RemoveByPrefix("test");
                _cacheService.RemoveByPrefix("user");
                _cacheService.RemoveByPrefix("auth");
                _cacheService.RemoveByPrefix("api");
                _cacheService.RemoveByPrefix("rate");
                
                _logger.LogInformation("In-memory cache cleared successfully");
            }
            catch (Exception memEx)
            {
                _logger.LogWarning(memEx, "Error clearing some in-memory cache items, continuing...");
            }
            
            // Try to flush all Redis databases (if permissions allow)
            try
            {
                await _redisService.FlushDatabaseAsync();
                _logger.LogInformation("Redis database flushed completely");
            }
            catch (Exception flushEx)
            {
                _logger.LogWarning(flushEx, "Could not flush Redis database (might not have permissions)");
            }
            
            var totalCleared = clearedItems.Values.Sum();
            
            return new { 
                success = true, 
                message = $"Complete cache purge successful. Cleared {totalCleared} Redis keys and all in-memory cache.",
                details = clearedItems,
                timestamp = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Get GitHub URLs being used for question fetching
        /// </summary>
        /// <returns>GitHub URLs for each test type</returns>
        [HttpGet("github-urls")]
        [AllowAnonymous]
        public IActionResult GetGitHubUrls()
        {
            try
            {
                var baseUrl = _configuration["GitHub:BaseUrl"] ?? "https://raw.githubusercontent.com/RazvanBordinc/questions/main/";
                
                var urls = new Dictionary<string, object>
                {
                    ["baseUrl"] = baseUrl,
                    ["testTypes"] = new Dictionary<string, string>
                    {
                        ["number-logic"] = $"{baseUrl}Numerical.json",
                        ["word-logic"] = $"{baseUrl}Verbal.json",
                        ["memory"] = $"{baseUrl}Memory.json",
                        ["mixed"] = $"{baseUrl}Comprehensive.json"
                    },
                    ["timestamp"] = DateTime.UtcNow
                };
                
                return Ok(urls);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting GitHub URLs");
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}