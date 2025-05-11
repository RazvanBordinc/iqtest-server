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
    public class TestController : BaseController  // Changed from ControllerBase to BaseController
    {
        private readonly TestService _testService;

        public TestController(TestService testService, ILogger<TestController> logger)
            : base(logger)  // Pass logger to base controller
        {
            _testService = testService;
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableTests()
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to tests - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting available tests for User: {UserId}", userId);
            var tests = await _testService.GetAllTestTypesAsync();
            return Ok(tests);
        }

        [HttpGet("{testTypeId}")]
        public async Task<IActionResult> GetTestById(string testTypeId)
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to test - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting test by ID: {TestTypeId}, User: {UserId}", testTypeId, userId);
            var test = await _testService.GetTestTypeByIdAsync(testTypeId);

            if (test == null)
            {
                return NotFound(new { message = "Test type not found" });
            }

            return Ok(test);
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

            _logger.LogInformation("Submitting test for user: {UserId}, test type: {TestTypeId}", userId, submission.TestTypeId);
            var result = await _testService.SubmitTestAsync(userId, submission);
            return Ok(result);
        }
    }
}