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
    public class QuestionController : ControllerBase
    {
        private readonly QuestionService _questionService;
        private readonly ILogger<QuestionController> _logger;

        public QuestionController(QuestionService questionService, ILogger<QuestionController> logger)
        {
            _questionService = questionService;
            _logger = logger;
        }

        [HttpGet("test/{testTypeId}")]
        public async Task<IActionResult> GetQuestionsByTestType(string testTypeId)
        {
            _logger.LogInformation("Getting questions for test type: {TestTypeId}", testTypeId);
            var questions = await _questionService.GetQuestionsByTestTypeIdAsync(testTypeId);
            return Ok(questions);
        }

        [HttpGet("{questionId}")]
        public async Task<IActionResult> GetQuestionById(int questionId)
        {
            _logger.LogInformation("Getting question by ID: {QuestionId}", questionId);
            var question = await _questionService.GetQuestionByIdAsync(questionId);

            if (question == null)
            {
                return NotFound(new { message = "Question not found" });
            }

            return Ok(question);
        }
    }
}