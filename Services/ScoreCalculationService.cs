using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using IqTest_server.Models;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class ScoreCalculationService
    {
        private readonly ILogger<ScoreCalculationService> _logger;

        public ScoreCalculationService(ILogger<ScoreCalculationService> logger)
        {
            _logger = logger;
        }

        public async Task<(int Score, float Accuracy, Dictionary<int, bool> CorrectAnswers)> CalculateScoreAsync(
            List<AnswerDto> userAnswers,
            List<Question> questions)
        {
            var correctAnswers = new Dictionary<int, bool>();
            int correctCount = 0;

            foreach (var answer in userAnswers)
            {
                var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                if (question == null) continue;

                bool isCorrect = false;

                switch (question.Type)
                {
                    case "multiple-choice":
                        // For multiple choice, user answers with the index of the selected option
                        if (int.TryParse(answer.Value.ToString(), out int selectedIndex))
                        {
                            var options = JsonSerializer.Deserialize<List<string>>(question.Options);
                            if (options != null && selectedIndex >= 0 && selectedIndex < options.Count)
                            {
                                isCorrect = options[selectedIndex] == question.CorrectAnswer;
                            }
                        }
                        break;

                    case "fill-in-gap":
                        // For fill-in questions, directly compare the text
                        var userAnswer = answer.Value.ToString().Trim().ToLower();
                        isCorrect = userAnswer == question.CorrectAnswer.Trim().ToLower();
                        break;

                    case "memory-pair":
                        // For memory questions, this gets more complex
                        // We need to check if each remembered pair is correct
                        // For simplicity, we'll skip detailed implementation here
                        // In a real app, you'd check each memory pair individually
                        isCorrect = answer.Value.ToString().Trim().ToLower() == question.CorrectAnswer.Trim().ToLower();
                        break;

                    default:
                        _logger.LogWarning("Unknown question type: {QuestionType}", question.Type);
                        break;
                }

                correctAnswers[question.Id] = isCorrect;
                if (isCorrect) correctCount++;
            }

            // Calculate score (0-100)
            int totalScore = (int)Math.Round(100 * (double)correctCount / Math.Max(1, userAnswers.Count));

            // Calculate accuracy
            float accuracy = userAnswers.Count > 0 ? 100f * (float)correctCount / userAnswers.Count : 0;

            return (totalScore, accuracy, correctAnswers);
        }
    }
}