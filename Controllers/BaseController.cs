using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Cors;
using Microsoft.Extensions.Logging;
using System.Linq;

namespace IqTest_server.Controllers
{
    [EnableCors("AllowedOrigins")]
    public class BaseController : ControllerBase
    {
        protected readonly ILogger _logger;

        public BaseController(ILogger logger)
        {
            _logger = logger;
        }

        protected int GetUserId()
        {
            // First try the standard .NET claim type (unmapped)
            var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);

            // If not found, try the "sub" claim (in case mapping is still active)
            if (userIdClaim == null)
            {
                userIdClaim = User.FindFirst("sub");
            }

            if (userIdClaim != null && int.TryParse(userIdClaim.Value, out int userId))
            {
                _logger.LogInformation("Found user ID: {UserId} from claim type: {ClaimType}",
                    userId, userIdClaim?.Type);
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