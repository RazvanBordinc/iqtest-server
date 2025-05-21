using System;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.DTOs.Auth;
using IqTest_server.Models;
using IqTest_server.Utilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    public class AuthService
    {
        private readonly ApplicationDbContext _context;
        private readonly PasswordHasher _passwordHasher;
        private readonly JwtHelper _jwtHelper;
        private readonly ILogger<AuthService> _logger;
        private readonly IConfiguration _configuration;
        private readonly RedisService _redisService;

        public AuthService(
            ApplicationDbContext context,
            PasswordHasher passwordHasher,
            JwtHelper jwtHelper,
            ILogger<AuthService> logger,
            IConfiguration configuration,
            RedisService redisService)
        {
            _context = context;
            _passwordHasher = passwordHasher;
            _jwtHelper = jwtHelper;
            _logger = logger;
            _configuration = configuration;
            _redisService = redisService;
        }

        public async Task<bool> CheckUsernameExistsAsync(string username)
        {
            var cacheKey = $"username_exists:{username.ToLowerInvariant()}";
            
            try
            {
                // Try to get from Redis cache first
                if (_redisService != null)
                {
                    var cachedResult = await _redisService.GetAsync(cacheKey);
                    if (cachedResult != null)
                    {
                        _logger.LogDebug("Username check for {Username} served from cache", username);
                        return bool.Parse(cachedResult);
                    }
                }
                
                // Query database with timeout
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                var exists = await _context.Users.AnyAsync(u => u.Username == username, cts.Token);
                
                // Cache the result for 5 minutes
                if (_redisService != null)
                {
                    await _redisService.SetAsync(cacheKey, exists.ToString(), TimeSpan.FromMinutes(5));
                }
                
                return exists;
            }
            catch (TaskCanceledException)
            {
                _logger.LogWarning("Username check for {Username} timed out after 5 seconds", username);
                return false; // Assume username doesn't exist on timeout
            }
            catch (Microsoft.Data.SqlClient.SqlException sqlEx)
            {
                _logger.LogError(sqlEx, "SQL Server connection error checking username {Username}: {Error}", username, sqlEx.Message);
                
                // For network-related SQL errors, return false (graceful degradation)
                if (sqlEx.Message.Contains("network-related") || 
                    sqlEx.Message.Contains("server was not found") ||
                    sqlEx.Message.Contains("timeout"))
                {
                    _logger.LogWarning("Database connection issue - assuming username {Username} doesn't exist for safety", username);
                    return false;
                }
                
                // For other SQL errors, rethrow
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking username existence for {Username}", username);
                
                // For connection string errors and other critical DB issues, throw to be handled by controller
                if (ex.Message.Contains("Keyword not supported") || 
                    ex.InnerException?.Message?.Contains("Keyword not supported") == true)
                {
                    throw; // Let controller handle this specific error
                }
                
                // For other database errors, return false (assume username doesn't exist)
                _logger.LogWarning("Assuming username doesn't exist due to database error");
                return false;
            }
        }

        public async Task<(bool Success, string Message, UserDto User)> CreateUserAsync(CreateUserDto model)
        {
            try 
            {
                // Add logging to track execution
                _logger.LogInformation("Starting user creation process for {Username}", model.Username);
                
                // Check if username already exists
                if (await _context.Users.AnyAsync(u => u.Username == model.Username))
                {
                    _logger.LogWarning("Username {Username} already exists, returning error", model.Username);
                    return (false, "Username already taken", null);
                }

                // Generate a placeholder email for internal use only
                var generatedEmail = $"{model.Username.ToLower()}@iqtest.local";
                _logger.LogDebug("Generated email: {Email} for user {Username}", generatedEmail, model.Username);
                
                // Create new user
                var user = new User
                {
                    Username = model.Username,
                    Email = generatedEmail, // Using placeholder email for internal use
                    Age = model.Age,
                    Country = model.Country,
                    PasswordHash = _passwordHasher.HashPassword(model.Password),
                    CreatedAt = DateTime.UtcNow
                };

                // Generate refresh token
                user.RefreshToken = _jwtHelper.GenerateRefreshToken();
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                // Log DB operations with try-catch
                try
                {
                    _logger.LogDebug("Adding new user {Username} to database", model.Username);
                    _context.Users.Add(user);
                    
                    _logger.LogDebug("Saving user {Username} to database", model.Username);
                    await _context.SaveChangesAsync();
                    _logger.LogInformation("User {Username} saved to database with ID {UserId}", model.Username, user.Id);
                }
                catch (Exception dbEx)
                {
                    // Log detailed info about database error
                    if (dbEx.InnerException != null)
                    {
                        _logger.LogError(dbEx, "Database inner exception during user creation: {Message}", dbEx.InnerException.Message);
                    }
                    throw; // Rethrow to be caught by outer handler
                }

                // Invalidate the total users count cache - handle Redis errors gracefully
                try
                {
                    await _redisService.RemoveAsync("total_users_count");
                }
                catch (Exception cacheEx)
                {
                    // Log but don't fail if Redis cache operation fails
                    _logger.LogWarning(cacheEx, "Failed to invalidate Redis cache during user creation");
                }

                // Generate access token
                var token = _jwtHelper.GenerateAccessToken(user);
                _logger.LogDebug("Generated JWT token for user {Username}", model.Username);

                // Return user data and token
                return (true, "User created successfully", new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Age = user.Age,
                    Country = user.Country,
                    Token = token
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user creation for username: {Username}. Exception type: {ExceptionType}, Message: {Message}", 
                    model.Username, ex.GetType().Name, ex.Message);
                
                // Log inner exception details if available
                if (ex.InnerException != null)
                {
                    _logger.LogError("Inner exception: Type: {InnerExceptionType}, Message: {InnerMessage}", 
                        ex.InnerException.GetType().Name, ex.InnerException.Message);
                }
                
                // Add more detail about specific database error types
                if (ex.GetType().Name.Contains("DbUpdateException"))
                {
                    _logger.LogError("Database update exception: {Message}", ex.InnerException?.Message ?? ex.Message);
                    return (false, $"Database error: {ex.InnerException?.Message ?? ex.Message}", null);
                }
                
                // Check for connection string specific errors
                if (ex.Message.Contains("Keyword not supported") || ex.InnerException?.Message?.Contains("Keyword not supported") == true)
                {
                    var keywordMessage = ex.InnerException?.Message ?? ex.Message;
                    _logger.LogError("Connection string format error: {Message}", keywordMessage);
                    return (false, "Database configuration error. Service temporarily unavailable.", null);
                }
                
                // Check for SQL Server specific errors
                if (ex.GetType().Name.Contains("SqlException") || ex.InnerException?.GetType().Name.Contains("SqlException") == true)
                {
                    var sqlMessage = ex.InnerException?.Message ?? ex.Message;
                    _logger.LogError("SQL Server exception: {SqlMessage}", sqlMessage);
                    
                    // Check for specific SQL error patterns
                    if (sqlMessage.Contains("Cannot open database") || sqlMessage.Contains("Login failed"))
                    {
                        return (false, "Database authentication error. Service temporarily unavailable.", null);
                    }
                    if (sqlMessage.Contains("timeout") || sqlMessage.Contains("Timeout"))
                    {
                        return (false, "Database timeout error. Please try again.", null);
                    }
                    if (sqlMessage.Contains("network") || sqlMessage.Contains("connection"))
                    {
                        return (false, "Database connection error. Please try again later.", null);
                    }
                    
                    return (false, $"Database error: {sqlMessage}", null);
                }
                
                // Check for connection errors
                if (ex.Message.Contains("connection") || ex.InnerException?.Message?.Contains("connection") == true)
                {
                    return (false, "Database connection error. Please try again later.", null);
                }
                
                // Return the actual error message for debugging in non-production
                var errorMessage = ex.InnerException?.Message ?? ex.Message;
                return (false, $"Server error: {errorMessage}", null);
            }
        }

        public async Task<(bool Success, string Message, UserDto User)> RegisterAsync(RegisterRequestDto model)
        {
            try
            {
                // Check if username already exists
                if (await _context.Users.AnyAsync(u => u.Username == model.Username))
                {
                    return (false, "Username already taken", null);
                }

                // Generate a placeholder email for internal use only
                var generatedEmail = $"{model.Username.ToLower()}@iqtest.local";
                
                // Create new user
                var user = new User
                {
                    Username = model.Username,
                    Email = generatedEmail, // Using placeholder email for internal use
                    Country = model.Country,
                    Age = model.Age,
                    PasswordHash = _passwordHasher.HashPassword(model.Password),
                    CreatedAt = DateTime.UtcNow
                };

                // Generate refresh token
                user.RefreshToken = _jwtHelper.GenerateRefreshToken();
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                // Invalidate the total users count cache
                await _redisService.RemoveAsync("total_users_count");

                // Generate access token
                var token = _jwtHelper.GenerateAccessToken(user);

                // Return user data and token
                return (true, "Registration successful", new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Token = token
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user registration");
                return (false, "Registration failed due to a server error", null);
            }
        }

        public async Task<(bool Success, string Message, UserDto User, string RefreshToken)> LoginAsync(LoginRequestDto model)
        {
            try
            {
                // Find user by username instead of email
                var user = await _context.Users.SingleOrDefaultAsync(u => u.Username == model.Username);
                if (user == null)
                {
                    return (false, "Invalid credentials", null, null);
                }

                // Verify password
                if (!_passwordHasher.VerifyPassword(user.PasswordHash, model.Password))
                {
                    _logger.LogWarning("Failed login attempt for user: {Username}", model.Username);
                    return (false, "Invalid credentials", null, null);
                }

                // Update last login time
                user.LastLoginAt = DateTime.UtcNow;

                // Generate new refresh token
                user.RefreshToken = _jwtHelper.GenerateRefreshToken();
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);

                await _context.SaveChangesAsync();

                // Generate access token
                var token = _jwtHelper.GenerateAccessToken(user);

                return (true, "Login successful", new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Token = token,
                    Country = user.Country,
                    Age = user.Age
                }, user.RefreshToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during user login");
                return (false, "Login failed due to a server error", null, null);
            }
        }

        public async Task<(bool Success, string Message, UserDto User, string NewRefreshToken)> RefreshTokenAsync(string accessToken, string refreshToken)
        {
            try
            {
                var principal = _jwtHelper.GetPrincipalFromExpiredToken(accessToken);
                var userId = int.Parse(principal.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value);

                var user = await _context.Users.SingleOrDefaultAsync(u => u.Id == userId);
                if (user == null || user.RefreshToken != refreshToken || user.RefreshTokenExpiryTime <= DateTime.UtcNow)
                {
                    return (false, "Invalid refresh token", null, null);
                }

                // Generate new tokens
                var newAccessToken = _jwtHelper.GenerateAccessToken(user);
                var newRefreshToken = _jwtHelper.GenerateRefreshToken();

                // Update refresh token in database
                user.RefreshToken = newRefreshToken;
                user.RefreshTokenExpiryTime = DateTime.UtcNow.AddDays(7);
                await _context.SaveChangesAsync();

                return (true, "Token refreshed successfully", new UserDto
                {
                    Id = user.Id,
                    Username = user.Username,
                    Email = user.Email,
                    Token = newAccessToken,
                    Country = user.Country,
                    Age = user.Age
                }, newRefreshToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token refresh");
                return (false, "Token refresh failed due to a server error", null, null);
            }
        }

        public async Task<bool> RevokeTokenAsync(int userId)
        {
            try
            {
                var user = await _context.Users.FindAsync(userId);
                if (user == null)
                {
                    return false;
                }

                user.RefreshToken = null;
                user.RefreshTokenExpiryTime = null;
                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during token revocation for user: {UserId}", userId);
                return false;
            }
        }
    }
}