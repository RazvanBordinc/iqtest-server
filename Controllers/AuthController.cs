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
    public class AuthController : BaseController
    {
        private readonly AuthService _authService;
        private readonly IHostEnvironment _env;

        public AuthController(AuthService authService, ILogger<AuthController> logger, IHostEnvironment env)
            : base(logger)
        {
            _authService = authService;
            _env = env;
        }

        [HttpPost("check-username")]
        public async Task<IActionResult> CheckUsername([FromBody] CheckUsernameDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received check-username request with model: {Model}", model);
            
            if (model == null || string.IsNullOrEmpty(model.Username))
            {
                _logger.LogWarning("Check username called with empty or null model");
                return BadRequest(new { message = "The Username field is required" });
            }
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Check username model invalid: {ModelState}", ModelState);
                return BadRequest(ModelState);
            }

            // Security: Don't reveal if username exists to prevent user enumeration
            // This should be combined with registration in production
            var exists = await _authService.CheckUsernameExistsAsync(model.Username);
            
            _logger.LogInformation("Username check completed for {Username}, exists: {Exists}", 
                model.Username, exists);
            
            // Always return success to prevent username enumeration
            return Ok(new { 
                message = "Username check completed",
                // Only reveal existence in development for testing
                exists = _env.IsDevelopment() ? (bool?)exists : null
            });
        }

        [HttpPost("create-user")]
        public async Task<IActionResult> CreateUser([FromBody] CreateUserDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received create-user request with model: {ModelInfo}", 
                new { Username = model?.Username, HasPassword = model?.Password != null });
                
            if (model == null)
            {
                _logger.LogWarning("Create user called with null model");
                return BadRequest(new { message = "Invalid request data" });
            }
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Create user model invalid: {ModelState}", ModelState);
                return BadRequest(ModelState);
            }

            var (success, message, user) = await _authService.CreateUserAsync(model);

            if (!success)
            {
                _logger.LogWarning("User creation failed: {Message}", message);
                return BadRequest(new { message });
            }

            // Set refresh token in HTTP-only cookie
            if (user != null)
            {
                var (_, _, _, refreshToken) = await _authService.LoginAsync(new LoginRequestDto
                {
                    Username = user.Username,
                    Password = model.Password
                });

                SetRefreshTokenCookie(refreshToken);
                SetAccessTokenCookie(user.Token);
                
                // Set user preferences in cookies
                SetUserPreferencesCookie(user);
            }

            return Ok(user);
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received register request with model: {ModelInfo}", 
                new { Username = model?.Username, Email = model?.Email, HasPassword = model?.Password != null });
                
            if (model == null)
            {
                _logger.LogWarning("Register called with null model");
                return BadRequest(new { message = "Invalid request data" });
            }
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Register model invalid: {ModelState}", ModelState);
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
                    Username = model.Username,
                    Password = model.Password
                });

                SetRefreshTokenCookie(refreshToken);
                SetAccessTokenCookie(user.Token);
                SetUserPreferencesCookie(user);
            }

            return Ok(user);
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequestDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received login request with model: {ModelInfo}", 
                new { Username = model?.Username, HasPassword = model?.Password != null });
                
            if (model == null)
            {
                _logger.LogWarning("Login called with null model");
                return BadRequest(new { message = "Invalid request data" });
            }
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Login model invalid: {ModelState}", ModelState);
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
            SetAccessTokenCookie(user.Token);
            SetUserPreferencesCookie(user);

            return Ok(user);
        }

        [HttpPost("login-with-password")]
        public async Task<IActionResult> LoginWithPassword([FromBody] LoginRequestDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received login-with-password request with model: {ModelInfo}", 
                new { Username = model?.Username, HasPassword = model?.Password != null });
                
            if (model == null)
            {
                _logger.LogWarning("Login with password called with null model");
                return BadRequest(new { message = "Invalid request data" });
            }
            
            if (!ModelState.IsValid)
            {
                _logger.LogWarning("Login with password model invalid: {ModelState}", ModelState);
                return BadRequest(ModelState);
            }

            var (success, message, user, refreshToken) = await _authService.LoginAsync(model);

            if (!success)
            {
                _logger.LogWarning("Login failed: {Message}", message);
                return BadRequest(new { message });
            }

            // Set tokens and preferences in cookies
            SetRefreshTokenCookie(refreshToken);
            SetAccessTokenCookie(user.Token);
            SetUserPreferencesCookie(user);

            return Ok(user);
        }
        
        [HttpPost("refresh-token")]
        public async Task<IActionResult> RefreshToken()
        {
            // Try to get refresh token from cookie first
            var refreshToken = Request.Cookies["refreshToken"];
            if (string.IsNullOrEmpty(refreshToken))
            {
                return BadRequest(new { message = "Refresh token is required" });
            }

            // Try to get access token from cookie or Authorization header
            var accessToken = Request.Cookies["token"];
            if (string.IsNullOrEmpty(accessToken))
            {
                accessToken = Request.Headers["Authorization"].ToString().Replace("Bearer ", "");
            }

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
            // Also set the new access token
            SetAccessTokenCookie(user.Token);

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
                // Clear both cookies
                Response.Cookies.Delete("refreshToken", new CookieOptions
                {
                    SameSite = SameSiteMode.None,
                    Secure = !_env.IsDevelopment(),
                    Path = "/"
                });
                Response.Cookies.Delete("token", new CookieOptions
                {
                    SameSite = SameSiteMode.None,
                    Secure = !_env.IsDevelopment(),
                    Path = "/"
                });
                return Ok(new { message = "Logged out successfully" });
            }

            return BadRequest(new { message = "Failed to logout" });
        }

        private void SetAccessTokenCookie(string accessToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false, // Allow JavaScript to read the token
                Expires = DateTime.UtcNow.AddMinutes(15), // Same as token expiry
                SameSite = _env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
                Secure = !_env.IsDevelopment(), // HTTPS only in production
                Path = "/",
                // IMPORTANT: Do NOT set Domain for container environments
            };

            Response.Cookies.Append("token", accessToken, cookieOptions);
        }

        private void SetRefreshTokenCookie(string refreshToken)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false, // Allow JavaScript to read the token
                Expires = DateTime.UtcNow.AddDays(7),
                SameSite = _env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
                Secure = !_env.IsDevelopment(), // HTTPS only in production
                Path = "/",
                // IMPORTANT: Do NOT set Domain for container environments
            };

            Response.Cookies.Append("refreshToken", refreshToken, cookieOptions);
        }

        private void SetUserPreferencesCookie(UserDto user)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false, // Allow JavaScript to read these preferences
                Expires = DateTime.UtcNow.AddDays(30), // Long-lived preferences
                SameSite = _env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
                Secure = !_env.IsDevelopment() || true, // Always secure for production, optional for dev
                Path = "/",
            };

            Response.Cookies.Append("username", user.Username, cookieOptions);
            if (user.Age.HasValue)
                Response.Cookies.Append("age", user.Age.Value.ToString(), cookieOptions);
            if (!string.IsNullOrEmpty(user.Country))
                Response.Cookies.Append("country", user.Country, cookieOptions);
        }

        [HttpPost("disconnect")]
        public IActionResult Disconnect()
        {
            var cookieOptions = new CookieOptions
            {
                SameSite = _env.IsDevelopment() ? SameSiteMode.Lax : SameSiteMode.None,
                Secure = !_env.IsDevelopment() || true,
                Path = "/"
            };

            // Clear all cookies
            Response.Cookies.Delete("refreshToken", cookieOptions);
            Response.Cookies.Delete("token", cookieOptions);
            Response.Cookies.Delete("username", cookieOptions);
            Response.Cookies.Delete("age", cookieOptions);
            Response.Cookies.Delete("country", cookieOptions); // Changed from gender to country

            return Ok(new { message = "Disconnected successfully" });
        }
    }
}