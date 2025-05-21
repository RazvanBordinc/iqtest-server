using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Middleware
{
    public class DynamicCorsMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<DynamicCorsMiddleware> _logger;
        private readonly string[] _allowedOrigins;

        public DynamicCorsMiddleware(RequestDelegate next, ILogger<DynamicCorsMiddleware> logger)
        {
            _next = next;
            _logger = logger;
            
            // List of origins that can use credentials
            // Important: This must match the WithOrigins list in Program.cs
            _allowedOrigins = new[]
            {
                "http://localhost:3000",
                "https://localhost:3000",
                "http://frontend:3000",
                "http://host.docker.internal:3000",
                "https://iqtest-app.vercel.app"
            };
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Get origin from request headers
            var origin = context.Request.Headers["Origin"].FirstOrDefault();
            
            if (!string.IsNullOrEmpty(origin))
            {
                // Check if this origin is in our list of origins that can use credentials
                var isAllowedOrigin = false;
                
                foreach (var allowedOrigin in _allowedOrigins)
                {
                    // Direct match
                    if (string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase))
                    {
                        isAllowedOrigin = true;
                        break;
                    }
                    
                    // Vercel wildcard match
                    if (allowedOrigin == "https://*.vercel.app" && 
                        origin.EndsWith(".vercel.app", StringComparison.OrdinalIgnoreCase) &&
                        origin.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                    {
                        isAllowedOrigin = true;
                        break;
                    }
                }
                
                // Add Allow-Credentials header only for specified origins
                if (isAllowedOrigin)
                {
                    context.Response.Headers.Add("Access-Control-Allow-Credentials", "true");
                    _logger.LogInformation("Using CORS with credentials for origin: {Origin}", origin);
                }
                else
                {
                    // Tell the browser not to send cookies
                    _logger.LogInformation("Using CORS without credentials for origin: {Origin}", origin);
                }
                
                // Add Access-Control-Allow-Origin header with the actual origin, regardless of whether it's allowed or not
                // The browser will check if the origin matches the Access-Control-Allow-Origin header
                context.Response.Headers.Add("Access-Control-Allow-Origin", origin);
            }

            // Continue processing
            await _next(context);
        }
    }
}