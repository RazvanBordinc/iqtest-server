using System;
using System.Collections.Generic;
using System.Linq;
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
                var openAIService = scope.ServiceProvider.GetRequiredService<OpenAIService>();
                var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();

                // Generate questions using OpenAI API
                List<QuestionWithAnswer> questionsWithAnswers;
                int questionCount = _questionsPerTestType[testTypeId];

                switch (testTypeId)
                {
                    case "number-logic":
                        questionsWithAnswers = await openAIService.GenerateNumericalReasoningQuestionsAsync(questionCount);
                        break;
                    case "word-logic":
                        questionsWithAnswers = await openAIService.GenerateVerbalIntelligenceQuestionsAsync(questionCount);
                        break;
                    case "memory":
                        questionsWithAnswers = await openAIService.GenerateMemoryRecallQuestionsAsync(questionCount);
                        break;
                    case "mixed":
                        questionsWithAnswers = await openAIService.GenerateComprehensiveIqQuestionsAsync(questionCount);
                        break;
                    default:
                        _logger.LogError("Unknown test type: {TestTypeId}", testTypeId);
                        return;
                }

                // Process generated questions and store in Redis
                if (questionsWithAnswers != null && questionsWithAnswers.Count > 0)
                {
                    var questionSetItems = new List<QuestionSetItem>();

                    foreach (var item in questionsWithAnswers)
                    {
                        // Assign weight based on question type
                        float weight = 1.0f;
                        if (_questionWeights.ContainsKey(item.Question.Type))
                        {
                            weight = _questionWeights[item.Question.Type];
                        }

                        questionSetItems.Add(new QuestionSetItem
                        {
                            Question = item.Question,
                            CorrectAnswer = item.CorrectAnswer,
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
    }
}