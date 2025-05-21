using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;

namespace IqTest_server.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class HealthController : BaseController
    {
        private static DateTime _startTime = DateTime.UtcNow;
        
        public HealthController(ILogger<HealthController> logger)
            : base(logger)
        {
        }

        [HttpGet]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        public IActionResult Get()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var isColdStart = uptime.TotalMinutes < 2; // Consider it a cold start if uptime < 2 minutes
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            return Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Uptime = uptime.ToString(),
                UptimeMinutes = Math.Round(uptime.TotalMinutes, 1),
                IsColdStart = isColdStart,
                IsRender = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null,
                Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
            });
        }
        
        // This endpoint is specifically for checking if the server is running
        // It's used by the frontend to determine if the backend is active
        [HttpGet("ping")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        public IActionResult Ping()
        {
            var uptime = DateTime.UtcNow - _startTime;
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            return Ok(new { 
                Status = "OK",
                Timestamp = DateTime.UtcNow,
                IsColdStart = uptime.TotalMinutes < 2,
                ResponseTime = DateTime.UtcNow.ToString("HH:mm:ss.fff")
            });
        }
        
        [HttpGet("wake")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        public IActionResult Wake()
        {
            var uptime = DateTime.UtcNow - _startTime;
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            // This endpoint is specifically for waking up the server
            return Ok(new { 
                Status = "Awake",
                WakeTime = DateTime.UtcNow,
                IsColdStart = uptime.TotalMinutes < 2,
                Message = uptime.TotalMinutes < 2 ? "Server was sleeping, now awake!" : "Server was already active"
            });
        }
    }
}