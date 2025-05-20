using System;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using IqTest_server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Middleware
{
    public class RateLimitingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RateLimitingMiddleware> _logger;

        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            var endpoint = context.Request.Path.Value.ToLower();
            
            // Check if this endpoint should be rate limited
            if (ShouldRateLimit(endpoint))
            {
                var rateLimitingService = context.RequestServices.GetRequiredService<RateLimitingService>();
                var clientId = GetClientIdentifier(context);
                
                // Get rate limit configuration for the endpoint
                var (maxAttempts, window) = GetRateLimitConfig(endpoint);
                
                // Check rate limit
                var allowed = await rateLimitingService.CheckRateLimitAsync(clientId, endpoint, maxAttempts, window);
                
                if (!allowed)
                {
                    // Get rate limit status for headers
                    var status = await rateLimitingService.GetRateLimitStatusAsync(clientId, endpoint, maxAttempts, window);
                    
                    // Add rate limit headers
                    context.Response.Headers["X-RateLimit-Limit"] = maxAttempts.ToString();
                    context.Response.Headers["X-RateLimit-Remaining"] = status.AttemptsRemaining.ToString();
                    context.Response.Headers["X-RateLimit-Reset"] = status.ResetsAt.ToUnixTimeSeconds().ToString();
                    
                    // Return 429 Too Many Requests
                    context.Response.StatusCode = (int)HttpStatusCode.TooManyRequests;
                    await context.Response.WriteAsync("Rate limit exceeded. Please try again later.");
                    
                    _logger.LogWarning($"Rate limit exceeded for {clientId} on {endpoint}");
                    return;
                }
                
                // Add rate limit headers for successful requests too
                var currentStatus = await rateLimitingService.GetRateLimitStatusAsync(clientId, endpoint, maxAttempts, window);
                context.Response.Headers["X-RateLimit-Limit"] = maxAttempts.ToString();
                context.Response.Headers["X-RateLimit-Remaining"] = currentStatus.AttemptsRemaining.ToString();
                context.Response.Headers["X-RateLimit-Reset"] = currentStatus.ResetsAt.ToUnixTimeSeconds().ToString();
            }

            await _next(context);
        }

        private bool ShouldRateLimit(string endpoint)
        {
            // API groups that should be rate limited
            if (endpoint.StartsWith("/api/auth/"))
            {
                // All authentication endpoints
                return true;
            }
            
            if (endpoint.StartsWith("/api/test/"))
            {
                // All test-related endpoints
                return true;
            }
            
            if (endpoint.StartsWith("/api/leaderboard/"))
            {
                // All leaderboard endpoints
                return true;
            }
            
            if (endpoint.StartsWith("/api/profile/"))
            {
                // All profile endpoints
                return true;
            }
            
            if (endpoint.StartsWith("/api/question/"))
            {
                // Question endpoints
                return true;
            }
            
            if (endpoint.StartsWith("/api/results/"))
            {
                // Results endpoints
                return true;
            }
            
            // Special case for user data operations
            if (endpoint.StartsWith("/api/userdata/"))
            {
                return true;
            }
            
            // Default: don't rate limit other endpoints
            return false;
        }

        private (int maxAttempts, TimeSpan window) GetRateLimitConfig(string endpoint)
        {
            // Authentication endpoints
            if (endpoint.Contains("/api/auth/login") || endpoint.Contains("/api/auth/login-with-password"))
            {
                return (20, TimeSpan.FromMinutes(5)); // 20 login attempts per 5 minutes
            }
            else if (endpoint.Contains("/api/auth/register") || endpoint.Contains("/api/auth/create-user"))
            {
                return (15, TimeSpan.FromMinutes(15)); // 15 registration attempts per 15 minutes
            }
            else if (endpoint.Contains("/api/auth/check-username"))
            {
                return (40, TimeSpan.FromMinutes(1)); // 40 username checks per minute
            }
            else if (endpoint.Contains("/api/auth/refresh-token"))
            {
                return (30, TimeSpan.FromMinutes(5)); // 30 token refreshes per 5 minutes
            }
            
            // Test endpoints
            else if (endpoint.Contains("/api/test/submit"))
            {
                return (15, TimeSpan.FromMinutes(30)); // 15 test submissions per 30 minutes
            }
            else if (endpoint.Contains("/api/test/questions"))
            {
                return (30, TimeSpan.FromMinutes(10)); // 30 question requests per 10 minutes
            }
            else if (endpoint.Contains("/api/test/"))
            {
                return (60, TimeSpan.FromMinutes(5)); // 60 other test related requests per 5 minutes
            }
            
            // Leaderboard endpoints - fairly generous as these are read-only
            else if (endpoint.StartsWith("/api/leaderboard/"))
            {
                return (120, TimeSpan.FromMinutes(5)); // 120 leaderboard requests per 5 minutes
            }
            
            // Profile endpoints
            else if (endpoint.StartsWith("/api/profile/"))
            {
                return (60, TimeSpan.FromMinutes(5)); // 60 profile requests per 5 minutes
            }
            
            // Results endpoints
            else if (endpoint.StartsWith("/api/results/"))
            {
                return (60, TimeSpan.FromMinutes(5)); // 60 results requests per 5 minutes
            }
            
            // User data endpoints
            else if (endpoint.StartsWith("/api/userdata/"))
            {
                return (50, TimeSpan.FromMinutes(5)); // 50 user data requests per 5 minutes
            }
            
            // Question endpoints (except fetching questions for a test)
            else if (endpoint.StartsWith("/api/question/"))
            {
                return (60, TimeSpan.FromMinutes(5)); // 60 question operations per 5 minutes
            }
            
            // Default rate limit for all other API endpoints
            return (100, TimeSpan.FromMinutes(1)); // 100 requests per minute
        }

        private string GetClientIdentifier(HttpContext context)
        {
            // If user is authenticated, use their user ID as part of the identifier
            string userId = "anonymous";
            if (context.User?.Identity?.IsAuthenticated == true)
            {
                var userIdClaim = context.User.Claims.FirstOrDefault(c => 
                    c.Type == "id" || 
                    c.Type == "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier");
                
                if (userIdClaim != null)
                {
                    userId = userIdClaim.Value;
                }
            }
            
            // Try headers used by cloud providers and CDNs
            // Vercel and other modern hosting services
            var clientIp = context.Request.Headers["X-Vercel-Forwarded-For"].ToString();
            if (string.IsNullOrEmpty(clientIp))
            {
                clientIp = context.Request.Headers["X-Forwarded-For"].ToString();
                if (!string.IsNullOrEmpty(clientIp))
                {
                    // X-Forwarded-For may contain multiple IPs (client, proxy1, proxy2, ...)
                    // The leftmost IP is typically the client
                    var ips = clientIp.Split(',');
                    if (ips.Length > 0)
                    {
                        clientIp = ips[0].Trim();
                    }
                }
            }
            
            // Render and some other hosting services
            if (string.IsNullOrEmpty(clientIp))
            {
                clientIp = context.Request.Headers["X-Real-IP"].ToString();
            }
            
            // Cloudflare
            if (string.IsNullOrEmpty(clientIp))
            {
                clientIp = context.Request.Headers["CF-Connecting-IP"].ToString();
            }
            
            // Fall back to connection remote IP
            if (string.IsNullOrEmpty(clientIp))
            {
                clientIp = context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
            }
            
            // Determine if we have a valid IPv4 or IPv6 address
            if (clientIp != "unknown" && !System.Net.IPAddress.TryParse(clientIp, out _))
            {
                clientIp = "invalid-ip";
            }
            
            // Combine user ID and IP for more granular rate limiting
            // For authenticated users, we rate limit by user ID
            // For anonymous users, we rate limit by IP
            return userId == "anonymous" 
                ? $"ip:{clientIp}" 
                : $"user:{userId}";
        }
    }

    public static class DateTimeExtensions
    {
        public static long ToUnixTimeSeconds(this DateTime dateTime)
        {
            return ((DateTimeOffset)dateTime).ToUnixTimeSeconds();
        }
    }
}