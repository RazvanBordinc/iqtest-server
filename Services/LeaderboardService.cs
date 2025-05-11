using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.DTOs.Leaderboard;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class LeaderboardService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<LeaderboardService> _logger;

        public LeaderboardService(ApplicationDbContext context, ILogger<LeaderboardService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<List<LeaderboardEntryDto>> GetGlobalLeaderboardAsync(int limit = 10)
        {
            try
            {
                // Get average scores across all test types
                var globalScores = await _context.LeaderboardEntries
                    .GroupBy(l => l.UserId)
                    .Select(g => new
                    {
                        UserId = g.Key,
                        AvgScore = g.Average(l => l.Score),
                        TotalTests = g.Sum(l => l.TestsCompleted),
                        Username = g.First().User.Username
                    })
                    .OrderByDescending(x => x.AvgScore)
                    .Take(limit)
                    .ToListAsync();

                // Calculate global percentile
                int totalUsers = await _context.Users.CountAsync();

                var result = new List<LeaderboardEntryDto>();
                for (int i = 0; i < globalScores.Count; i++)
                {
                    var score = globalScores[i];
                    result.Add(new LeaderboardEntryDto
                    {
                        Rank = i + 1,
                        Username = score.Username,
                        Score = (int)Math.Round(score.AvgScore),
                        TestsCompleted = score.TotalTests,
                        Percentile = 100f * (1f - (float)(i + 1) / totalUsers),
                        Country = "Romania" // Default country - in a real app, this would come from user profile
                    });
                }

                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving global leaderboard");
                throw;
            }
        }

        public async Task<List<LeaderboardEntryDto>> GetTestTypeLeaderboardAsync(string testTypeId, int limit = 10)
        {
            try
            {
                var testType = await _context.TestTypes
                    .FirstOrDefaultAsync(t => t.TypeId == testTypeId);

                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return new List<LeaderboardEntryDto>();
                }

                var entries = await _context.LeaderboardEntries
                    .Where(l => l.TestTypeId == testType.Id)
                    .OrderByDescending(l => l.Score)
                    .Take(limit)
                    .Select(l => new LeaderboardEntryDto
                    {
                        Rank = l.Rank,
                        Username = l.User.Username,
                        Score = l.Score,
                        TestsCompleted = l.TestsCompleted,
                        Percentile = l.Percentile,
                        Country = "Romania" // Default country
                    })
                    .ToListAsync();

                return entries;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving leaderboard for test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<UserRankingDto> GetUserRankingAsync(int userId)
        {
            try
            {
                var user = await _context.Users
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    return null;
                }

                // Get user's entries in all test types
                var entries = await _context.LeaderboardEntries
                    .Where(l => l.UserId == userId)
                    .Include(l => l.TestType)
                    .ToListAsync();

                // Calculate global rank (simplified)
                var globalRank = await CalculateGlobalRankAsync(userId);
                var globalPercentile = await CalculateGlobalPercentileAsync(userId);
                var iqScore = CalculateIQScore(globalPercentile);

                var testResults = new Dictionary<string, TestTypeRankingDto>();
                foreach (var entry in entries)
                {
                    testResults[entry.TestType.TypeId] = new TestTypeRankingDto
                    {
                        Rank = entry.Rank,
                        Score = entry.Score,
                        Percentile = entry.Percentile,
                        TotalTests = entry.TestsCompleted
                    };
                }

                return new UserRankingDto
                {
                    UserId = userId,
                    Username = user.Username,
                    GlobalRank = globalRank,
                    GlobalPercentile = globalPercentile,
                    IqScore = iqScore,
                    TestResults = testResults
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving ranking for user: {UserId}", userId);
                throw;
            }
        }

        private async Task<int> CalculateGlobalRankAsync(int userId)
        {
            // Calculate user's average score across all test types
            var userAvgScore = await _context.LeaderboardEntries
                .Where(l => l.UserId == userId)
                .AverageAsync(l => (double?)l.Score) ?? 0;

            // Count users with higher average scores
            var betterScoresCount = await _context.LeaderboardEntries
                .GroupBy(l => l.UserId)
                .Select(g => new { UserId = g.Key, AvgScore = g.Average(l => l.Score) })
                .CountAsync(x => x.AvgScore > userAvgScore);

            return betterScoresCount + 1; // +1 because rank is 1-based
        }

        private async Task<float> CalculateGlobalPercentileAsync(int userId)
        {
            var rank = await CalculateGlobalRankAsync(userId);
            var totalUsers = await _context.Users.CountAsync();

            // Calculate percentile (higher is better)
            return 100f * (1f - (float)rank / totalUsers);
        }

        private int CalculateIQScore(float percentile)
        {
            // Convert percentile to IQ score (simplified)
            // The standard IQ scale has a mean of 100 and standard deviation of 15
            // This is a simplified conversion - in a real app you'd use proper statistical methods
            return (int)Math.Round(100 + (percentile - 50) * 30 / 100);
        }
    }
}