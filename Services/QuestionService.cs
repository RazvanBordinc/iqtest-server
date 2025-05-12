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
        private readonly RedisService _redisService;

        public QuestionService(
            ILogger<QuestionService> logger,
            QuestionGeneratorService questionGenerator,
            RedisService redisService)
        {
            _logger = logger;
            _questionGenerator = questionGenerator;
            _redisService = redisService;
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByTestTypeIdAsync(string testTypeId)
        {
            try
            {
                // Try to get questions from Redis first
                var questionSetItems = await _redisService.GetLatestQuestionSetAsync(testTypeId);

                // If questions are found in Redis, return them
                if (questionSetItems != null && questionSetItems.Count > 0)
                {
                    _logger.LogInformation("Retrieved {Count} questions from Redis for test type: {TestTypeId}",
                        questionSetItems.Count, testTypeId);

                    // Extract just the questions without the correct answers
                    var questions = new List<QuestionDto>();
                    foreach (var item in questionSetItems)
                    {
                        questions.Add(item.Question);
                    }

                    return questions;
                }

                // Fall back to generated questions if none are found in Redis
                _logger.LogWarning("No questions found in Redis for test type: {TestTypeId}, falling back to generated questions", testTypeId);

                // Get test type to determine number of questions
                var testType = TestTypeData.GetTestTypeById(testTypeId);
                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return new List<QuestionDto>();
                }

                // Generate questions for this test type
                var generatedQuestions = await _questionGenerator.GenerateQuestionsAsync(testTypeId, testType.Stats.QuestionsCount);

                _logger.LogInformation("Generated {Count} questions for test type: {TestTypeId}", generatedQuestions.Count, testTypeId);

                return generatedQuestions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving questions for test type: {TestTypeId}", testTypeId);
                throw;
            }
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
            try
            {
                var questionSetItems = await _redisService.GetLatestQuestionSetAsync(testTypeId);

                if (questionSetItems == null || questionSetItems.Count == 0)
                {
                    _logger.LogWarning("No questions found in Redis for test type: {TestTypeId}", testTypeId);
                    return new Dictionary<int, string>();
                }

                var correctAnswers = new Dictionary<int, string>();
                foreach (var item in questionSetItems)
                {
                    correctAnswers[item.Question.Id] = item.CorrectAnswer;
                }

                return correctAnswers;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving correct answers for test type: {TestTypeId}", testTypeId);
                return new Dictionary<int, string>();
            }
        }

        // Get question weights (for scoring)
        public async Task<Dictionary<int, float>> GetQuestionWeightsAsync(string testTypeId)
        {
            try
            {
                var questionSetItems = await _redisService.GetLatestQuestionSetAsync(testTypeId);

                if (questionSetItems == null || questionSetItems.Count == 0)
                {
                    _logger.LogWarning("No questions found in Redis for test type: {TestTypeId}", testTypeId);
                    return new Dictionary<int, float>();
                }

                var weights = new Dictionary<int, float>();
                foreach (var item in questionSetItems)
                {
                    weights[item.Question.Id] = item.Weight;
                }

                return weights;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question weights for test type: {TestTypeId}", testTypeId);
                return new Dictionary<int, float>();
            }
        }
    }
}