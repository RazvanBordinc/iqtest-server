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
        private readonly ICacheService _cacheService;

        public LeaderboardService(ApplicationDbContext context, ILogger<LeaderboardService> logger, ICacheService cacheService)
        {
            _context = context;
            _logger = logger;
            _cacheService = cacheService;
        }

        public async Task<int> GetTestTypeCompletedCountAsync(string testTypeId)
        {
            var cacheKey = $"test_completed_count:{testTypeId}";
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                var testType = await _context.TestTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TypeId == testTypeId);
                
                if (testType == null) return 0;
                
                return await _context.TestResults
                    .Where(tr => tr.TestTypeId == testType.Id)
                    .CountAsync();
            }, CacheService.MediumCacheDuration);
        }
        
        public async Task<int> GetTotalUsersCountAsync()
        {
            var cacheKey = "total_users_count";
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                return await _context.Users.CountAsync();
            }, CacheService.MediumCacheDuration);
        }

        public async Task<List<LeaderboardEntryDto>> GetTestTypeLeaderboardAsync(string testTypeId, int page = 1, int pageSize = 10)
        {
            var cacheKey = CacheKeys.TestLeaderboard(_context.TestTypes.AsNoTracking().FirstOrDefault(t => t.TypeId == testTypeId)?.Id ?? 0, page, pageSize);
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                var testType = await _context.TestTypes
                    .AsNoTracking()
                    .FirstOrDefaultAsync(t => t.TypeId == testTypeId);

                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return new List<LeaderboardEntryDto>();
                }

                var skip = (page - 1) * pageSize;
                
                // Temporarily handle missing columns gracefully
                try
                {
                    var entries = await _context.LeaderboardEntries
                        .AsNoTracking()
                        .Include(l => l.User)
                        .Where(l => l.TestTypeId == testType.Id)
                        .OrderByDescending(l => l.Score)
                        .Skip(skip)
                        .Take(pageSize)
                        .Select(l => new LeaderboardEntryDto
                        {
                            UserId = l.UserId,
                            Rank = l.Rank,
                            Username = l.User.Username,
                            Score = l.Score,
                            TestsCompleted = l.TestsCompleted,
                            Percentile = l.Percentile,
                            Country = l.Country ?? l.User.Country ?? "Not specified",
                            AverageTime = l.AverageTime ?? "N/A",
                            BestTime = l.BestTime ?? "N/A",
                            IQScore = l.IQScore
                        })
                        .ToListAsync();
                    return entries;
                }
                catch (Exception ex)
                {
                    // If new columns don't exist, fall back to basic query
                    _logger.LogWarning(ex, "Error accessing new columns, falling back to basic query");
                    var entries = await _context.LeaderboardEntries
                        .Where(l => l.TestTypeId == testType.Id)
                        .OrderByDescending(l => l.Score)
                        .Skip(skip)
                        .Take(pageSize)
                        .Select(l => new LeaderboardEntryDto
                        {
                            UserId = l.UserId,
                            Rank = l.Rank,
                            Username = l.User.Username,
                            Score = l.Score,
                            TestsCompleted = l.TestsCompleted,
                            Percentile = l.Percentile,
                            Country = l.User.Country ?? "Not specified",
                            AverageTime = "N/A",
                            BestTime = "N/A",
                            IQScore = null
                        })
                        .ToListAsync();
                    return entries;
                }
            }, CacheService.MediumCacheDuration);
        }

        public async Task<UserRankingDto> GetUserRankingAsync(int userId)
        {
            var cacheKey = CacheKeys.UserRanking(userId);
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                var user = await _context.Users
                    .AsNoTracking()
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    return null;
                }

                // Get user's entries in all test types
                var entries = await _context.LeaderboardEntries
                    .AsNoTracking()
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
            }, CacheService.ShortCacheDuration);
        }

        private async Task<int> CalculateGlobalRankAsync(int userId)
        {
            // Use a more efficient query with proper indexing
            var userAvgScore = await _context.LeaderboardEntries
                .AsNoTracking()
                .Where(l => l.UserId == userId)
                .AverageAsync(l => (double?)l.Score) ?? 0;

            if (userAvgScore == 0) return int.MaxValue;

            // More efficient query using LINQ
            var betterScoresCount = await _context.LeaderboardEntries
                .AsNoTracking()
                .GroupBy(l => l.UserId)
                .Select(g => g.Average(l => l.Score))
                .CountAsync(avgScore => avgScore > userAvgScore);

            return betterScoresCount + 1;
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
            // Convert percentile to IQ score using normal distribution
            // IQ follows a normal distribution with mean 100 and standard deviation 15
            if (percentile <= 0) return 70;
            if (percentile >= 100) return 130;
            
            // Use a more accurate conversion based on z-scores
            double z = InvNorm(percentile / 100.0);
            return (int)Math.Round(100 + z * 15);
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
                q = Math.Sqrt(-2 * Math.Log(p));
                x = (((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            else if (p <= p_high)
            {
                q = p - 0.5;
                r = q * q;
                x = (((((a1 * r + a2) * r + a3) * r + a4) * r + a5) * r + a6) * q / (((((b1 * r + b2) * r + b3) * r + b4) * r + b5) * r + 1);
            }
            else
            {
                q = Math.Sqrt(-2 * Math.Log(1 - p));
                x = -(((((c1 * q + c2) * q + c3) * q + c4) * q + c5) * q + c6) / ((((d1 * q + d2) * q + d3) * q + d4) * q + 1);
            }
            
            return x;
        }
    }
}