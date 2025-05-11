using System.Threading.Tasks;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    [Authorize]
    public class ResultsController : BaseController  // Changed from ControllerBase to BaseController
    {
        private readonly TestService _testService;

        public ResultsController(TestService testService, ILogger<ResultsController> logger)
            : base(logger)  // Pass logger to base controller
        {
            _testService = testService;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserResults()
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to results - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting results for User: {UserId}", userId);
            // This endpoint would return the user's test results
            // For now it's just a placeholder returning an empty array
            return Ok(new object[0]);
        }

        [HttpGet("{resultId}")]
        public async Task<IActionResult> GetResultById(int resultId)
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to result - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting result ID: {ResultId}, User: {UserId}", resultId, userId);
            // This endpoint would return a specific test result
            // For now it's just a placeholder returning a not found result
            return NotFound(new { message = "Result not found" });
        }

        [HttpGet("test-type/{testTypeId}")]
        public async Task<IActionResult> GetResultsByTestType(string testTypeId)
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to test results - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting results for test type: {TestTypeId}, User: {UserId}", testTypeId, userId);
            // This endpoint would return the user's results for a specific test type
            // For now it's just a placeholder returning an empty array
            return Ok(new object[0]);
        }
    }
}