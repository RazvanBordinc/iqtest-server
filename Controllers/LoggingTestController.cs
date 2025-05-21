using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class LoggingTestController : ControllerBase
    {
        private readonly ILogger<LoggingTestController> _logger;
        private readonly LoggingService _loggingService;
        private readonly RedisService _redisService;

        public LoggingTestController(
            ILogger<LoggingTestController> logger,
            LoggingService loggingService,
            RedisService redisService)
        {
            _logger = logger;
            _loggingService = loggingService;
            _redisService = redisService;
        }

        [HttpGet("test")]
        [AllowAnonymous]
        public async Task<IActionResult> TestLogging()
        {
            // Test standard logging
            _logger.LogInformation("Standard log information message from test endpoint");
            _logger.LogWarning("Standard log warning message from test endpoint");

            // Test custom logging service
            _loggingService.LogInfo("Custom info log from test endpoint", new Dictionary<string, object>
            {
                { "source", "test_endpoint" },
                { "timestamp", DateTime.UtcNow },
                { "testId", Guid.NewGuid().ToString() }
            });

            _loggingService.LogWarning("Custom warning log from test endpoint", new Dictionary<string, object>
            {
                { "source", "test_endpoint" },
                { "severity", "medium" },
                { "testId", Guid.NewGuid().ToString() }
            });

            // Simulate an error
            try
            {
                throw new ApplicationException("Test exception for logging");
            }
            catch (Exception ex)
            {
                _loggingService.LogError("Custom error log from test endpoint", ex, new Dictionary<string, object>
                {
                    { "source", "test_endpoint" },
                    { "errorType", "ApplicationException" },
                    { "testId", Guid.NewGuid().ToString() }
                });
            }

            // Test Redis logging
            string testKey = $"test:logging:{Guid.NewGuid()}";
            var testData = new { Message = "Test data", Timestamp = DateTime.UtcNow };
            
            await _redisService.SetAsync(testKey, testData, TimeSpan.FromMinutes(5));
            var retrievedData = await _redisService.GetAsync<object>(testKey);
            await _redisService.KeyExistsAsync(testKey);
            await _redisService.RemoveAsync(testKey);

            return Ok(new { 
                message = "Logging test complete", 
                timestamp = DateTime.UtcNow 
            });
        }
        
        [HttpGet("frontend")]
        [AllowAnonymous]
        public IActionResult FrontendLoggingTest()
        {
            // This endpoint is meant to be called from the frontend to test API endpoint logging
            _loggingService.LogInfo("Frontend-initiated backend log test", new Dictionary<string, object>
            {
                { "source", "frontend_test" },
                { "timestamp", DateTime.UtcNow },
                { "requestPath", Request.Path },
                { "userAgent", Request.Headers["User-Agent"].ToString() }
            });
            
            return Ok(new { 
                message = "Frontend-initiated backend logging test complete", 
                timestamp = DateTime.UtcNow,
                serverInfo = Environment.MachineName,
                environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
            });
        }
    }
}