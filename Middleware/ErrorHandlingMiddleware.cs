using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

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
                            _ => "An error occurred"
                        },
                        statusCode = (int)code
                    });
                }
                else
                {
                    // More detailed errors in development
                    result = JsonSerializer.Serialize(new { 
                        message = exception.Message,
                        statusCode = (int)code,
                        type = exception.GetType().Name
                    });
                }
            }

            return context.Response.WriteAsync(result);
        }
    }
}