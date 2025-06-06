using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class QuestionService
    {
        private readonly ILogger<QuestionService> _logger;
        private readonly QuestionGeneratorService _questionGenerator;
        private readonly GithubService _githubService;
        private readonly ICacheService _cacheService;

        public QuestionService(
            ILogger<QuestionService> logger,
            QuestionGeneratorService questionGenerator,
            GithubService githubService,
            ICacheService cacheService)
        {
            _logger = logger;
            _questionGenerator = questionGenerator;
            _githubService = githubService;
            _cacheService = cacheService;
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByTestTypeIdAsync(string testTypeId, bool forceRefresh = false)
        {
            var cacheKey = CacheKeys.Questions(GetDbTestTypeId(testTypeId));
            
            if (forceRefresh)
            {
                _cacheService.Remove(cacheKey);
            }
            else
            {
            }
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                // Get the correct question count for each test type
                int questionCount = GetQuestionCount(testTypeId);
                
                // Fetch questions directly from GitHub (force refresh if requested)
                var questionItems = await _githubService.GetQuestionsAsync(testTypeId, questionCount, forceRefresh);

                if (questionItems != null && questionItems.Count > 0)
                {

                    // Extract just the questions without the correct answers
                    var questions = new List<QuestionDto>();
                    foreach (var item in questionItems)
                    {
                        // Add the weight to the question DTO
                        item.Question.Weight = item.Weight;
                        questions.Add(item.Question);
                    }


                    return questions;
                }

                // Fall back to generated questions if GitHub fetch fails

                // Get test type to determine number of questions
                var testType = TestTypeData.GetTestTypeById(testTypeId);
                if (testType == null)
                {
                    _logger.LogError("Test type not found: {TestTypeId}", testTypeId);
                    return new List<QuestionDto>();
                }

                // Generate questions for this test type
                var generatedQuestions = await _questionGenerator.GenerateQuestionsAsync(testTypeId, testType.Stats.QuestionsCount);

                _logger.LogInformation("Generated {Count} questions for test type: {TestTypeId}", generatedQuestions.Count, testTypeId);

                return generatedQuestions;
            }, CacheService.LongCacheDuration);
        }

        public async Task<QuestionDto> GetQuestionByIdAsync(int questionId)
        {
            try
            {
                // Implementation would depend on how you track question IDs
                // For now, we'll return null as the original method did
                _logger.LogWarning("GetQuestionById not implemented for ID: {QuestionId}", questionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question: {QuestionId}", questionId);
                throw;
            }
        }

        // Get the correct answers for a set of questions (for internal use only)
        public async Task<Dictionary<int, string>> GetCorrectAnswersAsync(string testTypeId)
        {
            var cacheKey = $"answers:{testTypeId}";
            
            return await _cacheService.GetOrCreateAsync(cacheKey, async () =>
            {
                int questionCount = GetQuestionCount(testTypeId);
                var questionItems = await _githubService.GetQuestionsAsync(testTypeId, questionCount);

                if (questionItems == null || questionItems.Count == 0)
                {
                    _logger.LogWarning("No questions found for test type: {TestTypeId}", testTypeId);
                    return new Dictionary<int, string>();
                }

                var correctAnswers = new Dictionary<int, string>();
                foreach (var item in questionItems)
                {
                    correctAnswers[item.Question.Id] = item.CorrectAnswer;
                }

                return correctAnswers;
            }, CacheService.LongCacheDuration);
        }

        // Get question weights (for scoring)
        public async Task<Dictionary<int, int>> GetQuestionWeightsAsync(string testTypeId)
        {
            try
            {
                int questionCount = GetQuestionCount(testTypeId);
                var questionItems = await _githubService.GetQuestionsAsync(testTypeId, questionCount);

                if (questionItems == null || questionItems.Count == 0)
                {
                    _logger.LogWarning("No questions found for test type: {TestTypeId}", testTypeId);
                    return new Dictionary<int, int>();
                }

                var weights = new Dictionary<int, int>();
                foreach (var item in questionItems)
                {
                    weights[item.Question.Id] = item.Weight;
                }

                return weights;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question weights for test type: {TestTypeId}", testTypeId);
                return new Dictionary<int, int>();
            }
        }

        private int GetQuestionCount(string testTypeId)
        {
            return testTypeId switch
            {
                "number-logic" => 24,
                "word-logic" => 28,
                "memory" => 20,
                "mixed" => 40,
                _ => 20
            };
        }
        
        private int GetDbTestTypeId(string testTypeId)
        {
            return testTypeId switch
            {
                "number-logic" => 1,
                "word-logic" => 2,
                "memory" => 3,
                "mixed" => 4,
                _ => throw new ArgumentException($"Unknown test type: {testTypeId}")
            };
        }
    }
}