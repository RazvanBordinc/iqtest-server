using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class QuestionsRefreshService : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<QuestionsRefreshService> _logger;
        private readonly TimeSpan _refreshInterval = TimeSpan.FromHours(24);

        public QuestionsRefreshService(
            IServiceProvider serviceProvider,
            ILogger<QuestionsRefreshService> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("Question Refresh Service is starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    _logger.LogInformation("Starting daily question refresh at {Time}", DateTimeOffset.Now);
                    await RefreshAllQuestions();
                    _logger.LogInformation("Completed daily question refresh at {Time}", DateTimeOffset.Now);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error occurred during question refresh");
                }

                // Wait for 24 hours before next refresh
                await Task.Delay(_refreshInterval, stoppingToken);
            }
        }

        private async Task RefreshAllQuestions()
        {
            using var scope = _serviceProvider.CreateScope();
            var githubService = scope.ServiceProvider.GetRequiredService<GithubService>();
            var redisService = scope.ServiceProvider.GetRequiredService<RedisService>();

            // Refresh all test types
            string[] testTypes = { "number-logic", "word-logic", "memory", "mixed" };

            foreach (var testType in testTypes)
            {
                try
                {
                    _logger.LogInformation("Refreshing questions for test type: {TestType}", testType);
                    
                    // Get question count based on test type
                    int count = GetQuestionCount(testType);
                    
                    // Fetch questions from GitHub
                    var questions = await githubService.GetQuestionsAsync(testType, count);
                    
                    // Store in Redis with 24-hour expiration
                    string key = $"questions:{testType}";
                    await redisService.SetAsync(key, questions, TimeSpan.FromHours(24));
                    
                    _logger.LogInformation("Successfully refreshed {Count} questions for {TestType}", questions.Count, testType);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error refreshing questions for test type: {TestType}", testType);
                }
            }
        }

        private int GetQuestionCount(string testType)
        {
            return testType switch
            {
                "number-logic" => 24,
                "word-logic" => 28,
                "memory" => 20,
                "mixed" => 40,
                _ => 20
            };
        }
    }
}