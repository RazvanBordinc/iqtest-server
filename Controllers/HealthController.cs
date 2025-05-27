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
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Get()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var isColdStart = uptime.TotalSeconds < 30; // Consider it a cold start if uptime < 30 seconds
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            
            return Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                IsColdStart = isColdStart
            });
        }
        
        // This endpoint is specifically for checking if the server is running
        // It's used by the frontend to determine if the backend is active
        [HttpGet("ping")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Ping()
        {
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            
            return Ok(new { 
                Status = "OK",
                Timestamp = DateTime.UtcNow
            });
        }
        
        [HttpGet("wake")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Wake()
        {
            var uptime = DateTime.UtcNow - _startTime;
            var isColdStart = uptime.TotalSeconds < 30;
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            
            // Skip database connectivity test for faster response
            // This endpoint is specifically for waking up the server
            return Ok(new { 
                Status = "Awake",
                WakeTime = DateTime.UtcNow,
                IsColdStart = isColdStart,
                Message = isColdStart ? "Server was sleeping, now awake!" : "Server was already active"
            });
        }
    }
}