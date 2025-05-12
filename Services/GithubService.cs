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
        private readonly string _rawGithubBaseUrl;

        // Cache questions in memory to reduce GitHub API calls
        private readonly Dictionary<string, List<QuestionSetItem>> _questionCache = new();
        private readonly Dictionary<string, DateTime> _cacheTimestamps = new();
        private readonly TimeSpan _cacheDuration = TimeSpan.FromHours(6); // Cache for 6 hours

        public GithubService(
            HttpClient httpClient,
            ILogger<GithubService> logger,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _logger = logger;
            _configuration = configuration;

            // GitHub raw content URL (converts GitHub UI URLs to raw content URLs)
            _rawGithubBaseUrl = _configuration["GitHub:BaseUrl"] ??
                                "https://raw.githubusercontent.com/RazvanBordinc/questions/main/";
        }
 
        public class QuestionSetItem
        {
            public DTOs.Test.QuestionDto Question { get; set; }
            public string CorrectAnswer { get; set; }
            public float Weight { get; set; } = 1.0f; // Default weight
        }
        public async Task<List<QuestionSetItem>> GetQuestionsAsync(string testTypeId, int count = 20)
        {
            try
            {
                // Check in-memory cache first
                if (_questionCache.TryGetValue(testTypeId, out var cachedQuestions) &&
                    _cacheTimestamps.TryGetValue(testTypeId, out var timestamp))
                {
                    if (DateTime.UtcNow - timestamp < _cacheDuration)
                    {
                        _logger.LogInformation("Returning cached questions for test type: {TestTypeId}", testTypeId);
                        return cachedQuestions;
                    }
                }

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
                var questions = JsonSerializer.Deserialize<List<QuestionDto>>(content,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                // Convert to QuestionSetItem format (includes correct answers and weights)
                var result = new List<QuestionSetItem>();
                if (questions != null)
                {
                    // Take only the requested number of questions, or all if less than requested
                    var subset = questions.Count <= count ? questions : questions.Take(count).ToList();

                    for (int i = 0; i < subset.Count; i++)
                    {
                        var question = subset[i];

                        // Ensure question has an ID
                        if (question.Id <= 0)
                        {
                            question.Id = i + 1;
                        }

                        // Extract correctAnswer from question properties
                        string correctAnswer = question.CorrectAnswer;

                        // Set default weight based on question type
                        float weight = 1.0f;
                        if (question.Type == "fill-in-gap") weight = 1.5f;
                        if (question.Type == "memory-pair") weight = 2.0f;

                        result.Add(new QuestionSetItem
                        {
                            Question = question,
                            CorrectAnswer = correctAnswer,
                            Weight = weight
                        });
                    }

                    // Cache the results
                    _questionCache[testTypeId] = result;
                    _cacheTimestamps[testTypeId] = DateTime.UtcNow;
                }

                _logger.LogInformation("Successfully fetched {Count} questions for test type: {TestTypeId}",
                    result.Count, testTypeId);
                return result;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching questions from GitHub for test type: {TestTypeId}", testTypeId);
                throw;
            }
        }
    }
}