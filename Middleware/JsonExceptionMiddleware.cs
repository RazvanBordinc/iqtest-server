using System;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Middleware
{
    public class JsonExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<JsonExceptionMiddleware> _logger;

        public JsonExceptionMiddleware(RequestDelegate next, ILogger<JsonExceptionMiddleware> logger)
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
            catch (JsonException jsonEx)
            {
                _logger.LogError(jsonEx, "JSON parsing error occurred");
                
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                
                var response = new
                {
                    message = "Invalid JSON format",
                    error = jsonEx.Message,
                    type = "JsonParsingError"
                };
                
                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
            catch (Exception ex) when (ex.InnerException is JsonException innerJsonEx)
            {
                _logger.LogError(innerJsonEx, "Nested JSON parsing error occurred");
                
                context.Response.StatusCode = 400;
                context.Response.ContentType = "application/json";
                
                var response = new
                {
                    message = "Invalid JSON format",
                    error = innerJsonEx.Message,
                    type = "NestedJsonParsingError"
                };
                
                await context.Response.WriteAsync(JsonSerializer.Serialize(response));
            }
        }
    }
}