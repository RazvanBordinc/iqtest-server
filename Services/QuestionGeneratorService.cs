 
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class QuestionGeneratorService
    {
        private readonly ILogger<QuestionGeneratorService> _logger;

        public QuestionGeneratorService(ILogger<QuestionGeneratorService> logger)
        {
            _logger = logger;
        }

        // This method will generate questions for a specific test type
        // Currently returns mockup data, will be replaced with AI generation later
        public async Task<List<QuestionDto>> GenerateQuestionsAsync(string testTypeId, int numberOfQuestions)
        {
            _logger.LogInformation("Generating {Count} questions for test type: {TestTypeId}", numberOfQuestions, testTypeId);

            // TODO: Replace with AI generation
            // For now, return mockup data based on test type
            return await GetMockupQuestions(testTypeId, numberOfQuestions);
        }

        // Mockup data - will be removed when AI integration is complete
        private async Task<List<QuestionDto>> GetMockupQuestions(string testTypeId, int numberOfQuestions)
        {
            await Task.Delay(100); // Simulate async operation

            var questions = new List<QuestionDto>();

            switch (testTypeId)
            {
                case "number-logic":
                    questions = GetNumericalMockupQuestions();
                    break;
                case "word-logic":
                    questions = GetVerbalMockupQuestions();
                    break;
                case "memory":
                    questions = GetMemoryMockupQuestions();
                    break;
                case "mixed":
                    questions = GetMixedMockupQuestions();
                    break;
                default:
                    _logger.LogWarning("Unknown test type: {TestTypeId}", testTypeId);
                    break;
            }

            // Return requested number of questions
            return questions.Take(numberOfQuestions).ToList();
        }

        private List<QuestionDto> GetNumericalMockupQuestions()
        {
            return new List<QuestionDto>
            {
                new QuestionDto
                {
                    Id = 1,
                    Type = "multiple-choice",
                    Category = "numerical",
                    Text = "What number comes next in the sequence: 2, 4, 8, 16, ?",
                    Options = new List<string> { "24", "32", "28", "30", "36", "40" }
                },
                new QuestionDto
                {
                    Id = 2,
                    Type = "fill-in-gap",
                    Category = "numerical",
                    Text = "Fill in the missing number: 1, 3, _, 7, 9"
                },
                new QuestionDto
                {
                    Id = 3,
                    Type = "multiple-choice",
                    Category = "numerical",
                    Text = "If 8 + 2x = 24, what is the value of x?",
                    Options = new List<string> { "6", "8", "10", "12", "16", "18" }
                },
                new QuestionDto
                {
                    Id = 4,
                    Type = "fill-in-gap",
                    Category = "numerical",
                    Text = "Complete the pattern: 3, 6, 12, 24, _"
                },
                new QuestionDto
                {
                    Id = 5,
                    Type = "multiple-choice",
                    Category = "numerical",
                    Text = "What is the prime factorization of 36?",
                    Options = new List<string> { "2² × 3²", "2² × 3³", "2³ × 3", "2 × 3³", "2³ × 3²", "6²" }
                },
                // Generate up to 24 questions (repeat with variations)
                new QuestionDto
                {
                    Id = 6,
                    Type = "multiple-choice",
                    Category = "numerical",
                    Text = "What's the next number in the sequence: 1, 4, 9, 16, ?",
                    Options = new List<string> { "20", "24", "25", "30", "32", "36" }
                },
                new QuestionDto
                {
                    Id = 7,
                    Type = "fill-in-gap",
                    Category = "numerical",
                    Text = "Solve for x: 3x + 7 = 22"
                },
                // Add more questions to reach the required count...
            };
        }

        private List<QuestionDto> GetVerbalMockupQuestions()
        {
            return new List<QuestionDto>
            {
                new QuestionDto
                {
                    Id = 1,
                    Type = "multiple-choice",
                    Category = "verbal",
                    Text = "Which word is closest in meaning to 'ephemeral'?",
                    Options = new List<string> { "Permanent", "Temporary", "Important", "Colorful", "Spiritual", "Dramatic" }
                },
                new QuestionDto
                {
                    Id = 2,
                    Type = "fill-in-gap",
                    Category = "verbal",
                    Text = "Complete the analogy: Book is to Reading as Fork is to _____"
                },
                new QuestionDto
                {
                    Id = 3,
                    Type = "multiple-choice",
                    Category = "verbal",
                    Text = "Which word is an antonym of 'benevolent'?",
                    Options = new List<string> { "Malicious", "Charitable", "Friendly", "Generous", "Considerate", "Sympathetic" }
                },
                new QuestionDto
                {
                    Id = 4,
                    Type = "fill-in-gap",
                    Category = "verbal",
                    Text = "Complete the word: Psy_____ogy"
                },
                new QuestionDto
                {
                    Id = 5,
                    Type = "multiple-choice",
                    Category = "verbal",
                    Text = "Identify the correct sentence:",
                    Options = new List<string>
                    {
                        "Their going to the store later.",
                        "They're going too the store later.",
                        "Their going too the store later.",
                        "They're going to the store later.",
                        "There going to the store later.",
                        "There going too the store later."
                    }
                },
                // Generate up to 28 questions...
            };
        }

        private List<QuestionDto> GetMemoryMockupQuestions()
        {
            return new List<QuestionDto>
            {
                new QuestionDto
                {
                    Id = 1,
                    Type = "memory-pair",
                    Category = "memory",
                    Text = "Recall the missing words from each pair",
                    MemorizationTime = 15,
                    Pairs = new List<List<string>>
                    {
                        new List<string> { "boat", "apple" },
                        new List<string> { "mountain", "coffee" },
                        new List<string> { "laptop", "forest" },
                        new List<string> { "bicycle", "ocean" }
                    },
                    MissingIndices = new List<List<int>>
                    {
                        new List<int> { 1 }, // "apple" is missing
                        new List<int> { 0 }, // "mountain" is missing
                        new List<int> { 1 }, // "forest" is missing
                        new List<int> { 0 }  // "bicycle" is missing
                    }
                },
                new QuestionDto
                {
                    Id = 2,
                    Type = "memory-pair",
                    Category = "memory",
                    Text = "Recall the missing words from each triplet",
                    MemorizationTime = 20,
                    Pairs = new List<List<string>>
                    {
                        new List<string> { "goat", "steel", "house" },
                        new List<string> { "river", "pencil", "cloud" },
                        new List<string> { "candle", "guitar", "diamond" }
                    },
                    MissingIndices = new List<List<int>>
                    {
                        new List<int> { 1, 2 }, // "steel" and "house" are missing
                        new List<int> { 0, 2 }, // "river" and "cloud" are missing
                        new List<int> { 0, 1 }  // "candle" and "guitar" are missing
                    }
                },
                // Generate up to 20 questions...
            };
        }

        private List<QuestionDto> GetMixedMockupQuestions()
        {
            var questions = new List<QuestionDto>();

            // Mix questions from all categories
            questions.AddRange(GetNumericalMockupQuestions().Take(10));
            questions.AddRange(GetVerbalMockupQuestions().Take(10));
            questions.AddRange(GetMemoryMockupQuestions().Take(10));

            // Re-index questions
            for (int i = 0; i < questions.Count; i++)
            {
                questions[i].Id = i + 1;
            }

            return questions.Take(40).ToList();
        }

        // Future method for AI integration
        // TODO: Implement this when AI service is ready
        // private async Task<List<QuestionDto>> GenerateQuestionsWithAI(string testTypeId, int numberOfQuestions)
        // {
        //     // Call AI API to generate questions
        //     // This will replace GetMockupQuestions in the future
        // }
    }
}