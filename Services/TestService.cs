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
        private readonly QuestionGeneratorService _questionGenerator;
        private readonly AnswerValidatorService _answerValidator;
        private readonly ILogger<TestService> _logger;

        public TestService(
            ApplicationDbContext context,
            QuestionGeneratorService questionGenerator,
            AnswerValidatorService answerValidator,
            ILogger<TestService> logger)
        {
            _context = context;
            _questionGenerator = questionGenerator;
            _answerValidator = answerValidator;
            _logger = logger;
        }

        public async Task<List<TestTypeDto>> GetAllTestTypesAsync()
        {
            // Return hardcoded test types from static data
            return TestTypeData.GetAllTestTypes();
        }

        public async Task<TestTypeDto> GetTestTypeByIdAsync(string testTypeId)
        {
            // Return hardcoded test type from static data
            return TestTypeData.GetTestTypeById(testTypeId);
        }

        public async Task<(TestTypeDto TestType, List<QuestionDto> Questions)> GenerateQuestionsForTestAsync(string testTypeId)
        {
            var testType = TestTypeData.GetTestTypeById(testTypeId);
            if (testType == null)
            {
                _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                return (null, new List<QuestionDto>());
            }

            try
            {
                // Get questions using the generator service
                _logger.LogInformation("Generating questions for test type: {TestTypeId}", testTypeId);
                var questions = await _questionGenerator.GenerateQuestionsAsync(testTypeId, testType.Stats.QuestionsCount);

                return (testType, questions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating questions for test type: {TestTypeId}", testTypeId);
                return (testType, new List<QuestionDto>());
            }
        }

        public async Task<TestResultDto> SubmitTestAsync(int userId, SubmitAnswersDto submission)
        {
            try
            {
                _logger.LogInformation("Processing test submission for user: {UserId}, test type: {TestTypeId}",
                    userId, submission.TestTypeId);

                // Get test type from ID
                var testType = await _context.TestTypes
                    .FirstOrDefaultAsync(t => t.TypeId == submission.TestTypeId);

                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", submission.TestTypeId);
                    throw new ArgumentException($"Test type not found: {submission.TestTypeId}");
                }

                // Get user
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    _logger.LogWarning("User not found: {UserId}", userId);
                    throw new ArgumentException($"User not found: {userId}");
                }

                // Generate questions for this test type (these will be used for validation)
                _logger.LogInformation("Generating questions for test type: {TestTypeId}", submission.TestTypeId);
                var result = await GenerateQuestionsForTestAsync(submission.TestTypeId);
                var questions = result.Questions;

                // Check if questions exist in the database and create a mapping of DTO IDs to DB IDs
                var questionIds = submission.Answers.Select(a => a.QuestionId).Distinct().ToList();
                _logger.LogInformation("Looking for questions with mock IDs: {Ids}", string.Join(", ", questionIds));

                // We'll create a mapping from the mockup question IDs to the database IDs
                var questionIdMapping = new Dictionary<int, int>();

                // Check which DTO questions are already stored in the database by content matching
                Dictionary<string, Question> existingQuestionsByText = new Dictionary<string, Question>();
                var existingQuestions = await _context.Questions
                    .Where(q => q.TestTypeId == testType.Id)
                    .ToListAsync();

                foreach (var q in existingQuestions)
                {
                    if (!existingQuestionsByText.ContainsKey(q.Text))
                    {
                        existingQuestionsByText[q.Text] = q;
                    }
                }

                // Create missing questions and build ID mapping
                var newQuestions = new List<Question>();
                foreach (var mockId in questionIds)
                {
                    var questionDto = questions.FirstOrDefault(q => q.Id == mockId);
                    if (questionDto == null)
                    {
                        _logger.LogWarning("Question with mock ID {Id} not found in generated questions", mockId);
                        continue;
                    }

                    // Check if this question already exists by text
                    if (existingQuestionsByText.TryGetValue(questionDto.Text, out var existingQuestion))
                    {
                        // Map the mockup ID to the existing database ID
                        questionIdMapping[mockId] = existingQuestion.Id;
                        _logger.LogInformation("Mapped mock ID {MockId} to existing question ID {DbId}",
                            mockId, existingQuestion.Id);
                    }
                    else
                    {
                        // This is a new question - create it but WITHOUT setting the ID
                        var newQuestion = new Question
                        {
                            // DO NOT set Id here - let SQL Server generate it
                            TestTypeId = testType.Id,
                            Type = questionDto.Type,
                            Text = questionDto.Text,
                            Category = questionDto.Category ?? "mixed",
                            Options = JsonSerializer.Serialize(questionDto.Options ?? new List<string>()),
                            CorrectAnswer = DetermineCorrectAnswer(questionDto),
                            MemorizationTime = questionDto.MemorizationTime,
                            Pairs = JsonSerializer.Serialize(questionDto.Pairs ?? new List<List<string>>()),
                            MissingIndices = JsonSerializer.Serialize(questionDto.MissingIndices ?? new List<List<int>>()),
                            OrderIndex = 0
                        };

                        newQuestions.Add(newQuestion);
                        _context.Questions.Add(newQuestion);
                    }
                }

                // Save the new questions to get their database-generated IDs
                if (newQuestions.Any())
                {
                    await _context.SaveChangesAsync();

                    // Now that DB has generated IDs, add them to the mapping
                    foreach (var mockId in questionIds)
                    {
                        // Skip if we already mapped this ID
                        if (questionIdMapping.ContainsKey(mockId))
                            continue;

                        var questionDto = questions.FirstOrDefault(q => q.Id == mockId);
                        if (questionDto == null)
                            continue;

                        // Find the newly created question by text
                        var newQuestion = newQuestions.FirstOrDefault(q => q.Text == questionDto.Text);
                        if (newQuestion != null)
                        {
                            questionIdMapping[mockId] = newQuestion.Id;
                            _logger.LogInformation("Mapped mock ID {MockId} to new question ID {DbId}",
                                mockId, newQuestion.Id);
                        }
                    }
                }

                // Process answers using the ID mapping
                _logger.LogInformation("Processing {Count} answers", submission.Answers.Count);

                // Create test result
                var testResult = new TestResult
                {
                    UserId = userId,
                    TestTypeId = testType.Id,
                    Score = 85, // Hardcoded score for now - would be calculated from answers
                    Percentile = 92.5f, // Example percentile
                    Duration = "10:00", // This should be passed from client in real implementation
                    QuestionsCompleted = submission.Answers.Count,
                    Accuracy = 85.0f, // Example accuracy
                    CompletedAt = DateTime.UtcNow
                };

                _context.TestResults.Add(testResult);
                await _context.SaveChangesAsync();

                // Save answers using mapped question IDs
                var answers = new List<Answer>();
                foreach (var answerDto in submission.Answers)
                {
                    // Map the DTO question ID to the database ID
                    if (!questionIdMapping.TryGetValue(answerDto.QuestionId, out var dbQuestionId))
                    {
                        _logger.LogWarning("No mapping found for question ID: {QuestionId}", answerDto.QuestionId);
                        continue;
                    }

                    string userAnswerValue;
                    if (answerDto.Type == "memory-pair" && answerDto.Value is string valueString)
                    {
                        // Handle nested JSON for memory-pair type
                        try
                        {
                            JsonDocument jsonDoc = JsonDocument.Parse(valueString);
                            var element = jsonDoc.RootElement;

                            if (element.TryGetProperty("value", out var innerValue))
                            {
                                userAnswerValue = innerValue.GetRawText();
                            }
                            else
                            {
                                userAnswerValue = valueString;
                            }
                        }
                        catch (JsonException ex)
                        {
                            _logger.LogError(ex, "Error parsing memory answer JSON");
                            userAnswerValue = valueString;
                        }
                    }
                    else if (answerDto.Value is JsonElement element)
                    {
                        userAnswerValue = element.GetRawText();
                    }
                    else
                    {
                        userAnswerValue = answerDto.Value?.ToString() ?? "";
                    }

                    var answerEntity = new Answer
                    {
                        TestResultId = testResult.Id,
                        QuestionId = dbQuestionId, // Use the mapped ID
                        UserAnswer = userAnswerValue,
                        Type = answerDto.Type,
                        IsCorrect = true // Simplified - we'd calculate this in a real app
                    };

                    answers.Add(answerEntity);
                }

                _context.Answers.AddRange(answers);
                await _context.SaveChangesAsync();

                // Update leaderboard
                await UpdateLeaderboardAsync(userId, testType.Id, testResult.Score, testResult.Percentile);

                // Return result
                return new TestResultDto
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
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test for user: {UserId}", userId);
                throw;
            }
        }

        private string DetermineCorrectAnswer(QuestionDto questionDto)
        {
            // In a real app, you would get this from an AI API
            // For this mockup implementation, use hardcoded answers based on question text
            switch (questionDto.Type)
            {
                case "multiple-choice":
                    if (questionDto.Text.Contains("next in the sequence: 2, 4, 8, 16"))
                        return "32";
                    if (questionDto.Text.Contains("next in the sequence: 1, 4, 9, 16"))
                        return "25";
                    if (questionDto.Text.Contains("8 + 2x = 24"))
                        return "8";
                    if (questionDto.Text.Contains("prime factorization of 36"))
                        return "2² × 3²";
                    if (questionDto.Text.Contains("ephemeral"))
                        return "Temporary";
                    if (questionDto.Text.Contains("antonym of 'benevolent'"))
                        return "Malicious";
                    if (questionDto.Text.Contains("correct sentence"))
                        return "They're going to the store later.";

                    // Default case - first option
                    return questionDto.Options?.FirstOrDefault() ?? "";

                case "fill-in-gap":
                    if (questionDto.Text.Contains("1, 3, _, 7, 9"))
                        return "5";
                    if (questionDto.Text.Contains("3, 6, 12, 24, _"))
                        return "48";
                    if (questionDto.Text.Contains("Book is to Reading as Fork is to"))
                        return "Eating";
                    if (questionDto.Text.Contains("Solve for x: 3x + 7 = 22"))
                        return "5";
                    if (questionDto.Text.Contains("Psy_____ogy"))
                        return "chol";

                    return "answer"; // Default

                case "memory-pair":
                    // For memory pairs, we need to build a mapping
                    var mappings = new List<string>();

                    if (questionDto.Pairs != null && questionDto.MissingIndices != null)
                    {
                        for (int pairIndex = 0; pairIndex < questionDto.Pairs.Count; pairIndex++)
                        {
                            var pair = questionDto.Pairs[pairIndex];
                            var missingIndices = questionDto.MissingIndices[pairIndex];

                            foreach (var wordIndex in missingIndices)
                            {
                                if (wordIndex < pair.Count)
                                {
                                    mappings.Add($"pair-{pairIndex}-word-{wordIndex}:{pair[wordIndex]}");
                                }
                            }
                        }
                    }

                    return string.Join(",", mappings);

                default:
                    return "";
            }
        }

        private async Task UpdateLeaderboardAsync(int userId, int testTypeId, int score, float percentile)
        {
            // Find existing leaderboard entry
            var entry = await _context.LeaderboardEntries
                .FirstOrDefaultAsync(l => l.UserId == userId && l.TestTypeId == testTypeId);

            if (entry == null)
            {
                // Create new entry
                entry = new LeaderboardEntry
                {
                    UserId = userId,
                    TestTypeId = testTypeId,
                    Score = score,
                    Percentile = percentile,
                    TestsCompleted = 1,
                    Rank = 0, // Will be calculated later
                    LastUpdated = DateTime.UtcNow
                };

                _context.LeaderboardEntries.Add(entry);
            }
            else
            {
                // Update existing entry
                entry.TestsCompleted++;

                // Only update score if better than previous
                if (score > entry.Score)
                {
                    entry.Score = score;
                    entry.Percentile = percentile;
                }

                entry.LastUpdated = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            // Update ranks (simplified)
            await UpdateLeaderboardRanksAsync(testTypeId);
        }

        private async Task UpdateLeaderboardRanksAsync(int testTypeId)
        {
            // Get all entries for this test type
            var entries = await _context.LeaderboardEntries
                .Where(l => l.TestTypeId == testTypeId)
                .OrderByDescending(l => l.Score)
                .ToListAsync();

            // Update ranks
            for (int i = 0; i < entries.Count; i++)
            {
                entries[i].Rank = i + 1;
            }

            await _context.SaveChangesAsync();
        }

        // When implementing the AI version, add a method like this:
        // private async Task<List<QuestionDto>> GenerateQuestionsWithAIAsync(string testTypeId, int count)
        // {
        //     // Call external AI API to generate questions
        //     // This will replace the mockup data
        // }

        // Additional method for future AI-based correct answer determination
        // private async Task<string> GetCorrectAnswerFromAIAsync(QuestionDto question)
        // {
        //     // Call AI API to get the correct answer
        // }
    }
}