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
    }
}