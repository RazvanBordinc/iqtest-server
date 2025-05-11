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
    public class QuestionService
    {
        private readonly ApplicationDbContext _context;
        private readonly ILogger<QuestionService> _logger;

        public QuestionService(ApplicationDbContext context, ILogger<QuestionService> logger)
        {
            _context = context;
            _logger = logger;
        }

        public async Task<IEnumerable<QuestionDto>> GetQuestionsByTestTypeIdAsync(string testTypeId)
        {
            try
            {
                var testType = await _context.TestTypes
                    .FirstOrDefaultAsync(t => t.TypeId == testTypeId);

                if (testType == null)
                {
                    _logger.LogWarning("Test type not found: {TestTypeId}", testTypeId);
                    return new List<QuestionDto>();
                }

                var questions = await _context.Questions
                    .Where(q => q.TestTypeId == testType.Id)
                    .OrderBy(q => q.OrderIndex)
                    .ToListAsync();

                return questions.Select(q => MapToQuestionDto(q)).ToList();
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
                var question = await _context.Questions.FindAsync(questionId);
                if (question == null)
                {
                    _logger.LogWarning("Question not found: {QuestionId}", questionId);
                    return null;
                }

                return MapToQuestionDto(question);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving question: {QuestionId}", questionId);
                throw;
            }
        }

        private QuestionDto MapToQuestionDto(Question question)
        {
            var dto = new QuestionDto
            {
                Id = question.Id,
                Type = question.Type,
                Category = question.Category,
                Text = question.Text,
                MemorizationTime = question.MemorizationTime
            };

            // Deserialize options if present
            if (!string.IsNullOrEmpty(question.Options))
            {
                dto.Options = JsonSerializer.Deserialize<List<string>>(question.Options);
            }

            // Deserialize pairs and missing indices for memory questions
            if (question.Type == "memory-pair" && !string.IsNullOrEmpty(question.Pairs))
            {
                dto.Pairs = JsonSerializer.Deserialize<List<List<string>>>(question.Pairs);

                if (!string.IsNullOrEmpty(question.MissingIndices))
                {
                    dto.MissingIndices = JsonSerializer.Deserialize<List<List<int>>>(question.MissingIndices);
                }
            }

            return dto;
        }
    }
}