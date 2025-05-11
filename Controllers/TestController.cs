// Controllers/TestController.cs
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
            // Add more logging here
            var userId = GetUserId();
            _logger.LogInformation("GetTestQuestions called with testTypeId: {TestTypeId}, userId: {UserId}",
                testTypeId, userId);

            // Rest of the code...
            var result = await _testService.GenerateQuestionsForTestAsync(testTypeId);
            _logger.LogInformation("Returning {Count} questions", result.Questions.Count);
            return Ok(result.Questions);
        }

        [HttpPost("submit")]
        public async Task<IActionResult> SubmitTest([FromBody] SubmitAnswersDto submission)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
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
    }
}