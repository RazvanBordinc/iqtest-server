using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using IqTest_server.Models;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;

namespace IqTest_server.Services
{
    public class DeepSeekService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<DeepSeekService> _logger;
        private readonly string _apiKey;
        private readonly string _apiUrl = "https://api.deepseek.com/chat/completions";

        public DeepSeekService(
            HttpClient httpClient,
            ILogger<DeepSeekService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _apiKey = configuration["DeepSeek:ApiKey"];

            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.LogError("DeepSeek API key is not configured");
                throw new InvalidOperationException("DeepSeek API key is not configured");
            }

            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_apiKey}");
        }

        public async Task<List<QuestionDto>> GenerateNumericalReasoningQuestionsAsync(int count = 20)
        {
            string prompt = GetNumericalReasoningPrompt(count);
            var response = await SendPromptToDeepSeekAsync(prompt);
            return ParseQuestionsFromResponse(response, "number-logic");
        }

        public async Task<List<QuestionDto>> GenerateVerbalIntelligenceQuestionsAsync(int count = 20)
        {
            string prompt = GetVerbalIntelligencePrompt(count);
            var response = await SendPromptToDeepSeekAsync(prompt);
            return ParseQuestionsFromResponse(response, "word-logic");
        }

        public async Task<List<QuestionDto>> GenerateMemoryRecallQuestionsAsync(int count = 15)
        {
            string prompt = GetMemoryRecallPrompt(count);
            var response = await SendPromptToDeepSeekAsync(prompt);
            return ParseQuestionsFromResponse(response, "memory");
        }

        public async Task<List<QuestionDto>> GenerateComprehensiveIqQuestionsAsync(int count = 16)
        {
            string prompt = GetComprehensiveIqPrompt(count);
            var response = await SendPromptToDeepSeekAsync(prompt);
            return ParseQuestionsFromResponse(response, "mixed");
        }

        private async Task<string> SendPromptToDeepSeekAsync(string promptText)
        {
            try
            {
                var messages = new[]
                {
                    new { role = "system", content = "You are an AI specialized in generating challenging, varied, and educational IQ test questions. You provide answers in JSON format exactly as specified." },
                    new { role = "user", content = promptText }
                };

                var request = new
                {
                    model = "deepseek-chat",
                    messages = messages,
                    stream = false,
                    temperature = 0.7,
                    max_tokens = 4000
                };

                var content = new StringContent(JsonSerializer.Serialize(request), Encoding.UTF8, "application/json");
                _logger.LogInformation("Sending request to DeepSeek API");

                var response = await _httpClient.PostAsync(_apiUrl, content);
                response.EnsureSuccessStatusCode();

                var responseContent = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Received response from DeepSeek API");

                using JsonDocument document = JsonDocument.Parse(responseContent);
                return document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error sending prompt to DeepSeek API");
                throw;
            }
        }

        private List<QuestionDto> ParseQuestionsFromResponse(string responseContent, string testTypeId)
        {
            try
            {
                _logger.LogInformation("Parsing questions from DeepSeek response");

                // Extract JSON portion from the response (in case the AI adds extra text)
                int jsonStart = responseContent.IndexOf('{');
                int jsonEnd = responseContent.LastIndexOf('}');

                if (jsonStart == -1 || jsonEnd == -1 || jsonEnd <= jsonStart)
                {
                    _logger.LogError("Invalid JSON format in DeepSeek response");
                    throw new FormatException("Invalid JSON format in DeepSeek response");
                }

                string jsonContent = responseContent.Substring(jsonStart, jsonEnd - jsonStart + 1);

                // Parse the JSON content
                using JsonDocument document = JsonDocument.Parse(jsonContent);
                var questions = new List<QuestionDto>();

                // The structure depends on the specific format requested in your prompt
                var questionsArray = document.RootElement.GetProperty("questions");

                foreach (var questionElement in questionsArray.EnumerateArray())
                {
                    var question = new QuestionDto
                    {
                        Id = questions.Count + 1, // Assign temporary ID
                        Type = questionElement.GetProperty("type").GetString(),
                        Category = questionElement.GetProperty("category").GetString(),
                        Text = questionElement.GetProperty("text").GetString()
                    };

                    // Handle options for multiple-choice questions
                    if (questionElement.TryGetProperty("options", out var optionsElement))
                    {
                        question.Options = new List<string>();
                        foreach (var option in optionsElement.EnumerateArray())
                        {
                            question.Options.Add(option.GetString());
                        }
                    }

                    // Handle memory-pair specific fields
                    if (question.Type == "memory-pair")
                    {
                        if (questionElement.TryGetProperty("memorizationTime", out var memTimeElement))
                        {
                            question.MemorizationTime = memTimeElement.GetInt32();
                        }
                        else
                        {
                            question.MemorizationTime = 15; // Default value
                        }

                        if (questionElement.TryGetProperty("pairs", out var pairsElement))
                        {
                            question.Pairs = new List<List<string>>();
                            foreach (var pairElement in pairsElement.EnumerateArray())
                            {
                                var pair = new List<string>();
                                foreach (var word in pairElement.EnumerateArray())
                                {
                                    pair.Add(word.GetString());
                                }
                                question.Pairs.Add(pair);
                            }
                        }

                        if (questionElement.TryGetProperty("missingIndices", out var missingIndicesElement))
                        {
                            question.MissingIndices = new List<List<int>>();
                            foreach (var indicesElement in missingIndicesElement.EnumerateArray())
                            {
                                var indices = new List<int>();
                                foreach (var index in indicesElement.EnumerateArray())
                                {
                                    indices.Add(index.GetInt32());
                                }
                                question.MissingIndices.Add(indices);
                            }
                        }
                    }

                    questions.Add(question);
                }

                _logger.LogInformation("Successfully parsed {Count} questions", questions.Count);
                return questions;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing questions from DeepSeek response");
                throw;
            }
        }

        #region Prompts

        private string GetNumericalReasoningPrompt(int count)
        {
            return $@"Generate {count} numerical reasoning IQ test questions with the following distribution:
- 4 fill-in-the-gap questions (simple numerical pattern completion)
- 16 multiple-choice questions with exactly 6 possible answers each

Each question should test different numerical reasoning abilities like pattern recognition, mathematical operations, logical sequences, etc.

Return your response in the following JSON format:
{{
  ""questions"": [
    {{
      ""type"": ""fill-in-gap"",
      ""category"": ""numerical"",
      ""text"": ""Fill in the missing number: 1, 3, _, 7, 9"",
      ""correctAnswer"": ""5""
    }},
    {{
      ""type"": ""multiple-choice"",
      ""category"": ""numerical"",
      ""text"": ""What number comes next in the sequence: 2, 4, 8, 16, ?"",
      ""options"": [""24"", ""32"", ""28"", ""30"", ""36"", ""40""],
      ""correctAnswer"": ""32""
    }}
    // ... more questions
  ]
}}

Make sure all questions are original, challenging but solvable, and appropriately test numerical reasoning skills. Ensure questions have clear, unambiguous answers and vary in difficulty.";
        }

        private string GetVerbalIntelligencePrompt(int count)
        {
            return $@"Generate {count} verbal intelligence IQ test questions with the following distribution:
- 10 fill-in-the-gap questions (vocabulary, analogies, word completion)
- 10 multiple-choice questions with exactly 6 possible answers each

Each question should test different verbal reasoning abilities like vocabulary, analogies, language comprehension, etc.

Return your response in the following JSON format:
{{
  ""questions"": [
    {{
      ""type"": ""fill-in-gap"",
      ""category"": ""verbal"",
      ""text"": ""Complete the analogy: Book is to Reading as Fork is to _____"",
      ""correctAnswer"": ""Eating""
    }},
    {{
      ""type"": ""multiple-choice"",
      ""category"": ""verbal"",
      ""text"": ""Which word is closest in meaning to 'ephemeral'?"",
      ""options"": [""Permanent"", ""Temporary"", ""Important"", ""Colorful"", ""Spiritual"", ""Dramatic""],
      ""correctAnswer"": ""Temporary""
    }}
    // ... more questions
  ]
}}

Make sure all questions are original, challenging but solvable, and appropriately test verbal intelligence. Ensure questions have clear, unambiguous answers and vary in difficulty.";
        }

        private string GetMemoryRecallPrompt(int count)
        {
            return $@"Generate {count} memory recall IQ test questions with the following distribution:
- 10 memory-pair questions (pairs of words where one word is shown and the other must be recalled)
- 5 memory-pair questions with triplets (three related words where one or two are shown and the others must be recalled)

Each memory-pair question should include appropriate memorization time and missing indices.

Return your response in the following JSON format:
{{
  ""questions"": [
    {{
      ""type"": ""memory-pair"",
      ""category"": ""memory"",
      ""text"": ""Recall the missing words from each pair"",
      ""memorizationTime"": 15,
      ""pairs"": [
        [""boat"", ""apple""],
        [""mountain"", ""coffee""],
        [""laptop"", ""forest""],
        [""bicycle"", ""ocean""]
      ],
      ""missingIndices"": [
        [1],
        [0],
        [1],
        [0]
      ],
      ""correctAnswer"": ""pair-0-word-1:apple,pair-1-word-0:mountain,pair-2-word-1:forest,pair-3-word-0:bicycle""
    }},
    {{
      ""type"": ""memory-pair"",
      ""category"": ""memory"",
      ""text"": ""Recall the missing words from each triplet"",
      ""memorizationTime"": 20,
      ""pairs"": [
        [""goat"", ""steel"", ""house""],
        [""river"", ""pencil"", ""cloud""],
        [""candle"", ""guitar"", ""diamond""]
      ],
      ""missingIndices"": [
        [1, 2],
        [0, 2],
        [0, 1]
      ],
      ""correctAnswer"": ""pair-0-word-1:steel,pair-0-word-2:house,pair-1-word-0:river,pair-1-word-2:cloud,pair-2-word-0:candle,pair-2-word-1:guitar""
    }}
    // ... more questions
  ]
}}

Make sure all questions are original, challenging but solvable, and appropriately test memory recall abilities. Use diverse word pairs that are not too obvious in their associations but can be memorized.";
        }

        private string GetComprehensiveIqPrompt(int count)
        {
            return $@"Generate a comprehensive IQ test with {count} questions across different categories:

Numerical Reasoning (8 questions):
- 6 multiple-choice questions with exactly 6 possible answers each
- 2 fill-in-the-gap questions

Verbal Intelligence (8 questions):
- 4 multiple-choice questions with exactly 6 possible answers each
- 4 fill-in-the-gap questions

Memory & Recall (12 questions):
- 8 memory-pair questions (pairs of words)
- 4 memory-pair questions with triplets

Each question should test different cognitive abilities appropriate to its category.

Return your response in the following JSON format:
{{
  ""questions"": [
    {{
      ""type"": ""multiple-choice"",
      ""category"": ""numerical"",
      ""text"": ""What number comes next in the sequence: 2, 4, 8, 16, ?"",
      ""options"": [""24"", ""32"", ""28"", ""30"", ""36"", ""40""],
      ""correctAnswer"": ""32""
    }},
    {{
      ""type"": ""fill-in-gap"",
      ""category"": ""verbal"",
      ""text"": ""Complete the analogy: Book is to Reading as Fork is to _____"",
      ""correctAnswer"": ""Eating""
    }},
    {{
      ""type"": ""memory-pair"",
      ""category"": ""memory"",
      ""text"": ""Recall the missing words from each pair"",
      ""memorizationTime"": 15,
      ""pairs"": [
        [""boat"", ""apple""],
        [""mountain"", ""coffee""]
      ],
      ""missingIndices"": [
        [1],
        [0]
      ],
      ""correctAnswer"": ""pair-0-word-1:apple,pair-1-word-0:mountain""
    }}
    // ... more questions
  ]
}}

Make sure all questions are original, challenging but solvable, and appropriately test different aspects of intelligence. Ensure questions have clear, unambiguous answers and vary in difficulty.";
        }

        #endregion
    }
}