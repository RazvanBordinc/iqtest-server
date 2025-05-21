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
        public async Task<IActionResult> CheckTestAvailability(string testTypeId)
        {
            var userId = GetUserId();
            var canTakeTest = await _testService.CanUserTakeTestAsync(userId, testTypeId);
            var timeUntilNext = await _testService.GetTimeUntilNextAttemptAsync(userId, testTypeId);
            
            return Ok(new 
            {
                canTake = canTakeTest,
                timeUntilNext = timeUntilNext?.TotalSeconds,
                message = canTakeTest ? "Test available" : "You must wait 24 hours between test attempts"
            });
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