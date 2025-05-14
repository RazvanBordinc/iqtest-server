using System;
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
            // Define which endpoints should be rate limited
            return endpoint.Contains("/api/auth/login") ||
                   endpoint.Contains("/api/auth/register") ||
                   endpoint.Contains("/api/auth/create-user") ||
                   endpoint.Contains("/api/auth/check-username") ||
                   endpoint.Contains("/api/auth/login-with-password") ||
                   endpoint.Contains("/api/auth/refresh-token") ||
                   endpoint.Contains("/api/test/submit");
        }

        private (int maxAttempts, TimeSpan window) GetRateLimitConfig(string endpoint)
        {
            // Different rate limits for different endpoints
            if (endpoint.Contains("/api/auth/login") || endpoint.Contains("/api/auth/login-with-password"))
            {
                return (15, TimeSpan.FromMinutes(5)); // 15 attempts per 5 minutes
            }
            else if (endpoint.Contains("/api/auth/register") || endpoint.Contains("/api/auth/create-user"))
            {
                return (10, TimeSpan.FromMinutes(15)); // 10 attempts per 15 minutes
            }
            else if (endpoint.Contains("/api/auth/check-username"))
            {
                return (30, TimeSpan.FromMinutes(1)); // 30 attempts per minute
            }
            else if (endpoint.Contains("/api/auth/refresh-token"))
            {
                return (20, TimeSpan.FromMinutes(5)); // 20 attempts per 5 minutes
            }
            else if (endpoint.Contains("/api/test/submit"))
            {
                return (10, TimeSpan.FromMinutes(30)); // 10 test submissions per 30 minutes
            }
            
            // Default rate limit
            return (60, TimeSpan.FromMinutes(1)); // 60 requests per minute
        }

        private string GetClientIdentifier(HttpContext context)
        {
            // Try to get IP from X-Forwarded-For header first (for proxies)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].ToString();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                var ips = forwardedFor.Split(',');
                if (ips.Length > 0)
                {
                    return ips[0].Trim();
                }
            }

            // Try X-Real-IP header
            var realIp = context.Request.Headers["X-Real-IP"].ToString();
            if (!string.IsNullOrEmpty(realIp))
            {
                return realIp;
            }

            // Fall back to connection remote IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
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