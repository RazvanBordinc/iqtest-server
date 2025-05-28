using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using IqTest_server.Controllers;
using IqTest_server.DTOs.Test;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Controllers
{
    [ApiController]
    [Route("api/test")]
    public class TestController : BaseController
    {
        private readonly TestService _testService;
        private new readonly ILogger<TestController> _logger;

        public TestController(TestService testService, ILogger<TestController> logger) : base(logger)
        {
            _testService = testService;
            _logger = logger;
        }

        // GET: api/test/types
        [HttpGet("types")]
        public async Task<IActionResult> GetTestTypes()
        {
            try
            {
                var testTypes = await _testService.GetAllTestTypesAsync();
                return Ok(testTypes);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test types");
                return StatusCode(500, new { message = "An error occurred while retrieving test types" });
            }
        }

        // GET: api/test/types/{id}
        [HttpGet("types/{id}")]
        public async Task<IActionResult> GetTestType(string id)
        {
            try
            {
                var testType = await _testService.GetTestTypeByIdAsync(id);
                if (testType == null)
                {
                    return NotFound(new { message = "Test type not found" });
                }
                return Ok(testType);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test type {TestTypeId}", id);
                return StatusCode(500, new { message = "An error occurred while retrieving the test type" });
            }
        }

        // GET: api/test/availability/{testTypeId}
        [HttpGet("availability/{testTypeId}")]
        [ResponseCache(Duration = 30, VaryByHeader = "Authorization", Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> CheckTestAvailability(string testTypeId)
        {
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var userId = GetUserId();
                var availability = await _testService.CheckTestAvailabilityAsync(userId, testTypeId);
                
                return Ok(availability);
            }
            catch (Exception ex)
            {
                stopwatch.Stop();
                _logger.LogError(ex, "[TEST_AVAILABILITY] Error for user {UserId}, test {TestTypeId} after {ElapsedMs}ms", 
                    GetUserId(), testTypeId, stopwatch.ElapsedMilliseconds);
                return StatusCode(500, new { message = "An error occurred while checking test availability" });
            }
        }

        // POST: api/test/availability/batch
        [HttpPost("availability/batch")]
        [ResponseCache(Duration = 30, VaryByHeader = "Authorization", Location = ResponseCacheLocation.Any)]
        public async Task<IActionResult> CheckBatchTestAvailability([FromBody] List<string> testTypeIds)
        {
            try
            {
                var userId = GetUserId();
                var results = new Dictionary<string, object>();
                
                // Process all test types in parallel
                var tasks = testTypeIds.Select(async testTypeId =>
                {
                    var availability = await _testService.CheckTestAvailabilityAsync(userId, testTypeId);
                    return new { testTypeId, availability };
                }).ToList();
                
                var allResults = await Task.WhenAll(tasks);
                
                foreach (var result in allResults)
                {
                    results[result.testTypeId] = result.availability;
                }
                
                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking batch test availability for user {UserId}", GetUserId());
                return StatusCode(500, new { message = "An error occurred while checking test availability" });
            }
        }

        // POST: api/test/submit
        [HttpPost("submit")]
        [Authorize]
        public async Task<IActionResult> SubmitTestAnswers([FromBody] SubmitAnswersDto submitDto)
        {
            try
            {
                var userId = GetUserId();
                var result = await _testService.SubmitTestAnswersAsync(userId, submitDto);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                _logger.LogWarning(ex, "Invalid test submission for user {UserId}", GetUserId());
                return BadRequest(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error submitting test answers for user {UserId}", GetUserId());
                return StatusCode(500, new { message = "An error occurred while submitting test answers" });
            }
        }

        // GET: api/test/stats/{testTypeId}
        [HttpGet("stats/{testTypeId}")]
        public async Task<IActionResult> GetTestStats(string testTypeId)
        {
            try
            {
                var stats = await _testService.GetTestStatsAsync(testTypeId);
                return Ok(stats);
            }
            catch (KeyNotFoundException ex)
            {
                _logger.LogWarning(ex, "Test type not found: {TestTypeId}", testTypeId);
                return NotFound(new { message = ex.Message });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error retrieving test stats for test type {TestTypeId}", testTypeId);
                return StatusCode(500, new { message = "An error occurred while retrieving test statistics" });
            }
        }

        // POST: api/test/clear-cooldowns (admin endpoint for debugging)
        [HttpPost("clear-cooldowns")]
        [Authorize]
        public async Task<IActionResult> ClearUserCooldowns()
        {
            try
            {
                var userId = GetUserId();
                await _testService.ClearUserTestCooldownsAsync(userId);
                _logger.LogInformation("Cleared test cooldowns for user {UserId}", userId);
                return Ok(new { message = "Test cooldowns cleared successfully" });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing test cooldowns for user {UserId}", GetUserId());
                return StatusCode(500, new { message = "An error occurred while clearing test cooldowns" });
            }
        }
    }
}