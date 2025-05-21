using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using IqTest_server.Data;
using System;
using System.Threading.Tasks;

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
        public async Task<IActionResult> Wake([FromServices] ApplicationDbContext context)
        {
            var uptime = DateTime.UtcNow - _startTime;
            var wakeTime = DateTime.UtcNow;
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            // Test database connectivity
            bool dbConnected = false;
            string dbStatus = "Unknown";
            
            try
            {
                // Quick database connectivity test with timeout
                using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));
                await context.Database.ExecuteSqlRawAsync("SELECT 1", cts.Token);
                dbConnected = true;
                dbStatus = "Connected";
            }
            catch (Exception ex)
            {
                dbConnected = false;
                dbStatus = ex.Message.Contains("network-related") || ex.Message.Contains("server was not found") 
                    ? "Network Error" : "Error";
                    
                _logger.LogWarning("Database connectivity test failed during wake: {Error}", ex.Message);
            }
            
            // This endpoint is specifically for waking up the server
            return Ok(new { 
                Status = "Awake",
                WakeTime = wakeTime,
                IsColdStart = uptime.TotalMinutes < 2,
                DatabaseConnected = dbConnected,
                DatabaseStatus = dbStatus,
                UptimeMinutes = Math.Round(uptime.TotalMinutes, 1),
                Message = uptime.TotalMinutes < 2 ? "Server was sleeping, now awake!" : "Server was already active"
            });
        }
    }
}