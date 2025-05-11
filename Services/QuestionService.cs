// Services/QuestionService.cs
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

        public QuestionService(ILogger<QuestionService> logger, QuestionGeneratorService questionGenerator)
        {
            _logger = logger;
            _questionGenerator = questionGenerator;
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByTestTypeIdAsync(string testTypeId)
        {
            try
            {
                // Get test type to determine number of questions
                var testType = TestTypeData.GetTestTypeById(testTypeId);
                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return new List<QuestionDto>();
                }

                // Generate questions for this test type
                var questions = await _questionGenerator.GenerateQuestionsAsync(testTypeId, testType.Stats.QuestionsCount);

                _logger.LogInformation("Generated {Count} questions for test type: {TestTypeId}", questions.Count, testTypeId);

                return questions;
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
                // For the mockup implementation, we don't store questions by ID
                // This method would typically be used for future implementations
                _logger.LogWarning("GetQuestionById not implemented for mockup data. QuestionId: {QuestionId}", questionId);
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question: {QuestionId}", questionId);
                throw;
            }
        }
    }
}