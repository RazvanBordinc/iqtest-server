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
    public class LeaderboardController : ControllerBase
    {
        private readonly LeaderboardService _leaderboardService;
        private readonly ILogger<LeaderboardController> _logger;

        public LeaderboardController(LeaderboardService leaderboardService, ILogger<LeaderboardController> logger)
        {
            _leaderboardService = leaderboardService;
            _logger = logger;
        }

        [HttpGet("global")]
        public async Task<IActionResult> GetGlobalLeaderboard([FromQuery] int limit = 10)
        {
            _logger.LogInformation("Getting global leaderboard with limit: {Limit}", limit);
            var leaderboard = await _leaderboardService.GetGlobalLeaderboardAsync(limit);
            return Ok(leaderboard);
        }

        [HttpGet("test-type/{testTypeId}")]
        public async Task<IActionResult> GetTestTypeLeaderboard(string testTypeId, [FromQuery] int limit = 10)
        {
            _logger.LogInformation("Getting leaderboard for test type: {TestTypeId} with limit: {Limit}", testTypeId, limit);
            var leaderboard = await _leaderboardService.GetTestTypeLeaderboardAsync(testTypeId, limit);
            return Ok(leaderboard);
        }

        [HttpGet("user-ranking")]
        public async Task<IActionResult> GetUserRanking()
        {
            var userId = GetUserId();
            if (userId <= 0)
            {
                return Unauthorized(new { message = "User not authenticated" });
            }

            _logger.LogInformation("Getting ranking for user: {UserId}", userId);
            var ranking = await _leaderboardService.GetUserRankingAsync(userId);

            if (ranking == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(ranking);
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