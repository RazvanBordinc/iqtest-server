// Services/TestService.cs
using System;
using System.Collections.Generic;
using System.Linq;
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
        private readonly QuestionGeneratorService _questionGenerator;
        private readonly AnswerValidatorService _answerValidator;

        public TestService(
            ApplicationDbContext context,
            ILogger<TestService> logger,
            QuestionGeneratorService questionGenerator,
            AnswerValidatorService answerValidator)
        {
            _context = context;
            _logger = logger;
            _questionGenerator = questionGenerator;
            _answerValidator = answerValidator;
        }

        public async Task<List<TestTypeDto>> GetAllTestTypesAsync()
        {
            try
            {
                return TestTypeData.GetAllTestTypes();
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
                var testType = TestTypeData.GetTestTypeById(testTypeId);
                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                }
                return testType;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<(List<QuestionDto> Questions, Dictionary<int, string> CorrectAnswers)> GenerateQuestionsForTestAsync(string testTypeId)
        {
            try
            {
                var testType = TestTypeData.GetTestTypeById(testTypeId);
                if (testType == null)
                {
                    throw new ArgumentException($"Test type not found: {testTypeId}");
                }

                // Generate questions using mockup data (AI will replace this)
                var questions = await _questionGenerator.GenerateQuestionsAsync(testTypeId, testType.Stats.QuestionsCount);

                // Extract correct answers for validation
                var correctAnswers = ExtractCorrectAnswers(questions);

                return (questions, correctAnswers);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating questions for test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        public async Task<TestResultDto> SubmitTestAsync(int userId, SubmitAnswersDto submission)
        {
            try
            {
                var testType = TestTypeData.GetTestTypeById(submission.TestTypeId);
                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", submission.TestTypeId);
                    throw new Exception("Invalid test type");
                }

                // Generate questions again to validate answers (needed for mockup data)
                var (questions, correctAnswers) = await GenerateQuestionsForTestAsync(submission.TestTypeId);

                // Validate answers and calculate score
                var (score, accuracy, correctCount) = _answerValidator.ValidateAnswers(
                    submission.Answers, questions, correctAnswers);

                // Calculate percentile (simplified)
                var percentile = CalculatePercentile(score);

                // Create test result
                var testResult = new TestResult
                {
                    UserId = userId,
                    TestTypeId = GetDbTestTypeId(testType.Id),
                    Score = score,
                    Accuracy = accuracy,
                    Percentile = percentile,
                    QuestionsCompleted = submission.Answers.Count,
                    CompletedAt = DateTime.UtcNow,
                    Duration = "15:30" // Default placeholder
                };

                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // Save individual answers
                foreach (var answer in submission.Answers)
                {
                    var isCorrect = IsAnswerCorrect(answer, questions, correctAnswers);

                    _context.Answers.Add(new Answer
                    {
                        TestResultId = testResult.Id,
                        QuestionId = answer.QuestionId,
                        UserAnswer = answer.Value?.ToString() ?? "",
                        Type = answer.Type,
                        IsCorrect = isCorrect
                    });
                }

                await _context.SaveChangesAsync();

                // Update leaderboard
                await UpdateLeaderboardAsync(userId, GetDbTestTypeId(testType.Id), score);

                return new TestResultDto
                {
                    Id = testResult.Id,
                    Score = score,
                    Percentile = percentile,
                    TestTypeId = testType.Id,
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

        private Dictionary<int, string> ExtractCorrectAnswers(List<QuestionDto> questions)
        {
            var correctAnswers = new Dictionary<int, string>();

            foreach (var question in questions)
            {
                switch (question.Type)
                {
                    case "multiple-choice":
                        // For mockup, we'll need to determine which option is correct
                        correctAnswers[question.Id] = GetCorrectMultipleChoiceAnswer(question);
                        break;

                    case "fill-in-gap":
                        correctAnswers[question.Id] = GetCorrectFillInGapAnswer(question);
                        break;

                    case "memory-pair":
                        correctAnswers[question.Id] = GetCorrectMemoryAnswer(question);
                        break;
                }
            }

            return correctAnswers;
        }

        private string GetCorrectMultipleChoiceAnswer(QuestionDto question)
        {
            // For mockup data, return predefined answers based on question ID
            // This is temporary - when AI is integrated, the correct answer will come with the question
            switch (question.Id)
            {
                case 1: return "32";  // Number sequence
                case 3: return "8";   // Algebra
                case 5: return "2² × 3²";  // Prime factorization
                default: return question.Options?.FirstOrDefault() ?? "";
            }
        }

        private string GetCorrectFillInGapAnswer(QuestionDto question)
        {
            // For mockup data, return predefined answers based on question ID
            switch (question.Id)
            {
                case 2: return "5";      // Number sequence
                case 4: return "48";     // Pattern
                case 7: return "Eating"; // Analogy
                default: return "";
            }
        }

        private string GetCorrectMemoryAnswer(QuestionDto question)
        {
            // For memory questions, format the correct answers as a string
            var correctAnswers = new List<string>();

            for (int pairIndex = 0; pairIndex < question.Pairs.Count; pairIndex++)
            {
                var pair = question.Pairs[pairIndex];
                var missingIndices = question.MissingIndices[pairIndex];

                foreach (var wordIndex in missingIndices)
                {
                    var inputId = $"pair-{pairIndex}-word-{wordIndex}";
                    var correctWord = pair[wordIndex];
                    correctAnswers.Add($"{inputId}:{correctWord}");
                }
            }

            return string.Join(",", correctAnswers);
        }

        private bool IsAnswerCorrect(AnswerDto answer, List<QuestionDto> questions, Dictionary<int, string> correctAnswers)
        {
            var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
            if (question == null || !correctAnswers.TryGetValue(question.Id, out var correctAnswer))
            {
                return false;
            }

            return _answerValidator.ValidateAnswers(new List<AnswerDto> { answer }, questions, correctAnswers).CorrectCount > 0;
        }

        private float CalculatePercentile(int score)
        {
            // Simplified percentile calculation
            if (score >= 90) return 95f + (score - 90) * 0.5f;
            if (score >= 80) return 80f + (score - 80) * 1.5f;
            if (score >= 70) return 60f + (score - 70) * 2f;
            if (score >= 60) return 40f + (score - 60) * 2f;
            if (score >= 50) return 20f + (score - 50) * 2f;
            return score * 0.4f;
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
        }

        private int GetDbTestTypeId(string testTypeId)
        {
            // Map string testTypeId to database ID
            return testTypeId switch
            {
                "number-logic" => 1,
                "word-logic" => 2,
                "memory" => 3,
                "mixed" => 4,
                _ => throw new ArgumentException($"Invalid test type ID: {testTypeId}")
            };
        }
    }
}