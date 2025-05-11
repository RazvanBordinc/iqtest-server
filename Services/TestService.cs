using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.DTOs.Test;
using IqTest_server.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class TestService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<TestService> _logger;
        private readonly ScoreCalculationService _scoreCalculationService;

        public TestService(
            ApplicationDbContext context,
            ILogger<TestService> logger,
            ScoreCalculationService scoreCalculationService)
        {
            _context = context;
            _logger = logger;
            _scoreCalculationService = scoreCalculationService;
        }

        public async Task<List<TestTypeDto>> GetAllTestTypesAsync()
        {
            try
            {
                var testTypes = await _context.TestTypes.ToListAsync();
                return testTypes.Select(t => new TestTypeDto
                {
                    Id = t.TypeId,
                    Title = t.Title,
                    Description = t.Description,
                    LongDescription = t.LongDescription,
                    Icon = t.Icon,
                    Color = t.Color,
                    Stats = new TestStatsDto
                    {
                        QuestionsCount = t.QuestionsCount,
                        TimeLimit = t.TimeLimit,
                        Difficulty = t.Difficulty
                    }
                }).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test types");
                throw;
            }
        }

        public async Task<TestTypeDto> GetTestTypeByIdAsync(string testTypeId)
        {
            try
            {
                var testType = await _context.TestTypes
                    .FirstOrDefaultAsync(t => t.TypeId == testTypeId);

                if (testType == null)
                {
                    return null;
                }

                return new TestTypeDto
                {
                    Id = testType.TypeId,
                    Title = testType.Title,
                    Description = testType.Description,
                    LongDescription = testType.LongDescription,
                    Icon = testType.Icon,
                    Color = testType.Color,
                    Stats = new TestStatsDto
                    {
                        QuestionsCount = testType.QuestionsCount,
                        TimeLimit = testType.TimeLimit,
                        Difficulty = testType.Difficulty
                    }
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<TestResultDto> SubmitTestAsync(int userId, SubmitAnswersDto submission)
        {
            try
            {
                var testType = await _context.TestTypes
                    .FirstOrDefaultAsync(t => t.TypeId == submission.TestTypeId);

                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", submission.TestTypeId);
                    throw new Exception("Invalid test type");
                }

                var questions = await _context.Questions
                    .Where(q => q.TestTypeId == testType.Id)
                    .ToListAsync();

                // Calculate score
                var (score, accuracy, correctAnswers) = await _scoreCalculationService.CalculateScoreAsync(
                    submission.Answers,
                    questions);

                // Create test result
                var testResult = new TestResult
                {
                    UserId = userId,
                    TestTypeId = testType.Id,
                    Score = score,
                    Accuracy = accuracy,
                    QuestionsCompleted = submission.Answers.Count,
                    CompletedAt = DateTime.UtcNow,
                    // Estimate duration (we don't have actual data for this)
                    Duration = "15:30" // Default placeholder
                };

                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // Save individual answers
                foreach (var answer in submission.Answers)
                {
                    var questionId = answer.QuestionId;
                    var question = questions.FirstOrDefault(q => q.Id == questionId);

                    if (question != null)
                    {
                        var answerValue = answer.Value.ToString();
                        var isCorrect = correctAnswers.ContainsKey(questionId) && correctAnswers[questionId];

                        _context.Answers.Add(new Answer
                        {
                            TestResultId = testResult.Id,
                            QuestionId = questionId,
                            UserAnswer = answerValue,
                            Type = answer.Type,
                            IsCorrect = isCorrect
                        });
                    }
                }

                await _context.SaveChangesAsync();

                // Update user's percentile and leaderboard entries
                await UpdateLeaderboardAsync(userId, testType.Id, score);

                return new TestResultDto
                {
                    Id = testResult.Id,
                    Score = score,
                    Percentile = testResult.Percentile,
                    TestTypeId = testType.TypeId,
                    TestTitle = testType.Title,
                    Duration = testResult.Duration,
                    QuestionsCompleted = testResult.QuestionsCompleted,
                    Accuracy = accuracy,
                    CompletedAt = testResult.CompletedAt
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test for user: {UserId}", userId);
                throw;
            }
        }

        private async Task UpdateLeaderboardAsync(int userId, int testTypeId, int score)
        {
            // Get all results for this test type
            var allResults = await _context.TestResults
                .Where(r => r.TestTypeId == testTypeId)
                .GroupBy(r => r.UserId)
                .Select(g => new
                {
                    UserId = g.Key,
                    MaxScore = g.Max(r => r.Score),
                    TestsCompleted = g.Count()
                })
                .OrderByDescending(r => r.MaxScore)
                .ToListAsync();

            // Calculate ranks and percentiles
            for (int i = 0; i < allResults.Count; i++)
            {
                var result = allResults[i];
                var rank = i + 1;
                var percentile = 100f * (1f - (float)rank / allResults.Count);

                // Update or create leaderboard entry
                var entry = await _context.LeaderboardEntries
                    .FirstOrDefaultAsync(l => l.UserId == result.UserId && l.TestTypeId == testTypeId);

                if (entry == null)
                {
                    entry = new LeaderboardEntry
                    {
                        UserId = result.UserId,
                        TestTypeId = testTypeId,
                        Score = result.MaxScore,
                        Rank = rank,
                        Percentile = percentile,
                        TestsCompleted = result.TestsCompleted,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.LeaderboardEntries.Add(entry);
                }
                else
                {
                    entry.Score = result.MaxScore;
                    entry.Rank = rank;
                    entry.Percentile = percentile;
                    entry.TestsCompleted = result.TestsCompleted;
                    entry.LastUpdated = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();

            // Also update global percentile for the user
            await UpdateGlobalPercentileAsync(userId);
        }

        private async Task UpdateGlobalPercentileAsync(int userId)
        {
            // This would calculate the user's global IQ score and percentile
            // Simplified for now - in a real app this would use more complex formula
            var userEntries = await _context.LeaderboardEntries
                .Where(l => l.UserId == userId)
                .ToListAsync();

            if (userEntries.Any())
            {
                // Update each entry's global rank
                foreach (var entry in userEntries)
                {
                    var globalRank = await _context.LeaderboardEntries
                        .Where(l => l.TestTypeId == entry.TestTypeId && l.Score >= entry.Score)
                        .CountAsync();

                    var totalUsers = await _context.LeaderboardEntries
                        .Where(l => l.TestTypeId == entry.TestTypeId)
                        .Select(l => l.UserId)
                        .Distinct()
                        .CountAsync();

                    entry.Rank = globalRank;
                    entry.Percentile = 100f * (1f - (float)globalRank / totalUsers);
                }

                await _context.SaveChangesAsync();
            }
        }
    }
}