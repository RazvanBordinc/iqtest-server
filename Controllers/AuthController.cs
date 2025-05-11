using System;
using System.Threading.Tasks;
using IqTest_server.DTOs.Auth;
using IqTest_server.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly AuthService _authService;
        private readonly ILogger<AuthController> _logger;

        public AuthController(AuthService authService, ILogger<AuthController> logger)
        {
            _authService = authService;
            _logger = logger;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var (success, message, user) = await _authService.RegisterAsync(model);

            if (!success)
            {
                _logger.LogWarning("Registration failed: {Message}", message);
                return BadRequest(new { message });
            }

            // Set refresh token in HTTP-only cookie
            if (user != null)
            {
                var (_, _, _, refreshToken) = await _authService.LoginAsync(new LoginRequestDto
                {
                    Email = model.Email,
                    Password = model.Password
                });

                SetRefreshTokenCookie(refreshToken);
            }

            return Ok(user);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            if (!ModelState.IsValid)
            {
                return BadRequest(ModelState);
            }

            var (success, message, user, refreshToken) = await _authService.LoginAsync(model);

            if (!success)
            {
                _logger.LogWarning("Login failed: {Message}", message);
                return BadRequest(new { message });
            }

            // Set refresh token in HTTP-only cookie
            SetRefreshTokenCookie(refreshToken);

            // ALSO set the access token in a cookie
            SetAccessTokenCookie(user.Token);

            return Ok(user);
        }

       

        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            // Get the refresh token from the cookie
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest(new { message = "Refresh token is required" });
            }

            // Get the access token from the Authorization header
            var accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            if (string.IsNullOrEmpty(accessToken))
            {
                return BadRequest(new { message = "Access token is required" });
            }

            var (success, message, user, newRefreshToken) = await _authService.RefreshTokenAsync(accessToken, refreshToken);

            if (!success)
            {
                _logger.LogWarning("Token refresh failed: {Message}", message);
                return BadRequest(new { message });
            }

            // Set the new refresh token in the cookie
            SetRefreshTokenCookie(newRefreshToken);

            return Ok(user);
        }

        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout()
        {
            var userId = GetUserId();

            if (userId <= 0)
            {
                return BadRequest(new { message = "User not authenticated" });
            }

            var success = await _authService.RevokeTokenAsync(userId);

            if (success)
            {
                // Clear the refresh token cookie
                Response.Cookies.Delete("refreshToken");
                return Ok(new { message = "Logged out successfully" });
            }

            return BadRequest(new { message = "Failed to logout" });
        }

        private void SetAccessTokenCookie(string accessToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false, // Allow JavaScript to read this cookie
                Expires = DateTime.UtcNow.AddMinutes(15), // Same as token expiry
                SameSite = SameSiteMode.None,
                Secure = false, // Set to true in production with HTTPS
                Path = "/"
            };

            Response.Cookies.Append("token", accessToken, cookieOptions);
        }

        private void SetRefreshTokenCookie(string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = true,
                Expires = DateTime.UtcNow.AddDays(7),
                SameSite = SameSiteMode.None,
                Secure = false, // Set to true in production with HTTPS
                Path = "/"
            };

            Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
        }
        private int GetUserId()
        {
            // Use the exact claim type that's in the token
            var userIdClaim = User.FindFirst("http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");

            if (userIdClaim != null)
            {
                _logger.LogInformation("Found user ID claim: {ClaimValue}", userIdClaim.Value);

                if (int.TryParse(userIdClaim.Value, out int userId))
                {
                    return userId;
                }
            }

            // Try alternative claim types
            userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
            if (userIdClaim != null)
            {
                _logger.LogInformation("Found alt user ID claim: {ClaimValue}", userIdClaim.Value);

                if (int.TryParse(userIdClaim.Value, out int userId))
                {
                    return userId;
                }
            }

            _logger.LogWarning("No valid user ID claim found. Available claims: {Claims}",
                string.Join(", ", User.Claims.Select(c => $"{c.Type}={c.Value}")));

            return 0;
        }
    }
}