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
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            return Ok(new
            {
                Status = "Healthy",
                Timestamp = DateTime.UtcNow,
                Uptime = uptime.ToString(),
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
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            
            return Ok(new { Status = "OK" });
        }
    }
}