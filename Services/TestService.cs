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
                    CompletedAt = DateTime.UtcNow,
                    Answers = new List<Answer>()
                };

                // Add individual answers
                foreach (var answer in submission.Answers)
                {
                    var question = questionsList.FirstOrDefault(q => q.Id == answer.QuestionId);
                    if (question == null) continue;

                    // Determine if the answer is correct
                    bool isCorrect = false;
                    if (correctAnswers.TryGetValue(answer.QuestionId, out var correctAnswer))
                    {
                        // Simple check for demonstration - the full validation logic is in AnswerValidatorService
                        if (question.Type == "multiple-choice")
                        {
                            if (answer.Value is long longValue && longValue >= 0 && longValue < question.Options.Count)
                            {
                                isCorrect = question.Options[(int)longValue] == correctAnswer;
                            }
                        }
                        else
                        {
                            isCorrect = answer.Value?.ToString()?.Trim().ToLower() == correctAnswer.Trim().ToLower();
                        }
                    }

                    testResult.Answers.Add(new Answer
                    {
                        QuestionId = answer.QuestionId,
                        UserAnswer = JsonSerializer.Serialize(answer.Value),
                        Type = answer.Type,
                        IsCorrect = isCorrect
                    });
                }

                // Add to database
                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // Update leaderboard
                await UpdateLeaderboardAsync(userId, testResult);

                // Return the result
                var resultDto = new TestResultDto
                {
                    Id = testResult.Id,
                    Score = testResult.Score,
                    Percentile = testResult.Percentile,
                    TestTypeId = submission.TestTypeId,
                    TestTitle = testType.Title,
                    Duration = testResult.Duration,
                    QuestionsCompleted = testResult.QuestionsCompleted,
                    Accuracy = testResult.Accuracy,
                    CompletedAt = testResult.CompletedAt
                };

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
                    entry = new LeaderboardEntry
                    {
                        UserId = userId,
                        TestTypeId = testResult.TestTypeId,
                        Score = testResult.Score,
                        TestsCompleted = 1,
                        LastUpdated = DateTime.UtcNow
                    };
                    _context.LeaderboardEntries.Add(entry);
                }
                else
                {
                    // Update existing entry - use the highest score achieved
                    entry.Score = Math.Max(entry.Score, testResult.Score);
                    entry.TestsCompleted++;
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
                }

                // Update test result percentile
                testResult.Percentile = entry.Percentile;

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
    }
}