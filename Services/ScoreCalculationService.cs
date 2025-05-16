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
        private readonly QuestionService _questionService;

        public ScoreCalculationService(
            ILogger<ScoreCalculationService> logger,
            QuestionService questionService)
        {
            _logger = logger;
            _questionService = questionService;
        }

        public async Task<(int Score, float Accuracy, Dictionary<int, bool> CorrectAnswers)> CalculateScoreAsync(
            string testTypeId,
            List<AnswerDto> userAnswers,
            List<Question> questions)
        {
            var correctAnswers = new Dictionary<int, bool>();
            int correctCount = 0;
            int totalWeightedScore = 0;
            int totalPossibleWeight = 0;

            // Get question weights from the service
            var questionWeights = await _questionService.GetQuestionWeightsAsync(testTypeId);

            foreach (var answer in userAnswers)
            {
                var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                if (question == null) continue;

                // Get weight for this question (default to 3 if not found)
                int weight = questionWeights.ContainsKey(question.Id) ? questionWeights[question.Id] : 3;
                totalPossibleWeight += weight;

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
                        // For memory questions, check if each remembered pair is correct
                        // For simplicity, we'll do a basic comparison
                        isCorrect = answer.Value.ToString().Trim().ToLower() == question.CorrectAnswer.Trim().ToLower();
                        break;

                    default:
                        _logger.LogWarning("Unknown question type: {QuestionType}", question.Type);
                        break;
                }

                correctAnswers[question.Id] = isCorrect;
                if (isCorrect)
                {
                    correctCount++;
                    totalWeightedScore += weight;
                }
            }

            // Calculate weighted score (0-100)
            int totalScore = totalPossibleWeight > 0
                ? (int)Math.Round(100 * (double)totalWeightedScore / totalPossibleWeight)
                : 0;

            // Calculate accuracy (percentage of correct answers)
            float accuracy = userAnswers.Count > 0 ? 100f * (float)correctCount / userAnswers.Count : 0;

            _logger.LogInformation("Score calculated: Weighted Score: {Score}, Accuracy: {Accuracy}%, Correct: {Correct}/{Total}",
                totalScore, accuracy, correctCount, userAnswers.Count);

            return (totalScore, accuracy, correctAnswers);
        }

        // Overload for backward compatibility
        public async Task<(int Score, float Accuracy, Dictionary<int, bool> CorrectAnswers)> CalculateScoreAsync(
            List<AnswerDto> userAnswers,
            List<Question> questions)
        {
            // If testTypeId is not provided, use a default without weights
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
                        var userAnswer = answer.Value.ToString().Trim().ToLower();
                        isCorrect = userAnswer == question.CorrectAnswer.Trim().ToLower();
                        break;

                    case "memory-pair":
                        isCorrect = answer.Value.ToString().Trim().ToLower() == question.CorrectAnswer.Trim().ToLower();
                        break;

                    default:
                        _logger.LogWarning("Unknown question type: {QuestionType}", question.Type);
                        break;
                }

                correctAnswers[question.Id] = isCorrect;
                if (isCorrect) correctCount++;
            }

            int totalScore = (int)Math.Round(100 * (double)correctCount / Math.Max(1, userAnswers.Count));
            float accuracy = userAnswers.Count > 0 ? 100f * (float)correctCount / userAnswers.Count : 0;

            return (totalScore, accuracy, correctAnswers);
        }
    }
}