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
    public class TestController : ControllerBase
    {
        private readonly TestService _testService;
        private readonly ILogger<TestController> _logger;

        public TestController(TestService testService, ILogger<TestController> logger)
        {
            _testService = testService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetAvailableTests()
        {
            _logger.LogInformation("Getting available tests");
            var tests = await _testService.GetAllTestTypesAsync();
            return Ok(tests);
        }

        [HttpGet("{testTypeId}")]
        public async Task<IActionResult> GetTestById(string testTypeId)
        {
            _logger.LogInformation("Getting test by ID: {TestTypeId}", testTypeId);
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
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Submitting test for user: {UserId}, test type: {TestTypeId}", userId, submission.TestTypeId);
            var result = await _testService.SubmitTestAsync(userId, submission);
            return Ok(result);
        }

        private int GetUserId()
        {
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }
            return 0;
        }
    }
}