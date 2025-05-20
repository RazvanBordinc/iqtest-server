using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IqTest_server.Data;
using IqTest_server.DTOs.Profile;

namespace IqTest_server.Services
{
    public class ProfileService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<ProfileService> _logger;

        public ProfileService(
            ApplicationDbContext context,
            ILogger<ProfileService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<UserProfileDto> GetUserProfileAsync(int userId)
        {
            try
            {
                var user = await _context.Users
                    .Include(u => u.TestResults)
                        .ThenInclude(tr => tr.TestType)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null) return null;

                // Get test statistics
                var testStats = user.TestResults
                    .GroupBy(tr => tr.TestType)
                    .Select(g => new TestStatDto
                    {
                        TestTypeId = g.Key.TypeId,
                        TestTypeName = g.Key.Title,
                        TestsCompleted = g.Count(),
                        BestScore = g.Max(tr => tr.Score),
                        AverageScore = (int)Math.Round(g.Average(tr => tr.Score)),
                        LastAttempt = g.Max(tr => tr.CompletedAt),
                        BestTime = g.OrderByDescending(tr => tr.Score).ThenBy(tr => tr.Duration).FirstOrDefault()?.Duration ?? "N/A",
                        IQScore = g.Key.TypeId == "mixed" ? g.OrderByDescending(tr => tr.Score).FirstOrDefault()?.IQScore : null
                    })
                    .ToList();

                // Get recent results
                var recentResults = user.TestResults
                    .OrderByDescending(tr => tr.CompletedAt)
                    .Take(10)
                    .Select(tr => new RecentTestResultDto
                    {
                        TestTypeId = tr.TestType.TypeId,
                        TestTypeName = tr.TestType.Title,
                        Score = tr.Score,
                        Percentile = tr.Percentile,
                        Duration = tr.Duration ?? "N/A",
                        CompletedAt = tr.CompletedAt,
                        IQScore = tr.TestType.TypeId == "mixed" ? tr.IQScore : null
                    })
                    .ToList();

                return new UserProfileDto
                {
                    UserId = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Age = user.Age ?? 0,
                    Country = user.Country ?? "Not specified",
                    CreatedAt = user.CreatedAt,
                    TotalTestsCompleted = user.TestResults.Count,
                    TestStats = testStats,
                    RecentResults = recentResults
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving profile for user: {UserId}", userId);
                throw;
            }
        }

        public async Task<bool> UpdateUserCountryAsync(int userId, string country)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return false;

                user.Country = country;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating country for user: {UserId}", userId);
                return false;
            }
        }

        public async Task<bool> UpdateUserAgeAsync(int userId, int age)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null) return false;

                user.Age = age;
                await _context.SaveChangesAsync();
                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating age for user: {UserId}", userId);
                return false;
            }
        }

        public async Task<object> GetUserTestHistoryAsync(int userId, int page = 1, int limit = 5, string testType = null)
        {
            try
            {
                // Ensure valid pagination values
                page = Math.Max(1, page);
                limit = Math.Clamp(limit, 1, 20); // Set reasonable limits
                
                // Base query
                var query = _context.TestResults
                    .Where(tr => tr.UserId == userId);
                
                // Apply test type filter if specified
                if (!string.IsNullOrEmpty(testType))
                {
                    int? dbTestTypeId = GetDbTestTypeId(testType);
                    if (dbTestTypeId.HasValue)
                    {
                        query = query.Where(tr => tr.TestTypeId == dbTestTypeId.Value);
                    }
                }
                
                // Get total count for pagination metadata
                var totalCount = await query.CountAsync();
                
                // If no results on current page but there are total results, reset to page 1
                if (totalCount > 0 && page > 1 && (page - 1) * limit >= totalCount) {
                    page = 1;
                }
                
                // Get paginated results
                var testResults = await query
                    .Include(tr => tr.TestType)
                    .OrderByDescending(tr => tr.CompletedAt)
                    .Skip((page - 1) * limit)
                    .Take(limit)
                    .ToListAsync();

                var testHistory = new List<TestHistoryDto>();

                foreach (var result in testResults)
                {
                    // Calculate better than percentage based on the test
                    var totalTestTakers = await _context.TestResults
                        .Where(tr => tr.TestTypeId == result.TestTypeId)
                        .CountAsync();
                    
                    var betterThanCount = await _context.TestResults
                        .Where(tr => tr.TestTypeId == result.TestTypeId && tr.Score < result.Score)
                        .CountAsync();
                    
                    var betterThanPercentage = totalTestTakers > 0 
                        ? (float)betterThanCount / totalTestTakers * 100 
                        : 0;

                    // Calculate time ago
                    var timeAgo = CalculateTimeAgo(result.CompletedAt);

                    testHistory.Add(new TestHistoryDto
                    {
                        Id = result.Id,
                        TestTypeId = GetFrontendTestTypeId(result.TestTypeId),
                        TestTitle = result.TestType.Title,
                        Score = result.Score,
                        Percentile = result.Percentile,
                        BetterThanPercentage = $"{betterThanPercentage:F1}%",
                        IQScore = result.IQScore,
                        Accuracy = result.Accuracy,
                        Duration = result.Duration ?? "N/A",
                        CompletedAt = result.CompletedAt,
                        QuestionsCompleted = result.QuestionsCompleted,
                        TimeAgo = timeAgo
                    });
                }

                // Return pagination metadata along with the results
                return new 
                {
                    Results = testHistory,
                    Pagination = new 
                    {
                        TotalItems = totalCount,
                        Page = page,
                        TotalPages = (int)Math.Ceiling(totalCount / (double)limit),
                        Limit = limit
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test history for user: {UserId}", userId);
                throw;
            }
        }

        private string GetFrontendTestTypeId(int dbTestTypeId)
        {
            return dbTestTypeId switch
            {
                1 => "number-logic",
                2 => "word-logic",
                3 => "memory",
                4 => "mixed",
                _ => "unknown"
            };
        }
        
        private int? GetDbTestTypeId(string frontendTestTypeId)
        {
            return frontendTestTypeId switch
            {
                "number-logic" => 1,
                "word-logic" => 2,
                "memory" => 3,
                "mixed" => 4,
                _ => null
            };
        }

        private string CalculateTimeAgo(DateTime dateTime)
        {
            var timeSpan = DateTime.UtcNow - dateTime;

            if (timeSpan.TotalMinutes < 1)
                return "just now";
            if (timeSpan.TotalMinutes < 60)
                return $"{(int)timeSpan.TotalMinutes} minute{((int)timeSpan.TotalMinutes == 1 ? "" : "s")} ago";
            if (timeSpan.TotalHours < 24)
                return $"{(int)timeSpan.TotalHours} hour{((int)timeSpan.TotalHours == 1 ? "" : "s")} ago";
            if (timeSpan.TotalDays < 30)
                return $"{(int)timeSpan.TotalDays} day{((int)timeSpan.TotalDays == 1 ? "" : "s")} ago";
            if (timeSpan.TotalDays < 365)
                return $"{(int)(timeSpan.TotalDays / 30)} month{((int)(timeSpan.TotalDays / 30) == 1 ? "" : "s")} ago";
            
            return $"{(int)(timeSpan.TotalDays / 365)} year{((int)(timeSpan.TotalDays / 365) == 1 ? "" : "s")} ago";
        }
    }
}