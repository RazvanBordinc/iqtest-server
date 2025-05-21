using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using IqTest_server.Data;
using IqTest_server.Services;

namespace IqTest_server.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class UserDataController : BaseController
    {
        private readonly ApplicationDbContext _context;
        private readonly new ILogger<UserDataController> _logger;

        public UserDataController(
            ApplicationDbContext context,
            ILogger<UserDataController> logger) : base(logger)
        {
            _context = context;
            _logger = logger;
        }

        // GDPR: Data Access Request
        [HttpGet("export")]
        public async Task<IActionResult> ExportUserData()
        {
            try
            {
                var userId = GetUserId();
                var user = await _context.Users
                    .Include(u => u.TestResults)
                    .Include(u => u.LeaderboardEntries)
                    .FirstOrDefaultAsync(u => u.Id == userId);

                if (user == null)
                {
                    return NotFound("User not found");
                }

                var userData = new
                {
                    PersonalData = new
                    {
                        user.Username,
                        user.Email,
                        user.Country,
                        user.Age,
                        user.CreatedAt,
                        user.LastLoginAt
                    },
                    TestHistory = user.TestResults.Select(tr => new
                    {
                        tr.TestType.Title,
                        tr.Score,
                        tr.IQScore,
                        tr.Duration,
                        tr.CompletedAt
                    }),
                    LeaderboardEntries = user.LeaderboardEntries.Select(le => new
                    {
                        TestType = le.TestType.Title,
                        le.Score,
                        le.IQScore,
                        TestDuration = le.BestTime,
                        CreatedAt = le.LastUpdated
                    })
                };

                return Ok(userData);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error exporting user data");
                return StatusCode(500, "An error occurred while exporting data");
            }
        }

    }
}