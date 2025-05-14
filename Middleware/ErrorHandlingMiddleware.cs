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
            _logger.LogError(exception, "An unhandled exception occurred");

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