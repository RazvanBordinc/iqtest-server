using System;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Middleware
{
    public class CsrfProtectionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<CsrfProtectionMiddleware> _logger;
        private const string CsrfTokenName = "X-CSRF-Token";
        private const string CsrfCookieName = "CSRF-TOKEN";

        public CsrfProtectionMiddleware(RequestDelegate next, ILogger<CsrfProtectionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext context)
        {
            // Generate CSRF token for GET requests
            if (context.Request.Method == HttpMethods.Get)
            {
                var token = GenerateToken();
                SetCsrfTokenCookie(context, token);
                context.Response.Headers[CsrfTokenName] = token;
            }
            // Validate CSRF token for state-changing requests
            else if (IsStateMutatingRequest(context.Request.Method))
            {
                if (!ValidateCsrfToken(context))
                {
                    _logger.LogWarning($"CSRF validation failed for {context.Request.Path}");
                    context.Response.StatusCode = 403;
                    await context.Response.WriteAsync("CSRF validation failed");
                    return;
                }
            }

            await _next(context);
        }

        private bool IsStateMutatingRequest(string method)
        {
            return method == HttpMethods.Post ||
                   method == HttpMethods.Put ||
                   method == HttpMethods.Delete ||
                   method == HttpMethods.Patch;
        }

        private string GenerateToken()
        {
            var bytes = new byte[32];
            using var rng = RandomNumberGenerator.Create();
            rng.GetBytes(bytes);
            return Convert.ToBase64String(bytes);
        }

        private void SetCsrfTokenCookie(HttpContext context, string token)
        {
            var cookieOptions = new CookieOptions
            {
                HttpOnly = false, // Must be readable by JavaScript
                Secure = true,
                SameSite = SameSiteMode.Strict,
                Path = "/",
                Expires = DateTimeOffset.UtcNow.AddHours(4)
            };

            context.Response.Cookies.Append(CsrfCookieName, token, cookieOptions);
        }

        private bool ValidateCsrfToken(HttpContext context)
        {
            // Skip CSRF validation for API endpoints (they use JWT)
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                return true;
            }

            // Get token from cookie
            var cookieToken = context.Request.Cookies[CsrfCookieName];
            if (string.IsNullOrEmpty(cookieToken))
            {
                return false;
            }

            // Get token from header or form
            var headerToken = context.Request.Headers[CsrfTokenName].ToString();
            if (string.IsNullOrEmpty(headerToken) && context.Request.HasFormContentType)
            {
                headerToken = context.Request.Form[CsrfTokenName].ToString();
            }

            // Compare tokens
            return !string.IsNullOrEmpty(headerToken) && cookieToken == headerToken;
        }
    }
}