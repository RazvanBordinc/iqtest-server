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
            Dictionary<int, int> questionWeights,
            TimeSpan timeTaken,
            TimeSpan timeLimit)
        {
            int weightedCorrectPoints = 0;
            int totalWeightedPoints = 0;
            int correctCount = 0;
            int totalQuestions = userAnswers.Count;

            // Calculate time factor - more strict scoring
            // Quick completion gives up to 105% bonus
            // Taking full time gives 90% penalty
            // Taking over time limit gives severe penalty
            float timeRatio = (float)(timeTaken.TotalMinutes / timeLimit.TotalMinutes);
            float timeFactor;
            
            if (timeRatio > 1.0f)
            {
                // Over time limit - severe penalty
                timeFactor = Math.Max(0.7f, 1.0f - (timeRatio - 1.0f) * 0.5f);
            }
            else if (timeRatio > 0.9f)
            {
                // 90-100% of time - penalty
                timeFactor = 0.9f + (1.0f - timeRatio) * 1.5f;
            }
            else if (timeRatio < 0.3f)
            {
                // Too fast (possibly cheating) - slight penalty
                timeFactor = 0.95f;
            }
            else if (timeRatio < 0.5f)
            {
                // Fast completion - bonus
                timeFactor = 1.05f;
            }
            else
            {
                // Linear scale between 50% and 90%
                timeFactor = 1.05f - 0.15f * ((timeRatio - 0.5f) / 0.4f);
            }

            _logger.LogInformation("Time factor: {TimeFactor} (took {TimeTaken} out of {TimeLimit})",
                timeFactor, timeTaken, timeLimit);

            // Use index-based matching since frontend sends sequential IDs
            for (int i = 0; i < userAnswers.Count; i++)
            {
                var answer = userAnswers[i];
                
                // Find the corresponding question by index (since frontend uses 1-based indexing)
                var questionIndex = answer.QuestionId - 1;
                if (questionIndex < 0 || questionIndex >= questions.Count)
                {
                    _logger.LogWarning("Invalid question index: {Index} for answer ID: {AnswerId}", questionIndex, answer.QuestionId);
                    continue;
                }
                
                var question = questions[questionIndex];

                // Default weight is 3 if not specified (from 2-8 scale)
                int weight = 3;
                // Use answer.QuestionId to get weight since it corresponds to the sequential ID
                if (questionWeights.TryGetValue(answer.QuestionId, out int definedWeight))
                {
                    weight = definedWeight;
                }

                totalWeightedPoints += weight;

                // Special handling for memory questions to allow partial credit
                if (answer.Value != null)
                {
                    if (question.Type.ToLower() == "memory-pair" || question.Type.ToLower() == "memory")
                    {
                        // Get partial credit for memory questions
                        float correctRatio = GetMemoryAnswerPartialCredit(answer, question, correctAnswers);
                        
                        // Log partial credit amount
                        _logger.LogInformation("Memory question {QuestionId} partial credit: {CorrectRatio:P2}", 
                            answer.QuestionId, correctRatio);
                        
                        // Add partial points based on correctness ratio
                        weightedCorrectPoints += (int)(weight * correctRatio);
                        
                        // For the correct count, consider it correct if at least 75% correct
                        if (correctRatio >= 0.75f)
                        {
                            correctCount++;
                        }
                    }
                    // Standard checks for other question types
                    else if (CheckAnswer(answer, question, correctAnswers))
                    {
                        correctCount++;
                        weightedCorrectPoints += weight;
                    }
                }
                // Null answers are implicitly incorrect and contribute 0 points
            }

            // Calculate weighted score with time factor
            // Apply additional penalties for low performance
            int score = 0;
            if (totalWeightedPoints > 0)
            {
                double rawScore = 100 * (double)weightedCorrectPoints / totalWeightedPoints;
                
                // Apply exponential curve for lower scores to make it more difficult
                if (rawScore < 50)
                {
                    // Below 50% gets exponentially harder (squared scaling)
                    rawScore = Math.Pow(rawScore / 50, 2) * 50;
                }
                else if (rawScore < 70)
                {
                    // 50-70% gets moderately harder
                    rawScore = 50 + (rawScore - 50) * 0.8;
                }
                
                // Apply time factor
                score = (int)Math.Round(rawScore * timeFactor);
                
                // Ensure minimum score isn't too high
                score = Math.Max(0, Math.Min(100, score));
            }

            // Calculate accuracy (percentage of questions answered correctly)
            float accuracy = totalQuestions > 0 ? 100f * (float)correctCount / totalQuestions : 0;

            return (score, accuracy, correctCount);
        }

        private bool CheckAnswer(AnswerDto answer, QuestionDto question, Dictionary<int, string> correctAnswers)
        {
            // Since frontend sends sequential IDs (1, 2, 3...) that match the question.Id
            // we can directly use answer.QuestionId to get the correct answer
            if (!correctAnswers.TryGetValue(answer.QuestionId, out var correctAnswer))
            {
                _logger.LogWarning("No correct answer found for question ID: {QuestionId}", answer.QuestionId);
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
            if (answer.Value == null)
            {
                return false;
            }
            
            int index = -1;
            
            // Handle both int32 and int64 values
            if (answer.Value is long longValue)
            {
                index = (int)longValue;
            }
            else if (answer.Value is int intValue)
            {
                index = intValue;
            }
            
            if (question.Options != null && index >= 0 && index < question.Options.Count)
            {
                return question.Options[index] == correctAnswer;
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
                if (answer.Value == null)
                {
                    return false;
                }
                
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
                else if (answer.Value is string stringValue)
                {
                    // Handle case where value is already a string (from frontend)
                    try
                    {
                        memoryAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(stringValue);
                    }
                    catch
                    {
                        // If that fails, try to parse as a nested object
                        var nestedObj = JsonSerializer.Deserialize<MemoryAnswerWrapper>(stringValue);
                        if (nestedObj?.value != null)
                        {
                            // Extract the inner dictionary
                            memoryAnswers = nestedObj.value;
                        }
                        else
                        {
                            _logger.LogError("Invalid memory answer format (string): {Json}", stringValue);
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

            // Calculate a score based on the percentage of correct pairs
            // This allows for partial credit instead of all-or-nothing
            float correctPercentage = (float)correctCount / totalExpected;
            
            _logger.LogInformation("Memory answer check: {CorrectCount}/{TotalExpected} = {Percentage:P2}", 
                correctCount, totalExpected, correctPercentage);
                
            // Consider fully correct if at least 75% of pairs are recalled correctly
            // But give partial credit too - this affects the overall weighted score
            return correctPercentage >= 0.75f;
        }
        
        // Add a helper method to get partial credit for memory questions from the AnswerDto
        private float GetMemoryAnswerPartialCredit(AnswerDto answer, QuestionDto question, Dictionary<int, string> correctAnswers)
        {
            try
            {
                // Get the correct answer string for this question
                if (!correctAnswers.TryGetValue(answer.QuestionId, out var correctAnswer))
                {
                    _logger.LogWarning("No correct answer found for memory question ID: {QuestionId}", answer.QuestionId);
                    return 0f;
                }
                
                // Parse the user's answer into a dictionary
                Dictionary<string, string> userAnswers = null;
                
                if (answer.Value is JsonElement jsonElement)
                {
                    // Get JSON as string
                    string jsonString = jsonElement.GetRawText();
                    try
                    {
                        userAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(jsonString);
                    }
                    catch
                    {
                        var nestedObj = JsonSerializer.Deserialize<MemoryAnswerWrapper>(jsonString);
                        userAnswers = nestedObj?.value;
                    }
                }
                else if (answer.Value is string stringValue)
                {
                    try
                    {
                        userAnswers = JsonSerializer.Deserialize<Dictionary<string, string>>(stringValue);
                    }
                    catch
                    {
                        var nestedObj = JsonSerializer.Deserialize<MemoryAnswerWrapper>(stringValue);
                        userAnswers = nestedObj?.value;
                    }
                }
                
                if (userAnswers == null)
                {
                    _logger.LogError("Could not parse memory answer to dictionary: {Answer}", answer.Value);
                    return 0f;
                }
                
                // Now calculate the partial credit
                var expectedAnswers = ParseMemoryExpectedAnswer(correctAnswer);
                int correctCount = 0;
                int totalExpected = expectedAnswers.Count;

                if (totalExpected == 0)
                    return 0f;

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

                float correctRatio = (float)correctCount / totalExpected;
                _logger.LogInformation("Memory answer partial credit: {CorrectCount}/{TotalExpected} = {CorrectRatio:P2}",
                    correctCount, totalExpected, correctRatio);
                    
                return correctRatio;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error calculating memory answer partial credit");
                return 0f;
            }
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