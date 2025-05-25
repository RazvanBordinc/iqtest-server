using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using StackExchange.Redis;

namespace IqTest_server.Middleware
{
    public class ErrorHandlingMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<ErrorHandlingMiddleware> _logger;

        public ErrorHandlingMiddleware(RequestDelegate next, ILogger<ErrorHandlingMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            try
            {
                await _next(context);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(context, ex);
            }
        }

        private Task HandleExceptionAsync(HttpContext context, Exception exception)
        {
            // Capture request details for better debugging
            var requestPath = context.Request.Path;
            var method = context.Request.Method;
            var contentType = context.Request.ContentType;
            var isAuth = context.User?.Identity?.IsAuthenticated ?? false;
            
            _logger.LogError(exception, 
                "An unhandled exception occurred during {Method} request to {Path} with ContentType={ContentType}, IsAuthenticated={IsAuth}", 
                method, requestPath, contentType, isAuth);

            var code = HttpStatusCode.InternalServerError; // 500 if unexpected
            var result = string.Empty;
            var isRenderEnvironment = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null;

            switch (exception)
            {
                case ArgumentException _:
                    code = HttpStatusCode.BadRequest;
                    break;
                case UnauthorizedAccessException _:
                    code = HttpStatusCode.Unauthorized;
                    break;
                case KeyNotFoundException _:
                    code = HttpStatusCode.NotFound;
                    break;
                case TaskCanceledException _:
                case OperationCanceledException _:
                    // Request was cancelled, likely due to timeout
                    code = HttpStatusCode.RequestTimeout;
                    _logger.LogWarning("Request cancelled/timed out for {Path}", requestPath);
                    break;
                case SqlException sqlEx:
                    // Handle SQL connection errors specially on Render
                    if (isRenderEnvironment && (sqlEx.Message.Contains("network-related") || 
                        sqlEx.Message.Contains("server was not found") ||
                        sqlEx.Number == -2)) // Timeout
                    {
                        code = HttpStatusCode.ServiceUnavailable;
                        _logger.LogWarning("SQL connection error on Render (cold start): {Message}", sqlEx.Message);
                    }
                    break;
                case DbUpdateException dbEx when dbEx.InnerException is SqlException:
                    // Handle EF Core SQL exceptions
                    var innerSqlEx = dbEx.InnerException as SqlException;
                    if (isRenderEnvironment && innerSqlEx != null && 
                        (innerSqlEx.Message.Contains("network-related") || innerSqlEx.Number == -2))
                    {
                        code = HttpStatusCode.ServiceUnavailable;
                        _logger.LogWarning("Database connection error on Render: {Message}", innerSqlEx.Message);
                    }
                    break;
                case RedisConnectionException redisEx:
                    // Log Redis errors but don't fail the request
                    _logger.LogWarning("Redis connection error (non-critical): {Message}", redisEx.Message);
                    // Don't change the status code, let it continue as 500
                    break;
                case RedisTimeoutException redisTimeout:
                    // Log Redis timeout but don't fail the request
                    _logger.LogWarning("Redis timeout (non-critical): {Message}", redisTimeout.Message);
                    // Don't change the status code, let it continue as 500
                    break;
            }

            context.Response.ContentType = "application/json";
            context.Response.StatusCode = (int)code;

            // Security: Don't expose internal error details to clients
            var isProduction = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") == "Production";
            
            // Add special error handling for auth endpoints
            if (context.Request.Path.StartsWithSegments("/api/auth"))
            {
                _logger.LogError("Authentication error: {Message} at {Path} with method {Method}", 
                    exception.Message, context.Request.Path, context.Request.Method);
                
                // For auth-specific errors, provide better messages
                if (exception is System.Text.Json.JsonException jsonEx)
                {
                    // JSON parsing errors are common in auth endpoints
                    _logger.LogError(jsonEx, "JSON parsing error in auth endpoint");
                    code = HttpStatusCode.BadRequest;
                    result = JsonSerializer.Serialize(new { 
                        message = isProduction ? "Invalid request format" : jsonEx.Message,
                        statusCode = (int)code 
                    });
                    return context.Response.WriteAsync(result);
                }
            }
            
            if (string.IsNullOrEmpty(result))
            {
                if (isProduction)
                {
                    // Generic error messages in production
                    result = JsonSerializer.Serialize(new { 
                        message = code switch
                        {
                            HttpStatusCode.BadRequest => "Invalid request",
                            HttpStatusCode.Unauthorized => "Authentication required",
                            HttpStatusCode.NotFound => "Resource not found",
                            HttpStatusCode.RequestTimeout => "Request timed out. The server may be waking up, please try again.",
                            HttpStatusCode.ServiceUnavailable => "Service temporarily unavailable. Please wait a moment and try again.",
                            _ => "An error occurred"
                        },
                        statusCode = (int)code,
                        isServerSleep = code == HttpStatusCode.ServiceUnavailable && isRenderEnvironment,
                        retryAfter = code == HttpStatusCode.ServiceUnavailable ? 30 : (int?)null
                    });
                }
                else
                {
                    // More detailed errors in development
                    result = JsonSerializer.Serialize(new { 
                        message = exception.Message,
                        statusCode = (int)code,
                        type = exception.GetType().Name,
                        isServerSleep = code == HttpStatusCode.ServiceUnavailable && isRenderEnvironment,
                        retryAfter = code == HttpStatusCode.ServiceUnavailable ? 30 : (int?)null
                    });
                }
            }

            return context.Response.WriteAsync(result);
        }
    }
}