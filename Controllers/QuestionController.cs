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
    public class QuestionController : BaseController  // Changed from ControllerBase to BaseController
    {
        private readonly QuestionService _questionService;

        public QuestionController(QuestionService questionService, ILogger<QuestionController> logger)
            : base(logger)  // Pass logger to base controller
        {
            _questionService = questionService;
        }

        [HttpGet("test/{testTypeId}")]
        public async Task<IActionResult> GetQuestionsByTestType(string testTypeId)
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to questions - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting questions for test type: {TestTypeId}, User: {UserId}", testTypeId, userId);
            var questions = await _questionService.GetQuestionsByTestTypeIdAsync(testTypeId);
            return Ok(questions);
        }

        [HttpGet("{questionId}")]
        public async Task<IActionResult> GetQuestionById(int questionId)
        {
            // Add authentication check
            var userId = GetUserId();
            if (userId <= 0)
            {
                _logger.LogWarning("Unauthorized access to question - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting question by ID: {QuestionId}, User: {UserId}", questionId, userId);
            var question = await _questionService.GetQuestionByIdAsync(questionId);

            if (question == null)
            {
                return NotFound(new { message = "Question not found" });
            }

            return Ok(question);
        }
    }
}