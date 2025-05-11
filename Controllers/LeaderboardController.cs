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
    public class LeaderboardController : BaseController
    {
        private readonly LeaderboardService _leaderboardService;

        public LeaderboardController(LeaderboardService leaderboardService, ILogger<LeaderboardController> logger)
            : base(logger)
        {
            _leaderboardService = leaderboardService;
        }

        [HttpGet("global")]
        public async Task<IActionResult> GetGlobalLeaderboard([FromQuery] int limit = 10)
        {
            var authHeader = Request.Headers["Authorization"].FirstOrDefault();

            var leaderboard = await _leaderboardService.GetGlobalLeaderboardAsync(limit);
            return Ok(leaderboard);
        }

        [HttpGet("test-type/{testTypeId}")]
        public async Task<IActionResult> GetTestTypeLeaderboard(string testTypeId, [FromQuery] int limit = 10)
        {
            var leaderboard = await _leaderboardService.GetTestTypeLeaderboardAsync(testTypeId, limit);
            return Ok(leaderboard);
        }

        [HttpGet("user-ranking")]
        public async Task<IActionResult> GetUserRanking()
        {
            var userId = GetUserId();

            _logger.LogInformation("Getting user ranking - User ID: {UserId}", userId);

            if (userId <= 0)
            {
                _logger.LogWarning("User not authenticated - User ID: {UserId}", userId);
                return Unauthorized(new { message = "User not authenticated" });
            }

            var ranking = await _leaderboardService.GetUserRankingAsync(userId);

            if (ranking == null)
            {
                return NotFound(new { message = "User not found" });
            }

            return Ok(ranking);
        }
    }
}