using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using IqTest_server.Data;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
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

        public MaintenanceController(
            ApplicationDbContext context,
            RedisService redisService,
            ICacheService cacheService,
            ILogger<MaintenanceController> logger) : base(logger)
        {
            _context = context;
            _redisService = redisService;
            _cacheService = cacheService;
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
    }
}