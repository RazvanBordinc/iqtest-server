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
        private HttpContext context; // Add field for context

        public RateLimitingMiddleware(RequestDelegate next, ILogger<RateLimitingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            this.context = context; // Store context for use in other methods
            
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
            // Health endpoint should never be rate limited
            if (endpoint.Contains("/api/health"))
            {
                return false;
            }
            
            // API groups that should be rate limited
            if (endpoint.StartsWith("/api/auth/"))
            {
                // Authentication endpoints that should NOT be rate limited
                if (endpoint.Contains("/check-username") || 
                    endpoint.Contains("/create-user") || 
                    endpoint.Contains("/register"))
                {
                    return false;
                }
                // All other authentication endpoints
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
            // Check for direct backend access header
            var isDirectBackendAccess = context?.Request?.Headers.ContainsKey("X-Direct-Backend-Fallback") == true;
            
            // For direct backend access (fallback mode), use higher limits
            int multiplier = isDirectBackendAccess ? 100 : 50;
            
            // Health endpoint should never be rate limited
            if (endpoint.Contains("/api/health"))
            {
                return (50000, TimeSpan.FromMinutes(1)); // 50000 health checks per minute
            }
            
            // Authentication endpoints
            if (endpoint.Contains("/api/auth/login") || endpoint.Contains("/api/auth/login-with-password"))
            {
                return (2000 * multiplier, TimeSpan.FromMinutes(10)); // 20000-40000 login attempts per 10 minutes
            }
            else if (endpoint.Contains("/api/auth/register") || endpoint.Contains("/api/auth/create-user"))
            {
                return (1000 * multiplier, TimeSpan.FromMinutes(10)); // 10000-20000 registration attempts per 10 minutes
            }
            else if (endpoint.Contains("/api/auth/check-username"))
            {
                return (5000 * multiplier, TimeSpan.FromMinutes(1)); // 50000-100000 username checks per minute
            }
            else if (endpoint.Contains("/api/auth/refresh-token"))
            {
                return (3000 * multiplier, TimeSpan.FromMinutes(5)); // 30000-60000 token refreshes per 5 minutes
            }
            
            // Test endpoints
            else if (endpoint.Contains("/api/test/submit"))
            {
                return (1000 * multiplier, TimeSpan.FromMinutes(10)); // 10000-20000 test submissions per 10 minutes
            }
            else if (endpoint.Contains("/api/test/questions"))
            {
                return (5000 * multiplier, TimeSpan.FromMinutes(5)); // 50000-100000 question requests per 5 minutes
            }
            else if (endpoint.Contains("/api/test/types"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 test types requests per minute
            }
            else if (endpoint.Contains("/api/test/"))
            {
                return (5000 * multiplier, TimeSpan.FromMinutes(1)); // 50000-100000 other test related requests per minute
            }
            
            // Leaderboard endpoints - very generous as these are read-only
            else if (endpoint.StartsWith("/api/leaderboard/"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 leaderboard requests per minute
            }
            
            // Profile endpoints
            else if (endpoint.StartsWith("/api/profile/"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 profile requests per minute
            }
            
            // Results endpoints
            else if (endpoint.StartsWith("/api/results/"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 results requests per minute
            }
            
            // User data endpoints
            else if (endpoint.StartsWith("/api/userdata/"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 user data requests per minute
            }
            
            // Question endpoints (except fetching questions for a test)
            else if (endpoint.StartsWith("/api/question/"))
            {
                return (10000 * multiplier, TimeSpan.FromMinutes(1)); // 100000-200000 question operations per minute
            }
            
            // Default rate limit for all other API endpoints
            return (20000 * multiplier, TimeSpan.FromMinutes(1)); // 200000-400000 requests per minute
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