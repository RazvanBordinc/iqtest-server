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
    public class ResultsController : ControllerBase
    {
        private readonly TestService _testService;
        private readonly ILogger<ResultsController> _logger;

        public ResultsController(TestService testService, ILogger<ResultsController> logger)
        {
            _testService = testService;
            _logger = logger;
        }

        [HttpGet]
        public async Task<IActionResult> GetUserResults()
        {
            // This endpoint would return the user's test results
            // For now it's just a placeholder returning an empty array
            return Ok(new object[0]);
        }

        [HttpGet("{resultId}")]
        public async Task<IActionResult> GetResultById(int resultId)
        {
            // This endpoint would return a specific test result
            // For now it's just a placeholder returning a not found result
            return NotFound(new { message = "Result not found" });
        }

        [HttpGet("test-type/{testTypeId}")]
        public async Task<IActionResult> GetResultsByTestType(string testTypeId)
        {
            // This endpoint would return the user's results for a specific test type
            // For now it's just a placeholder returning an empty array
            return Ok(new object[0]);
        }
    }
}