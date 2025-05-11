using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IqTest_server.Controllers
{
    public class BaseController : ControllerBase
    {
        protected readonly ILogger _logger;

        public BaseController(ILogger logger)
        {
            _logger = logger;
        }

        protected int GetUserId()
        {
            // First try the standard .NET claim type
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                return userId;
            }

            // Log available claims for debugging
            var claims = User.Claims.Select(c => $"{c.Type}={c.Value}").ToList();
            _logger.LogWarning("No valid user ID claim found. Available claims: {Claims}",
                string.Join(", ", claims));

            return 0;
        }
    }
}