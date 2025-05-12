using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class BackgroundQuestionGenerationService : BackgroundService
    {
        private readonly ILogger<BackgroundQuestionGenerationService> _logger;
        private readonly IServiceProvider _serviceProvider;
        private readonly Random _random = new Random();
        private DateTime _lastGenerationDate = DateTime.MinValue;

        // Number of questions per test type
        private readonly Dictionary<string, int> _questionsPerTestType = new Dictionary<string, int>
        {
            { "number-logic", 20 },
            { "word-logic", 20 },
            { "memory", 15 },
            { "mixed", 16 }
        };

        // Question weights based on type
        private readonly Dictionary<string, float> _questionWeights = new Dictionary<string, float>
        {
            { "multiple-choice", 1.0f },
            { "fill-in-gap", 1.5f },
            { "memory-pair", 2.0f }
        };

        public BackgroundQuestionGenerationService(
            IServiceProvider serviceProvider,
            ILogger<BackgroundQuestionGenerationService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Background Question Generation Service is starting");

            // Check if we need to generate questions immediately on startup
            if (!await CheckIfTodaysQuestionsExistAsync())
            {
                _logger.LogInformation("No questions found for today - generating initial set of questions");
                await GenerateAndStoreAllQuestionsAsync();
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    // Check if we need to generate new questions for today
                    var currentDate = DateTime.UtcNow.Date;

                    if (currentDate > _lastGenerationDate && DateTime.UtcNow.Hour >= 2) // Generate after 2 AM UTC
                    {
                        _logger.LogInformation("Generating new set of questions for {Date}", currentDate);
                        await GenerateAndStoreAllQuestionsAsync();
                        _lastGenerationDate = currentDate;
                    }

                    // Check every hour
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
                catch (Exception ex) when (ex is not TaskCanceledException)
                {
                    _logger.LogError(ex, "Error in background question generation");
                    // Wait a bit before retrying
                    await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken);
                }
            }
        }

        private async Task<bool> CheckIfTodaysQuestionsExistAsync()
        {
            try
            {
                using var scope = _serviceProvider.CreateScope();
                var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();
                var today = DateTime.UtcNow.Date;

                // Check if any of the test types has questions for today
                foreach (var testTypeId in _questionsPerTestType.Keys)
                {
                    var questions = await redisService.GetQuestionSetAsync(testTypeId, today);
                    if (questions != null && questions.Count > 0)
                    {
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking if today's questions exist");
                return false;
            }
        }

        private async Task GenerateAndStoreAllQuestionsAsync()
        {
            // Generate questions for each test type with 1-minute delay between them
            await GenerateAndStoreQuestionsForTestType("number-logic");
            await Task.Delay(TimeSpan.FromMinutes(1));

            await GenerateAndStoreQuestionsForTestType("word-logic");
            await Task.Delay(TimeSpan.FromMinutes(1));

            await GenerateAndStoreQuestionsForTestType("memory");
            await Task.Delay(TimeSpan.FromMinutes(1));

            await GenerateAndStoreQuestionsForTestType("mixed");
        }

        private async Task GenerateAndStoreQuestionsForTestType(string testTypeId)
        {
            try
            {
                _logger.LogInformation("Generating questions for test type: {TestTypeId}", testTypeId);

                using var scope = _serviceProvider.CreateScope();
                var deepSeekService = scope.ServiceProvider.GetRequiredService<DeepSeekService>();
                var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();

                // Generate questions using DeepSeek API
                List<QuestionDto> questions;
                int questionCount = _questionsPerTestType[testTypeId];

                switch (testTypeId)
                {
                    case "number-logic":
                        questions = await deepSeekService.GenerateNumericalReasoningQuestionsAsync(questionCount);
                        break;
                    case "word-logic":
                        questions = await deepSeekService.GenerateVerbalIntelligenceQuestionsAsync(questionCount);
                        break;
                    case "memory":
                        questions = await deepSeekService.GenerateMemoryRecallQuestionsAsync(questionCount);
                        break;
                    case "mixed":
                        questions = await deepSeekService.GenerateComprehensiveIqQuestionsAsync(questionCount);
                        break;
                    default:
                        _logger.LogError("Unknown test type: {TestTypeId}", testTypeId);
                        return;
                }

                // Process generated questions and store in Redis
                if (questions != null && questions.Count > 0)
                {
                    var questionSetItems = new List<QuestionSetItem>();

                    foreach (var question in questions)
                    {
                        // Parse correct answer from DeepSeek response
                        // This assumes the DeepSeek service includes the correct answer in its response
                        var correctAnswer = ExtractCorrectAnswer(question);

                        // Assign weight based on question type
                        float weight = 1.0f;
                        if (_questionWeights.ContainsKey(question.Type))
                        {
                            weight = _questionWeights[question.Type];
                        }

                        questionSetItems.Add(new QuestionSetItem
                        {
                            Question = question,
                            CorrectAnswer = correctAnswer,
                            Weight = weight
                        });
                    }

                    // Store in Redis
                    bool success = await redisService.StoreQuestionSetAsync(
                        testTypeId,
                        DateTime.UtcNow.Date,
                        questionSetItems);

                    if (success)
                    {
                        _logger.LogInformation("Successfully stored {Count} questions for test type: {TestTypeId}",
                            questionSetItems.Count, testTypeId);
                    }
                    else
                    {
                        _logger.LogWarning("Failed to store questions for test type: {TestTypeId}", testTypeId);
                    }
                }
                else
                {
                    _logger.LogWarning("No questions were generated for test type: {TestTypeId}", testTypeId);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating questions for test type: {TestTypeId}", testTypeId);
            }
        }

        private string ExtractCorrectAnswer(QuestionDto question)
        {
            // This method would normally parse the correct answer from the DeepSeek response
            // For now, we'll use a placeholder implementation

            // In a real implementation, this information would be included with the question from DeepSeek
            // and we would extract it here

            // For fill-in-gap questions
            if (question.Type == "fill-in-gap")
            {
                if (question.Category == "numerical")
                {
                    // Example logic for numerical sequences
                    if (question.Text.Contains("1, 3, _, 7, 9"))
                    {
                        return "5";
                    }
                    else if (question.Text.Contains("3, 6, 12, 24, _"))
                    {
                        return "48";
                    }
                }
                else if (question.Category == "verbal")
                {
                    // Example for verbal analogies
                    if (question.Text.Contains("Book is to Reading as Fork is to"))
                    {
                        return "Eating";
                    }
                }
            }
            // For multiple-choice questions
            else if (question.Type == "multiple-choice" && question.Options?.Count > 0)
            {
                // In a real implementation, we'd have the correct answer index
                // For now, we'll select a random option as the "correct" answer
                int randomIndex = _random.Next(question.Options.Count);
                return question.Options[randomIndex];
            }
            // For memory-pair questions
            else if (question.Type == "memory-pair")
            {
                // Format: "pair-0-word-1:apple,pair-1-word-0:mountain,..."
                List<string> answerParts = new List<string>();

                for (int i = 0; i < question.Pairs.Count; i++)
                {
                    foreach (int missingIdx in question.MissingIndices[i])
                    {
                        answerParts.Add($"pair-{i}-word-{missingIdx}:{question.Pairs[i][missingIdx]}");
                    }
                }

                return string.Join(",", answerParts);
            }

            // Default case
            return "placeholder_answer";
        }
    }
}