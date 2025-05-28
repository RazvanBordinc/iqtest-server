using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IqTest_server.Services
{
    public class GithubService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<GithubService> _logger;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;
        private readonly string _rawGithubBaseUrl;

        public GithubService(
            HttpClient httpClient,
            ILogger<GithubService> logger,
            IConfiguration configuration,
            RedisService redisService)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;
            _redisService = redisService;

            // GitHub raw content URL (converts GitHub UI URLs to raw content URLs)
            _rawGithubBaseUrl = _configuration["GitHub:BaseUrl"] ??
                                "https://raw.githubusercontent.com/RazvanBordinc/questions/main/";
        }
 
        public class QuestionSetItem
        {
            public DTOs.Test.QuestionDto Question { get; set; }
            public string CorrectAnswer { get; set; }
            public int Weight { get; set; } // Difficulty weight from 2-8
        }

        public async Task<List<QuestionSetItem>> GetQuestionsAsync(string testTypeId, int count = 20)
        {
            try
            {
                // First check Redis cache
                string redisKey = $"questions:{testTypeId}";
                var cachedQuestions = await _redisService.GetAsync<List<QuestionSetItem>>(redisKey);
                
                if (cachedQuestions != null && cachedQuestions.Count > 0)
                {
                    _logger.LogInformation("Returning {ReturnCount} cached questions out of {TotalCount} from Redis for test type: {TestTypeId}", 
                        Math.Min(cachedQuestions.Count, count), cachedQuestions.Count, testTypeId);
                    return cachedQuestions.Take(count).ToList();
                }

                // If not in cache, fetch from GitHub
                string filename;
                switch (testTypeId)
                {
                    case "number-logic":
                        filename = "Numerical.json";
                        break;
                    case "word-logic":
                        filename = "Verbal.json";
                        break;
                    case "memory":
                        filename = "Memory.json";
                        break;
                    case "mixed":
                        filename = "Comprehensive.json";
                        break;
                    default:
                        throw new ArgumentException($"Unknown test type: {testTypeId}");
                }

                string url = $"{_rawGithubBaseUrl}{filename}";
                _logger.LogInformation("Fetching questions from GitHub: {Url}", url);

                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                _logger.LogInformation("Raw GitHub content length: {Length} characters for {TestTypeId}", content.Length, testTypeId);
                
                var questions = JsonSerializer.Deserialize<List<QuestionDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                _logger.LogInformation("Deserialized {Count} questions from GitHub for {TestTypeId}", questions?.Count ?? 0, testTypeId);

                // Convert to QuestionSetItem format with weights
                var result = new List<QuestionSetItem>();
                if (questions != null)
                {
                    for (int i = 0; i < questions.Count; i++)
                    {
                        var question = questions[i];

                        // Ensure question has an ID
                        if (question.Id <= 0)
                        {
                            question.Id = i + 1;
                        }

                        // Extract correctAnswer from question properties
                        string correctAnswer = question.CorrectAnswer;

                        // Calculate weight based on question type and complexity
                        int weight = CalculateQuestionWeight(question);

                        result.Add(new QuestionSetItem
                        {
                            Question = question,
                            CorrectAnswer = correctAnswer,
                            Weight = weight
                        });
                    }

                    // Store in Redis with 24-hour expiration
                    await _redisService.SetAsync(redisKey, result, TimeSpan.FromHours(24));
                }

                _logger.LogInformation("Successfully fetched and cached {Count} questions for test type: {TestTypeId}",
                    result.Count, testTypeId);
                
                // Return ALL questions, not limited by count - let the calling service handle the selection
                _logger.LogInformation("Returning {ReturnCount} questions out of {TotalCount} for {TestTypeId}", 
                    Math.Min(result.Count, count), result.Count, testTypeId);
                    
                return result.Take(count).ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching questions from GitHub for test type: {TestTypeId}", testTypeId);
                throw;
            }
        }

        private int CalculateQuestionWeight(QuestionDto question)
        {
            // Base weight depending on question type
            int baseWeight = question.Type switch
            {
                "multiple-choice" => 3,
                "fill-in-gap" => 5,
                "memory-pair" => 6,
                _ => 3
            };

            // Adjust based on complexity (you can enhance this logic based on question content)
            if (question.Options != null && question.Options.Count > 5)
            {
                baseWeight += 1;
            }

            if (question.Type == "memory-pair" && question.Pairs != null && question.Pairs.Count > 5)
            {
                baseWeight += 1;
            }

            // Ensure weight is between 2 and 8
            return Math.Max(2, Math.Min(8, baseWeight));
        }
    }
}