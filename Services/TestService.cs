using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.DTOs.Test;
using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace IqTest_server.Services
{
    public class TestService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TestService> _logger;
        private readonly QuestionService _questionService;
        private readonly AnswerValidatorService _answerValidator;
        private readonly RedisService _redisService;

        public TestService(
            ApplicationDbContext context,
            ILogger<TestService> logger,
            QuestionService questionService,
            AnswerValidatorService answerValidator,
            RedisService redisService)
        {
            _context = context;
            _logger = logger;
            _questionService = questionService;
            _answerValidator = answerValidator;
            _redisService = redisService;
        }

        public async Task<List<TestTypeDto>> GetAllTestTypesAsync()
        {
            try
            {
                return TestTypeData.GetAllTestTypes();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving all test types");
                throw;
            }
        }

        public async Task<TestTypeDto> GetTestTypeByIdAsync(string testTypeId)
        {
            try
            {
                return TestTypeData.GetTestTypeById(testTypeId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<bool> CanUserTakeTestAsync(int userId, string testTypeId, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                if (userId <= 0) // Anonymous user or invalid user
                {
                    return true;
                }
                
                // Use cache key with user ID and test type
                var key = $"test_attempt:{userId}:{testTypeId}";
                
                // Try to get from cache with timeout
                Task<DateTime?> getTask = _redisService.GetAsync<DateTime?>(key);
                
                // Add a timeout to the Redis operation
                if (await Task.WhenAny(getTask, Task.Delay(2000, cancellationToken)) != getTask)
                {
                    _logger.LogWarning("Redis GetAsync timed out for user {UserId}, test {TestTypeId}", userId, testTypeId);
                    return true; // Default to allowing the test on timeout
                }
                
                // Complete the task if it hasn't completed yet
                var lastAttempt = await getTask;
                
                if (!lastAttempt.HasValue)
                {
                    return true; // No previous attempt
                }
                
                var timeSinceLastAttempt = DateTime.UtcNow - lastAttempt.Value;
                return timeSinceLastAttempt.TotalHours >= 24;
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                _logger.LogWarning("CanUserTakeTest canceled for user {UserId}, test {TestTypeId}", userId, testTypeId);
                return true; // Allow test if canceled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if user {UserId} can take test {TestTypeId}", userId, testTypeId);
                return true; // Allow test in case of error
            }
        }
        
        public async Task<TimeSpan?> GetTimeUntilNextAttemptAsync(int userId, string testTypeId, System.Threading.CancellationToken cancellationToken = default)
        {
            try
            {
                if (userId <= 0) // Anonymous user or invalid user
                {
                    return null; // No cooldown for anonymous users
                }
                
                var key = $"test_attempt:{userId}:{testTypeId}";
                
                // Try to get from cache with timeout
                Task<DateTime?> getTask = _redisService.GetAsync<DateTime?>(key);
                
                // Add a timeout to the Redis operation
                if (await Task.WhenAny(getTask, Task.Delay(2000, cancellationToken)) != getTask)
                {
                    _logger.LogWarning("Redis GetAsync timed out for calculating cooldown: user {UserId}, test {TestTypeId}", userId, testTypeId);
                    return null; // No cooldown on timeout
                }
                
                // Complete the task if it hasn't completed yet
                var lastAttempt = await getTask;
                
                if (!lastAttempt.HasValue)
                {
                    return null; // No cooldown
                }
                
                var timeSinceLastAttempt = DateTime.UtcNow - lastAttempt.Value;
                if (timeSinceLastAttempt.TotalHours >= 24)
                {
                    return null; // Cooldown expired
                }
                
                return TimeSpan.FromHours(24) - timeSinceLastAttempt;
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                _logger.LogWarning("GetTimeUntilNextAttempt canceled for user {UserId}, test {TestTypeId}", userId, testTypeId);
                return null; // No cooldown if canceled
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting time until next attempt for user {UserId}, test {TestTypeId}", userId, testTypeId);
                return null; // No cooldown in case of error
            }
        }

        public async Task<(List<QuestionDto> Questions, TimeSpan TimeLimit)> GenerateQuestionsForTestAsync(string testTypeId)
        {
            try
            {
                var testType = await GetTestTypeByIdAsync(testTypeId);
                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return (new List<QuestionDto>(), TimeSpan.Zero);
                }

                // Parse time limit from string (e.g., "25 minutes" -> 25 minutes)
                TimeSpan timeLimit = ParseTimeLimit(testType.Stats.TimeLimit);

                // Get questions from the question service
                var questions = await _questionService.GetQuestionsByTestTypeIdAsync(testTypeId);

                return (questions.ToList(), timeLimit);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating questions for test: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<TestResultDto> SubmitTestAsync(int userId, SubmitAnswersDto submission)
        {
            try
            {
                var startTime = DateTime.UtcNow;
                _logger.LogInformation("Starting test submission for user {UserId}, test type {TestTypeId}",
                    userId, submission.TestTypeId);

                // Get test type
                var testType = await GetTestTypeByIdAsync(submission.TestTypeId);
                if (testType == null)
                {
                    throw new KeyNotFoundException($"Test type not found: {submission.TestTypeId}");
                }

                // Parse time limit
                TimeSpan timeLimit = ParseTimeLimit(testType.Stats.TimeLimit);

                // Get questions for this test
                var questions = await _questionService.GetQuestionsByTestTypeIdAsync(submission.TestTypeId);
                var questionsList = questions.ToList();

                // Get correct answers and weights
                var correctAnswers = await _questionService.GetCorrectAnswersAsync(submission.TestTypeId);
                var questionWeights = await _questionService.GetQuestionWeightsAsync(submission.TestTypeId);

                // Get time taken from submission (client should send this)
                // Default to time limit if not provided
                TimeSpan timeTaken = timeLimit;
                if (submission.TimeTaken.HasValue)
                {
                    timeTaken = TimeSpan.FromSeconds(submission.TimeTaken.Value);
                }

                // Validate answers
                var (score, accuracy, correctCount) = _answerValidator.ValidateAnswers(
                    submission.Answers,
                    questionsList,
                    correctAnswers,
                    questionWeights,
                    timeTaken,
                    timeLimit);

                // Create test result
                var testResult = new TestResult
                {
                    UserId = userId,
                    TestTypeId = GetDbTestTypeId(submission.TestTypeId),
                    Score = score,
                    Percentile = 0, // Will calculate later
                    Duration = FormatDuration(timeTaken),
                    QuestionsCompleted = submission.Answers.Count,
                    Accuracy = accuracy,
                    IQScore = null, // Will calculate for comprehensive test only
                    CompletedAt = DateTime.UtcNow
                };

                // Add to database without answers (they don't have foreign key references to questions table)
                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // Update leaderboard
                await UpdateLeaderboardAsync(userId, testResult);

                // Get the updated leaderboard entry to fetch the calculated percentile
                var updatedEntry = await _context.LeaderboardEntries
                    .FirstOrDefaultAsync(l => l.UserId == userId && l.TestTypeId == testResult.TestTypeId);

                // Return the result
                var resultDto = new TestResultDto
                {
                    Id = testResult.Id,
                    Score = testResult.Score,
                    Percentile = updatedEntry?.Percentile ?? 0,
                    TestTypeId = submission.TestTypeId,
                    TestTitle = testType.Title,
                    Duration = testResult.Duration,
                    QuestionsCompleted = testResult.QuestionsCompleted,
                    Accuracy = testResult.Accuracy,
                    CompletedAt = testResult.CompletedAt,
                    IQScore = updatedEntry?.IQScore
                };

                // Store test attempt in Redis
                var attemptKey = $"test_attempt:{userId}:{submission.TestTypeId}";
                await _redisService.SetAsync(attemptKey, DateTime.UtcNow, TimeSpan.FromHours(24));
                
                // Invalidate the cached test count for this test type so it gets refreshed
                var testCountCacheKey = $"test_completed_count:{submission.TestTypeId}";
                await _redisService.RemoveAsync(testCountCacheKey);
                
                _logger.LogInformation("Completed test submission for user {UserId} with score {Score}",
                    userId, score);

                return resultDto;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test for user {UserId}, test type {TestTypeId}",
                    userId, submission.TestTypeId);
                throw;
            }
        }

        private async Task UpdateLeaderboardAsync(int userId, TestResult testResult)
        {
            try
            {
                // Get existing leaderboard entry for this user and test type
                var entry = await _context.LeaderboardEntries
                    .FirstOrDefaultAsync(l => l.UserId == userId && l.TestTypeId == testResult.TestTypeId);

                // If no entry exists, create a new one
                if (entry == null)
                {
                    // Get user info for country
                    var user = await _context.Users.FindAsync(userId);
                    entry = new LeaderboardEntry
                    {
                        UserId = userId,
                        TestTypeId = testResult.TestTypeId,
                        Score = testResult.Score,
                        TestsCompleted = 1,
                        BestTime = testResult.Duration,
                        AverageTime = testResult.Duration,
                        IQScore = testResult.TestTypeId == 4 ? 
                            CalculateEnhancedIQScore(testResult.Score, 0, testResult.Duration ?? "0m 0s", testResult.Accuracy) : 
                            null,
                        Country = user?.Country ?? "United States",
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.LeaderboardEntries.Add(entry);
                }
                else
                {
                    // Update existing entry - use the highest score achieved
                    bool isHigherScore = testResult.Score > entry.Score;
                    entry.Score = Math.Max(entry.Score, testResult.Score);
                    entry.TestsCompleted++;
                    
                    // Update best time if this is the best score or first test
                    if (isHigherScore || string.IsNullOrEmpty(entry.BestTime))
                    {
                        entry.BestTime = testResult.Duration;
                    }
                    
                    // Calculate average time (simple approach - just use most recent for now)
                    entry.AverageTime = testResult.Duration;
                    
                    entry.LastUpdated = DateTime.UtcNow;
                    _context.LeaderboardEntries.Update(entry);
                }

                // Calculate ranks and percentiles for all users of this test type
                var allEntries = await _context.LeaderboardEntries
                    .Where(l => l.TestTypeId == testResult.TestTypeId)
                    .OrderByDescending(l => l.Score)
                    .ToListAsync();

                // Update ranks and percentiles
                for (int i = 0; i < allEntries.Count; i++)
                {
                    allEntries[i].Rank = i + 1;
                    allEntries[i].Percentile = 100f * (1f - (float)(i + 1) / allEntries.Count);
                    
                    // Calculate IQ score only for comprehensive test (testTypeId = 4)
                    if (allEntries[i].TestTypeId == 4)
                    {
                        // Get the test result to get time information
                        var currentResult = await _context.TestResults
                            .Where(tr => tr.UserId == allEntries[i].UserId && tr.TestTypeId == 4)
                            .OrderByDescending(tr => tr.Score)
                            .FirstOrDefaultAsync();
                            
                        if (currentResult != null)
                        {
                            allEntries[i].IQScore = CalculateEnhancedIQScore(
                                allEntries[i].Score, 
                                allEntries[i].Percentile,
                                currentResult.Duration ?? "0m 0s",
                                currentResult.Accuracy);
                        }
                        else
                        {
                            allEntries[i].IQScore = CalculateIQScore(allEntries[i].Percentile);
                        }
                    }
                }

                // Update test result percentile and IQ
                testResult.Percentile = entry.Percentile;
                if (testResult.TestTypeId == 4 && entry != null)
                {
                    // Calculate IQ score for the current test result
                    testResult.IQScore = CalculateEnhancedIQScore(
                        testResult.Score,
                        entry.Percentile,
                        testResult.Duration ?? "0m 0s",
                        testResult.Accuracy);
                    entry.IQScore = testResult.IQScore;
                }

                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating leaderboard for user {UserId}, test type {TestTypeId}",
                    userId, testResult.TestTypeId);
                // Don't throw - just log the error and continue
            }
        }

        private int GetDbTestTypeId(string testTypeId)
        {
            // This method maps the frontend test type ID (e.g., "number-logic") to the database ID
            switch (testTypeId)
            {
                case "number-logic": return 1;
                case "word-logic": return 2;
                case "memory": return 3;
                case "mixed": return 4;
                default: throw new KeyNotFoundException($"Unknown test type ID: {testTypeId}");
            }
        }

        private TimeSpan ParseTimeLimit(string timeLimitStr)
        {
            // Parse time limit string (e.g., "25 minutes" -> 25 minutes)
            try
            {
                var parts = timeLimitStr.Trim().Split(' ');
                if (parts.Length >= 2 && int.TryParse(parts[0], out int value))
                {
                    string unit = parts[1].ToLower();
                    if (unit.StartsWith("minute"))
                    {
                        return TimeSpan.FromMinutes(value);
                    }
                    else if (unit.StartsWith("hour"))
                    {
                        return TimeSpan.FromHours(value);
                    }
                    else if (unit.StartsWith("second"))
                    {
                        return TimeSpan.FromSeconds(value);
                    }
                }

                // Default to 30 minutes if parsing fails
                return TimeSpan.FromMinutes(30);
            }
            catch
            {
                return TimeSpan.FromMinutes(30); // Default fallback
            }
        }

        private string FormatDuration(TimeSpan duration)
        {
            // Format duration as a readable string (e.g., "25m 10s")
            if (duration.TotalHours >= 1)
            {
                return $"{duration.Hours}h {duration.Minutes}m {duration.Seconds}s";
            }
            else
            {
                return $"{duration.Minutes}m {duration.Seconds}s";
            }
        }
        
        private int CalculateIQScore(float percentile)
        {
            // Convert percentile to IQ score using normal distribution
            // IQ follows a normal distribution with mean 100 and standard deviation 15
            if (percentile <= 0) return 70;
            if (percentile >= 100) return 130;
            
            // Use approximation of inverse normal distribution function
            double z = InvNorm(percentile / 100.0);
            return (int)Math.Round(100 + z * 15);
        }
        
        private int CalculateEnhancedIQScore(int score, float percentile, string duration, float accuracy)
        {
            // Enhanced IQ calculation that considers multiple factors
            
            // 1. Base IQ from percentile (40% weight)
            int baseIQ = CalculateIQScore(percentile);
            
            // 2. Score component (30% weight)
            // Higher scores indicate better performance
            double scoreComponent = (score / 100.0) * 30; // Normalize to 0-30
            
            // 3. Accuracy component (20% weight)
            // Reward high accuracy
            double accuracyComponent = (accuracy / 100.0) * 20; // Normalize to 0-20
            
            // 4. Time efficiency component (10% weight)
            // Parse duration and calculate efficiency
            double timeComponent = 10; // Default full points
            if (TryParseDuration(duration, out TimeSpan timeTaken))
            {
                // Mixed test has 35 minutes time limit
                TimeSpan timeLimit = TimeSpan.FromMinutes(35);
                double timeRatio = timeTaken.TotalSeconds / timeLimit.TotalSeconds;
                
                if (timeRatio < 0.5)
                {
                    // Bonus for finishing in less than half the time
                    timeComponent = 15;
                }
                else if (timeRatio > 0.9)
                {
                    // Penalty for using almost all the time
                    timeComponent = 5;
                }
                else
                {
                    // Linear scale between 0.5 and 0.9
                    timeComponent = 15 - ((timeRatio - 0.5) / 0.4) * 10;
                }
            }
            
            // Calculate weighted IQ score
            double weightedIQ = (baseIQ * 0.4) + scoreComponent + accuracyComponent + timeComponent;
            
            // Apply adjustment factor to maintain normal distribution
            // Mean should be around 100, so we scale accordingly
            double adjustedIQ = 70 + (weightedIQ / 100) * 60; // Maps 0-100 weighted score to 70-130 IQ range
            
            // Ensure IQ is within reasonable bounds (70-160)
            return Math.Max(70, Math.Min(160, (int)Math.Round(adjustedIQ)));
        }
        
        private bool TryParseDuration(string duration, out TimeSpan result)
        {
            result = TimeSpan.Zero;
            if (string.IsNullOrEmpty(duration)) return false;
            
            // Parse format like "1m 35s" or "45m 10s"
            try
            {
                int totalSeconds = 0;
                var parts = duration.Split(' ');
                
                foreach (var part in parts)
                {
                    if (part.EndsWith("h"))
                    {
                        if (int.TryParse(part.TrimEnd('h'), out int hours))
                            totalSeconds += hours * 3600;
                    }
                    else if (part.EndsWith("m"))
                    {
                        if (int.TryParse(part.TrimEnd('m'), out int minutes))
                            totalSeconds += minutes * 60;
                    }
                    else if (part.EndsWith("s"))
                    {
                        if (int.TryParse(part.TrimEnd('s'), out int seconds))
                            totalSeconds += seconds;
                    }
                }
                
                result = TimeSpan.FromSeconds(totalSeconds);
                return totalSeconds > 0;
            }
            catch
            {
                return false;
            }
        }
        
        private double InvNorm(double p)
        {
            // Approximation of inverse normal distribution function
            // Using the algorithm by Peter John Acklam
            if (p <= 0) return double.NegativeInfinity;
            if (p >= 1) return double.PositiveInfinity;
            
            const double a1 = -3.969683028665376e+01;
            const double a2 = 2.209460984245205e+02;
            const double a3 = -2.759285104469687e+02;
            const double a4 = 1.383577518672690e+02;
            const double a5 = -3.066479806614716e+01;
            const double a6 = 2.506628277459239e+00;
            
            const double b1 = -5.447609879822406e+01;
            const double b2 = 1.615858368580409e+02;
            const double b3 = -1.556989798598866e+02;
            const double b4 = 6.680131188771972e+01;
            const double b5 = -1.328068155288572e+01;
            
            const double c1 = -7.784894002430293e-03;
            const double c2 = -3.223964580411365e-01;
            const double c3 = -2.400758277161838e+00;
            const double c4 = -2.549732539343734e+00;
            const double c5 = 4.374664141464968e+00;
            const double c6 = 2.938163982698783e+00;
            
            const double d1 = 7.784695709041462e-03;
            const double d2 = 3.224671290700398e-01;
            const double d3 = 2.445134137142996e+00;
            const double d4 = 3.754408661907416e+00;
            
            const double p_low = 0.02425;
            const double p_high = 1 - p_low;
            
            double x, q, r;
            
            if (p < p_low)
            {
                // Lower region
                q = Math.Sqrt(-2 * Math.Log(p));
                x = (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / 
                    ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            else if (p <= p_high)
            {
                // Central region
                q = p - 0.5;
                r = q * q;
                x = (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q / 
                    (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
            }
            else
            {
                // Upper region
                q = Math.Sqrt(-2 * Math.Log(1 - p));
                x = -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / 
                     ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            
            return x;
        }
        // Clear all test cooldowns for a specific user
        public async Task ClearUserTestCooldownsAsync(int userId)
        {
            try
            {
                var testTypes = await GetAllTestTypesAsync();
                
                foreach (var testType in testTypes)
                {
                    var cacheKey = $"test_attempt:{userId}:{testType.Id}";
                    await _redisService.RemoveAsync(cacheKey);
                }
                
                _logger.LogInformation($"Cleared all test cooldowns for user {userId}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error clearing test cooldowns for user {userId}");
                throw;
            }
        }

        // Clear all test cooldowns for all users (admin function)
        public async Task ClearAllTestCooldownsAsync()
        {
            try
            {
                // This is a pattern-based deletion - requires SCAN command
                await _redisService.DeleteKeysByPatternAsync("test_attempt:*");
                _logger.LogInformation("Cleared all test cooldowns for all users");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing all test cooldowns");
                throw;
            }
        }
    }
}