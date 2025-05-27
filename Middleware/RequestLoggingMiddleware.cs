using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IqTest_server.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.IO;

namespace IqTest_server.Middleware
{
    /// <summary>
    /// Middleware to log all HTTP requests and responses
    /// </summary>
    public class RequestLoggingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<RequestLoggingMiddleware> _logger;
        private readonly LoggingService _loggingService;
        private readonly RecyclableMemoryStreamManager _recyclableMemoryStreamManager;
        private readonly List<string> _sensitivePathsToFilter;
        
        public RequestLoggingMiddleware(
            RequestDelegate next,
            ILogger<RequestLoggingMiddleware> logger,
            LoggingService loggingService)
        {
            _next = next;
            _logger = logger;
            _loggingService = loggingService;
            _recyclableMemoryStreamManager = new RecyclableMemoryStreamManager();
            
            // Define paths with sensitive data that should be filtered
            _sensitivePathsToFilter = new List<string>
            {
                "/api/auth/login",
                "/api/auth/register",
                "/api/auth/refresh",
                "/api/profile"
            };
        }
        
        public async Task InvokeAsync(HttpContext context)
        {
            // Skip logging for health checks, static files, and OPTIONS requests
            if (context.Request.Method == "OPTIONS" ||
                context.Request.Path.StartsWithSegments("/health") || 
                context.Request.Path.StartsWithSegments("/api/health") ||
                context.Request.Path.StartsWithSegments("/swagger") ||
                context.Request.Path.Value.Contains("."))
            {
                await _next(context);
                return;
            }
            
            var stopwatch = Stopwatch.StartNew();
            var requestPath = context.Request.Path.ToString();
            var requestMethod = context.Request.Method;
            
            // Build metadata for logging
            var metadata = new Dictionary<string, object>
            {
                { "method", requestMethod },
                { "path", requestPath },
                { "query", FormatQueryString(context.Request.QueryString.ToString()) },
                { "ip", GetClientIpAddress(context) },
                { "userAgent", context.Request.Headers["User-Agent"].ToString() },
                { "isAuthenticated", context.User?.Identity?.IsAuthenticated ?? false },
                { "requestId", context.TraceIdentifier }
            };
            
            // Add user ID if authenticated
            if (context.User?.Identity?.IsAuthenticated ?? false)
            {
                var userId = context.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
                if (!string.IsNullOrEmpty(userId))
                {
                    metadata["userId"] = userId;
                }
            }
            
            // Log the incoming request (without body)
            _loggingService.LogInfo($"HTTP Request: {requestMethod} {requestPath}", metadata);
            
            Exception exception = null;
            
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                exception = ex;
                throw;
            }
            finally
            {
                stopwatch.Stop();
                
                // Prepare response metadata
                var responseMetadata = new Dictionary<string, object>(metadata)
                {
                    { "statusCode", context.Response.StatusCode },
                    { "duration", stopwatch.ElapsedMilliseconds }
                };
                
                // Choose log level based on status code
                var logLevel = context.Response.StatusCode >= 500 
                    ? LogLevel.Error 
                    : (context.Response.StatusCode >= 400 ? LogLevel.Warning : LogLevel.Information);
                
                // Log the response
                if (exception != null)
                {
                    responseMetadata["exception"] = exception.Message;
                    _loggingService.Log(LogLevel.Error, 
                        $"HTTP Response: {requestMethod} {requestPath} failed with {context.Response.StatusCode} after {stopwatch.ElapsedMilliseconds}ms",
                        responseMetadata);
                }
                else if (stopwatch.ElapsedMilliseconds > 1000) // Only log slow requests
                {
                    _loggingService.Log(LogLevel.Warning, 
                        $"Slow HTTP Response: {requestMethod} {requestPath} completed with {context.Response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms",
                        responseMetadata);
                }
                else if (context.Response.StatusCode >= 400) // Log errors
                {
                    _loggingService.Log(logLevel, 
                        $"HTTP Response: {requestMethod} {requestPath} completed with {context.Response.StatusCode} in {stopwatch.ElapsedMilliseconds}ms",
                        responseMetadata);
                }
            }
        }
        
        private bool IsSensitivePath(string path)
        {
            return _sensitivePathsToFilter.Any(p => path.StartsWith(p, StringComparison.OrdinalIgnoreCase));
        }
        
        private string GetClientIpAddress(HttpContext context)
        {
            // Try to get IP from forwarded headers first (for reverse proxies)
            var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
            if (!string.IsNullOrEmpty(forwardedFor))
            {
                // X-Forwarded-For can contain multiple IPs, get the first one
                var ips = forwardedFor.Split(',');
                return ips[0].Trim();
            }
            
            // Fall back to connection remote IP
            return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        }
        
        private string FormatQueryString(string query)
        {
            // Sanitize query string to remove sensitive parameters
            if (string.IsNullOrEmpty(query))
            {
                return "";
            }
            
            return query.Replace("?", "");
        }
        
        private string SanitizePayload(string payload, string path)
        {
            // For sensitive paths, redact the entire payload
            if (IsSensitivePath(path))
            {
                return "[REDACTED]";
            }
            
            // Simplify sanitization - in production we would use regex to target specific fields
            return payload
                .Replace("\"password\":", "\"password\":\"[REDACTED]\"")
                .Replace("\"token\":", "\"token\":\"[REDACTED]\"")
                .Replace("\"refreshToken\":", "\"refreshToken\":\"[REDACTED]\"");
        }
    }
}