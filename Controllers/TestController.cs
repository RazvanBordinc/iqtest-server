// Controllers/TestController.cs
using System.Linq;
using System.Threading.Tasks;
using IqTest_server.DTOs.Test;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class TestController : BaseController
    {
        private readonly TestService _testService;

        public TestController(TestService testService, ILogger<TestController> logger)
            : base(logger)
        {
            _testService = testService;
        }

        [HttpGet("types")]
        public async Task<IActionResult> GetAllTestTypes()
        {
            var testTypes = await _testService.GetAllTestTypesAsync();
            return Ok(testTypes);
        }

        [HttpGet("types/{testTypeId}")]
        public async Task<IActionResult> GetTestType(string testTypeId)
        {
            var testType = await _testService.GetTestTypeByIdAsync(testTypeId);

            if (testType == null)
            {
                return NotFound(new { message = "Test type not found" });
            }

            return Ok(testType);
        }

        [HttpGet("questions/{testTypeId}")]
        public async Task<IActionResult> GetTestQuestions(string testTypeId)
        {
            var userId = GetUserId();
                
            // Check if user can take this test
            var canTakeTest = await _testService.CanUserTakeTestAsync(userId, testTypeId);
            if (!canTakeTest)
            {
                var timeUntilNext = await _testService.GetTimeUntilNextAttemptAsync(userId, testTypeId);
                return StatusCode(403, new { message = "You must wait 24 hours between test attempts", timeUntilNext });
            }

            var result = await _testService.GenerateQuestionsForTestAsync(testTypeId);
            return Ok(result.Questions);
        }
        
        [HttpGet("availability/{testTypeId}")]
        [ResponseCache(Duration = 60, VaryByQueryKeys = new string[] { "testTypeId" })] // Add caching for performance
        [AllowAnonymous] // Allow unauthenticated access for initial load
        public async Task<IActionResult> CheckTestAvailability(string testTypeId)
        {
            // Add timeout for this operation
            using var cts = new System.Threading.CancellationTokenSource(5000); // 5 second timeout
            
            try
            {
                _logger.LogInformation("Checking test availability for test type: {TestTypeId}", testTypeId);
                
                // Get userId if authenticated, or use -1 for anonymous access
                int userId = -1;
                try
                {
                    userId = GetUserId();
                }
                catch
                {
                    // If not authenticated, just use default anonymous ID
                    _logger.LogInformation("Anonymous user checking test availability for {TestTypeId}", testTypeId);
                }
                
                // For anonymous users, always return available
                if (userId <= 0)
                {
                    return Ok(new 
                    {
                        canTake = true,
                        timeUntilNext = 0,
                        message = "Test available (anonymous)",
                        userId = "anonymous"
                    });
                }

                // Run both operations concurrently for authenticated users
                var canTakeTask = _testService.CanUserTakeTestAsync(userId, testTypeId, cts.Token);
                var timeUntilNextTask = _testService.GetTimeUntilNextAttemptAsync(userId, testTypeId, cts.Token);
                
                // Wait for both tasks to complete
                await Task.WhenAll(canTakeTask, timeUntilNextTask);
                
                var canTakeTest = await canTakeTask;
                var timeUntilNext = await timeUntilNextTask;
                
                _logger.LogInformation("Test availability check completed for {TestTypeId}: CanTake={CanTake}", 
                    testTypeId, canTakeTest);
                
                return Ok(new 
                {
                    canTake = canTakeTest,
                    timeUntilNext = timeUntilNext?.TotalSeconds,
                    message = canTakeTest ? "Test available" : "You must wait 24 hours between test attempts"
                });
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                _logger.LogWarning("Test availability check timed out for {TestTypeId}", testTypeId);
                
                // Return a fallback response on timeout
                return Ok(new 
                {
                    canTake = true, // Default to allow the test
                    timeUntilNext = 0,
                    message = "Test availability check timed out, allowing test by default",
                    isTimeout = true
                });
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error checking test availability for {TestTypeId}", testTypeId);
                
                // Return a fallback response on other errors
                return Ok(new 
                {
                    canTake = true, // Default to allow the test
                    timeUntilNext = 0,
                    message = "Error checking test availability, allowing test by default",
                    isError = true
                });
            }
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitTest([FromBody] SubmitAnswersDto submission)
        {
            _logger.LogInformation("Received test submission for TestTypeId: {TestTypeId}", submission?.TestTypeId);
            
            if (!ModelState.IsValid)
            {
                var errors = ModelState
                    .Where(e => e.Value.Errors.Count > 0)
                    .Select(e => new
                    {
                        Field = e.Key,
                        Errors = e.Value.Errors.Select(error => error.ErrorMessage)
                    })
                    .ToList();
                    
                _logger.LogWarning("Invalid model state for test submission: {@Errors}", errors);
                return BadRequest(new { message = "Invalid request data", errors });
            }

            var userId = GetUserId();

            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized test submission - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Submitting test for User: {UserId}, Test Type: {TestTypeId}", userId, submission.TestTypeId);

            try
            {
                var result = await _testService.SubmitTestAsync(userId, submission);
                return Ok(result);
            }
            catch (System.Exception ex)
            {
                _logger.LogError(ex, "Error submitting test for user: {UserId}", userId);
                return StatusCode(500, new { message = "An error occurred while submitting the test" });
            }
        }
        // Method removed for production release
    }
}