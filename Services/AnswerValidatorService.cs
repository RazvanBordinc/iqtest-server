using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class AnswerValidatorService
    {
        private readonly ILogger<AnswerValidatorService> _logger;

        public AnswerValidatorService(ILogger<AnswerValidatorService> logger)
        {
            _logger = logger;
        }

        // Validate answers and calculate score, taking into account time and question weights
        public (int Score, float Accuracy, int CorrectCount) ValidateAnswers(
            List<AnswerDto> userAnswers,
            List<QuestionDto> questions,
            Dictionary<int, string> correctAnswers,
            Dictionary<int, float> questionWeights,
            TimeSpan timeTaken,
            TimeSpan timeLimit)
        {
            int weightedCorrectPoints = 0;
            int totalWeightedPoints = 0;
            int correctCount = 0;
            int totalQuestions = userAnswers.Count;

            // Calculate time factor (faster completion = higher score, up to 110% of normal score)
            // If they take the full time, they get 100% of their score
            // If they take half the time, they get 110% of their score
            float timeFactor = Math.Min(1.1f, Math.Max(1.0f, 1.1f - 0.2f * (float)(timeTaken.TotalMinutes / timeLimit.TotalMinutes)));

            _logger.LogInformation("Time factor: {TimeFactor} (took {TimeTaken} out of {TimeLimit})",
                timeFactor, timeTaken, timeLimit);

            foreach (var answer in userAnswers)
            {
                var question = questions.FirstOrDefault(q => q.Id == answer.QuestionId);
                if (question == null) continue;

                // Default weight is 1.0 if not specified
                float weight = 1.0f;
                if (questionWeights.TryGetValue(question.Id, out float definedWeight))
                {
                    weight = definedWeight;
                }

                int weightedPoints = (int)Math.Round(weight * 100);
                totalWeightedPoints += weightedPoints;

                if (CheckAnswer(answer, question, correctAnswers))
                {
                    correctCount++;
                    weightedCorrectPoints += weightedPoints;
                }
            }

            // Calculate weighted score with time factor
            int score = 0;
            if (totalWeightedPoints > 0)
            {
                score = (int)Math.Round(100 * (double)weightedCorrectPoints / totalWeightedPoints * timeFactor);
            }

            // Calculate accuracy (percentage of questions answered correctly)
            float accuracy = totalQuestions > 0 ? 100f * (float)correctCount / totalQuestions : 0;

            return (score, accuracy, correctCount);
        }

        private bool CheckAnswer(AnswerDto answer, QuestionDto question, Dictionary<int, string> correctAnswers)
        {
            // Get correct answer for this question
            if (!correctAnswers.TryGetValue(question.Id, out var correctAnswer))
            {
                _logger.LogWarning("No correct answer found for question ID: {QuestionId}", question.Id);
                return false;
            }

            switch (question.Type)
            {
                case "multiple-choice":
                    return CheckMultipleChoiceAnswer(answer, question, correctAnswer);

                case "fill-in-gap":
                    return CheckFillInGapAnswer(answer, correctAnswer);

                case "memory-pair":
                    return CheckMemoryAnswer(answer, question, correctAnswer);

                default:
                    _logger.LogWarning("Unknown question type: {QuestionType}", question.Type);
                    return false;
            }
        }

        private bool CheckMultipleChoiceAnswer(AnswerDto answer, QuestionDto question, string correctAnswer)
        {
            if (answer.Value is long longValue && longValue >= 0 && longValue < question.Options.Count)
            {
                return question.Options[(int)longValue] == correctAnswer;
            }
            return false;
        }

        private bool CheckFillInGapAnswer(AnswerDto answer, string correctAnswer)
        {
            var userAnswer = answer.Value?.ToString()?.Trim().ToLower();
            return userAnswer == correctAnswer.Trim().ToLower();
        }

        private bool CheckMemoryAnswer(AnswerDto answer, QuestionDto question, string correctAnswer)
        {
            try
            {
                Dictionary<string, string> memoryAnswers;

                if (answer.Value is JsonElement jsonElement)
                {
                    // Get JSON as string
                    string jsonString = jsonElement.GetRawText();

                    try
                    {
                        // Try direct deserialization first
                        memoryAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                    }
                    catch
                    {
                        // If that fails, try to parse as a nested object
                        var nestedObj = JsonSerializer.Deserialize<MemoryAnswerWrapper>(jsonString);
                        if (nestedObj?.value != null)
                        {
                            // Extract the inner dictionary
                            memoryAnswers = nestedObj.value;
                        }
                        else
                        {
                            _logger.LogError("Invalid memory answer format: {Json}", jsonString);
                            return false;
                        }
                    }
                }
                else
                {
                    _logger.LogError("Memory answer has unexpected type: {Type}", answer.Value?.GetType());
                    return false;
                }

                return CheckMemoryAnswerWithExpected(memoryAnswers, question, correctAnswer);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing memory answer");
                return false;
            }
        }

        private class MemoryAnswerWrapper
        {
            public Dictionary<string, string> value { get; set; }
        }

        private bool CheckMemoryAnswerWithExpected(Dictionary<string, string> userAnswers, QuestionDto question, string correctAnswer)
        {
            // Parse expected answers from correctAnswer string
            // Format: "pair-0-word-1:apple,pair-1-word-0:mountain,..."
            var expectedAnswers = ParseMemoryExpectedAnswer(correctAnswer);

            int correctCount = 0;
            int totalExpected = expectedAnswers.Count;

            foreach (var expected in expectedAnswers)
            {
                if (userAnswers.TryGetValue(expected.Key, out var userAnswer))
                {
                    if (userAnswer.Trim().ToLower() == expected.Value.Trim().ToLower())
                    {
                        correctCount++;
                    }
                }
            }

            // Consider memory question correct if at least 75% of pairs are recalled correctly
            return correctCount >= (totalExpected * 0.75);
        }

        private Dictionary<string, string> ParseMemoryExpectedAnswer(string correctAnswer)
        {
            var result = new Dictionary<string, string>();

            try
            {
                var pairs = correctAnswer.Split(',');
                foreach (var pair in pairs)
                {
                    var parts = pair.Split(':');
                    if (parts.Length == 2)
                    {
                        result[parts[0].Trim()] = parts[1].Trim();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing memory expected answer: {CorrectAnswer}", correctAnswer);
            }

            return result;
        }
    }
}