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
            return await _context.Users.AnyAsync(u => u.Username == username);
        }

        public async Task<(bool Success, string Message, UserDto User)> CreateUserAsync(CreateUserDto model)
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
                    Age = model.Age,
                    Country = model.Country,
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
                _logger.LogError(ex, "Error during user creation");
                return (false, "User creation failed due to a server error", null);
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