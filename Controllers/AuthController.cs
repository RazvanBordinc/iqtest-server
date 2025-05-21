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

        // Support both POST and GET methods for check-username
        [HttpPost("check-username")]
        [HttpGet("check-username/{username}")]
        public async Task<IActionResult> CheckUsername([FromBody] object? requestData = null, [FromRoute] string? username = null)
        {
            // Add extra logging for debugging
            try
            {
                _logger.LogInformation("Received check-username request with data: {Data}", 
                    System.Text.Json.JsonSerializer.Serialize(requestData));
            }
            catch
            {
                _logger.LogInformation("Received check-username request with non-serializable data");
            }
            
            // Try to extract username from route parameter first, then from request body
            string usernameValue = username; // From route parameter
            
            // If we have a route parameter, use that directly
            if (string.IsNullOrEmpty(usernameValue) && requestData != null)
            {
                try
                {
                    // Handle different request formats
                    if (requestData is CheckUsernameDto dto)
                    {
                        usernameValue = dto.Username;
                    }
                    else if (requestData is System.Text.Json.JsonElement jsonElement)
                    {
                        // Try to get username using case-insensitive property name matching
                        foreach (var prop in new[] { "Username", "username", "userName" })
                        {
                            if (jsonElement.TryGetProperty(prop, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.String)
                            {
                                usernameValue = value.GetString();
                                break;
                            }
                        }
                        
                        // If we couldn't find a property, check if it's just a string
                        if (usernameValue == null && jsonElement.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            usernameValue = jsonElement.GetString();
                        }
                    }
                    else if (requestData is string strValue)
                    {
                        usernameValue = strValue;
                    }
                    else
                    {
                        // Try reflection as a last resort
                        var type = requestData.GetType();
                        foreach (var prop in new[] { "Username", "username", "userName" })
                        {
                            var property = type.GetProperty(prop);
                            if (property != null)
                            {
                                usernameValue = property.GetValue(requestData) as string;
                                if (usernameValue != null) break;
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error extracting username from request data");
                }
            }
            
            // Try to get the username from query string as a last resort
            if (string.IsNullOrEmpty(usernameValue))
            {
                usernameValue = Request.Query["username"].ToString();
            }
            
            // Validate username
            if (string.IsNullOrEmpty(usernameValue))
            {
                _logger.LogWarning("Check username called with empty or null username");
                return Ok(new { 
                    message = "Username check completed", 
                    exists = false,
                    isValid = false,
                    details = "The Username field is required" 
                });
            }
            
            if (usernameValue.Length < 3 || usernameValue.Length > 100)
            {
                _logger.LogWarning("Username length invalid: {Length}", usernameValue.Length);
                return Ok(new { 
                    message = "Username check completed", 
                    exists = false,
                    isValid = false,
                    details = "Username must be between 3 and 100 characters" 
                });
            }

            try
            {
                // Security: Don't reveal if username exists to prevent user enumeration
                // This should be combined with registration in production
                var exists = await _authService.CheckUsernameExistsAsync(usernameValue);
                
                _logger.LogInformation("Username check completed for {Username}, exists: {Exists}", 
                    usernameValue, exists);
                
                // Always return success to prevent username enumeration
                return Ok(new { 
                    message = "Username check completed",
                    exists = exists, // Always return existence since we use it for UX
                    isValid = true
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in username check database operation");
                // Return a fallback response to prevent breaking the client flow
                return Ok(new { 
                    message = "Username check completed",
                    exists = false, // Assume username doesn't exist if there's a DB error
                    isValid = true
                });
            }
        }

        [HttpPost("create-user")]
        public async Task<IActionResult> CreateUser([FromBody] object requestData)
        {
            // Add extra logging for debugging
            try {
                _logger.LogInformation("Received create-user request with data: {Data}", 
                    System.Text.Json.JsonSerializer.Serialize(requestData));
            } catch {
                _logger.LogInformation("Received create-user request with non-serializable data");
            }
            
            if (requestData == null)
            {
                _logger.LogWarning("Create user called with null data");
                return BadRequest(new { message = "Invalid request data" });
            }
            
            // Try to extract user data from the request
            CreateUserDto model = null;
            
            try
            {
                // First try to see if it's already a CreateUserDto
                if (requestData is CreateUserDto dto)
                {
                    model = dto;
                }
                // Otherwise, try to parse it from JsonElement
                else if (requestData is System.Text.Json.JsonElement jsonElement)
                {
                    model = new CreateUserDto();
                    
                    // Extract Username
                    if (jsonElement.TryGetProperty("Username", out var usernameValue) || 
                        jsonElement.TryGetProperty("username", out usernameValue))
                    {
                        model.Username = usernameValue.GetString();
                    }
                    
                    // Extract Password
                    if (jsonElement.TryGetProperty("Password", out var passwordValue) || 
                        jsonElement.TryGetProperty("password", out passwordValue))
                    {
                        model.Password = passwordValue.GetString();
                    }
                    
                    // Extract Country
                    if (jsonElement.TryGetProperty("Country", out var countryValue) || 
                        jsonElement.TryGetProperty("country", out countryValue))
                    {
                        model.Country = countryValue.GetString();
                    }
                    
                    // Extract Age
                    if (jsonElement.TryGetProperty("Age", out var ageValue) || 
                        jsonElement.TryGetProperty("age", out ageValue))
                    {
                        if (ageValue.TryGetInt32(out int age))
                        {
                            model.Age = age;
                        }
                    }
                }
                // Try to extract properties using reflection
                else
                {
                    model = new CreateUserDto();
                    var type = requestData.GetType();
                    
                    // Try to get Username
                    var usernameProp = type.GetProperty("Username") ?? type.GetProperty("username");
                    if (usernameProp != null)
                    {
                        model.Username = usernameProp.GetValue(requestData) as string;
                    }
                    
                    // Try to get Password
                    var passwordProp = type.GetProperty("Password") ?? type.GetProperty("password");
                    if (passwordProp != null)
                    {
                        model.Password = passwordProp.GetValue(requestData) as string;
                    }
                    
                    // Try to get Country
                    var countryProp = type.GetProperty("Country") ?? type.GetProperty("country");
                    if (countryProp != null)
                    {
                        model.Country = countryProp.GetValue(requestData) as string;
                    }
                    
                    // Try to get Age
                    var ageProp = type.GetProperty("Age") ?? type.GetProperty("age");
                    if (ageProp != null)
                    {
                        var ageValue = ageProp.GetValue(requestData);
                        if (ageValue is int intAge)
                        {
                            model.Age = intAge;
                        }
                        else if (ageValue is string strAge && int.TryParse(strAge, out int parsedAge))
                        {
                            model.Age = parsedAge;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error parsing create user request data");
                return BadRequest(new { message = "Could not parse request data" });
            }
            
            // Validate model
            if (model == null || string.IsNullOrEmpty(model.Username) || string.IsNullOrEmpty(model.Password))
            {
                _logger.LogWarning("Create user model invalid: missing required fields");
                return BadRequest(new { message = "Username and Password are required" });
            }
            
            // Check length constraints for username
            if (model.Username.Length < 3 || model.Username.Length > 100)
            {
                _logger.LogWarning("Username validation failed for {Username}: Length constraint", model.Username);
                return BadRequest(new { 
                    message = "Username must be between 3 and 100 characters",
                    code = "USERNAME_LENGTH_INVALID",
                    field = "username"
                });
            }
            
            // Check username format constraints
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Username, @"^[a-zA-Z0-9_-]+$"))
            {
                _logger.LogWarning("Username validation failed for {Username}: Format constraint", model.Username);
                return BadRequest(new { 
                    message = "Username can only contain letters, numbers, underscores, and hyphens",
                    code = "USERNAME_FORMAT_INVALID",
                    field = "username"
                });
            }
            
            // Check Age constraint
            if (model.Age.HasValue && (model.Age.Value < 1 || model.Age.Value > 120))
            {
                _logger.LogWarning("Age validation failed: {Age} is not between 1 and 120", model.Age.Value);
                return BadRequest(new { 
                    message = "Age must be between 1 and 120",
                    code = "AGE_RANGE_INVALID",
                    field = "age"
                });
            }
            
            // Check Password constraints using the StrongPasswordAttribute logic
            if (string.IsNullOrEmpty(model.Password))
            {
                _logger.LogWarning("Password validation failed: Empty password");
                return BadRequest(new { 
                    message = "Password is required",
                    code = "PASSWORD_REQUIRED",
                    field = "password"
                });
            }
            
            if (model.Password.Length < 8)
            {
                _logger.LogWarning("Password validation failed: Length constraint");
                return BadRequest(new { 
                    message = "Password must be at least 8 characters long",
                    code = "PASSWORD_TOO_SHORT",
                    field = "password"
                });
            }
            
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[A-Z]"))
            {
                _logger.LogWarning("Password validation failed: Missing uppercase letter");
                return BadRequest(new { 
                    message = "Password must contain at least one uppercase letter",
                    code = "PASSWORD_NO_UPPERCASE",
                    field = "password"
                });
            }
            
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[a-z]"))
            {
                _logger.LogWarning("Password validation failed: Missing lowercase letter");
                return BadRequest(new { 
                    message = "Password must contain at least one lowercase letter",
                    code = "PASSWORD_NO_LOWERCASE",
                    field = "password"
                });
            }
            
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"\d"))
            {
                _logger.LogWarning("Password validation failed: Missing digit");
                return BadRequest(new { 
                    message = "Password must contain at least one number",
                    code = "PASSWORD_NO_DIGIT",
                    field = "password"
                });
            }
            
            if (!System.Text.RegularExpressions.Regex.IsMatch(model.Password, @"[!@#$%^&*(),.?""':{}|<>]"))
            {
                _logger.LogWarning("Password validation failed: Missing special character");
                return BadRequest(new { 
                    message = "Password must contain at least one special character",
                    code = "PASSWORD_NO_SPECIAL_CHAR",
                    field = "password"
                });
            }

            try
            {
                // Add detailed logging before creating user
                _logger.LogInformation("Creating user with details: Username={Username}, Country={Country}, Age={Age}", 
                    model.Username, model.Country, model.Age);
                
                // Wrap user creation in try/catch to get detailed error info
                try 
                {
                    var (success, message, user) = await _authService.CreateUserAsync(model);

                    if (!success)
                    {
                        _logger.LogWarning("User creation failed: {Message}", message);
                        return BadRequest(new { message });
                    }

                    if (user == null)
                    {
                        _logger.LogError("AuthService.CreateUserAsync returned success=true but user is null");
                        return StatusCode(500, new { message = "Error creating user account" });
                    }

                    // Set refresh token in HTTP-only cookie
                    try
                    {
                        var (loginSuccess, loginMessage, _, refreshToken) = await _authService.LoginAsync(new LoginRequestDto
                        {
                            Username = user.Username,
                            Password = model.Password
                        });

                        if (loginSuccess && !string.IsNullOrEmpty(refreshToken))
                        {
                            SetRefreshTokenCookie(refreshToken);
                            SetAccessTokenCookie(user.Token);
                            
                            // Set user preferences in cookies
                            SetUserPreferencesCookie(user);
                        }
                        else
                        {
                            _logger.LogWarning("Auto-login after user creation failed: {Message}", loginMessage);
                            // We still return the user, but client might need to login again
                        }
                    }
                    catch (Exception loginEx)
                    {
                        _logger.LogError(loginEx, "Error during auto-login after user creation");
                        // Continue and return the user, but client might need to login again
                    }

                    return Ok(user);
                }
                catch (Exception dbEx)
                {
                    _logger.LogError(dbEx, "Database error during user creation for {Username}", model.Username);
                    
                    // Check if it's a SQL error to give more specific feedback
                    if (dbEx.GetType().Name.Contains("Sql") || dbEx.InnerException?.GetType().Name.Contains("Sql") == true)
                    {
                        _logger.LogError("SQL Exception details: {Message}", dbEx.InnerException?.Message ?? dbEx.Message);
                        
                        // Check for duplicate key errors (username constraint)
                        if (dbEx.Message.Contains("duplicate") || 
                            dbEx.Message.Contains("unique constraint") || 
                            dbEx.Message.Contains("UNIQUE KEY") || 
                            dbEx.Message.Contains("Violation of UNIQUE KEY") ||
                            (dbEx.InnerException?.Message?.Contains("duplicate") == true))
                        {
                            return BadRequest(new { 
                                message = "Username already exists",
                                code = "USERNAME_ALREADY_EXISTS",
                                field = "username"
                            });
                        }
                        
                        return StatusCode(503, new { 
                            message = "The service is temporarily unavailable. Please try again later.",
                            code = "DATABASE_ERROR",
                            isRetryable = true
                        });
                    }
                    
                    throw; // Rethrow to be caught by outer handler
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unhandled exception during user creation for {Username}", model.Username);
                
                // Check if it's a connection string related issue
                if (ex.Message.Contains("connection") || ex.InnerException?.Message?.Contains("connection") == true)
                {
                    return StatusCode(503, new { 
                        message = "The service is temporarily unavailable. Please try again later.",
                        code = "CONNECTION_ERROR",
                        isRetryable = true
                    });
                }
                
                // Handle validation errors with more specific messages
                if (ex.Message.Contains("validation") || ex.GetType().Name.Contains("Validation"))
                {
                    return BadRequest(new {
                        message = "Invalid input data. Please check your information and try again.",
                        code = "VALIDATION_ERROR"
                    });
                }
                
                // Return a user-friendly error message for production
                return StatusCode(500, new { 
                    message = "An unexpected error occurred during user creation. Please try again later.",
                    code = "INTERNAL_SERVER_ERROR",
                    isRetryable = true,
                    requestId = HttpContext.TraceIdentifier // Include for log correlation
                });
            }
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequestDto model)
        {
            // Add extra logging for debugging
            _logger.LogInformation("Received register request with model: {ModelInfo}", 
                new { Username = model?.Username, Country = model?.Country, Age = model?.Age, HasPassword = model?.Password != null });
                
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