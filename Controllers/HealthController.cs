using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.EntityFrameworkCore;
using IqTest_server.Data;
using IqTest_server.Services;
using System;
using System.Threading.Tasks;
using StackExchange.Redis;

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
        public async Task<IActionResult> Wake(
            [FromServices] ApplicationDbContext context,
            [FromServices] AuthService authService,
            [FromServices] TestService testService)
        {
            var uptime = DateTime.UtcNow - _startTime;
            var isColdStart = uptime.TotalSeconds < 30;
            
            // Add special CORS headers directly for health endpoint
            Response.Headers["Access-Control-Allow-Origin"] = "*";
            Response.Headers["Cache-Control"] = "no-cache, no-store, must-revalidate";
            
            // Warm up critical services in parallel for faster subsequent requests
            if (isColdStart)
            {
                var warmupTasks = new[]
                {
                    // Warm up EF Core model
                    Task.Run(async () => 
                    {
                        try 
                        { 
                            await context.Database.ExecuteSqlRawAsync("SELECT 1");
                        } 
                        catch { /* Ignore errors during warmup */ }
                    }),
                    
                    // Warm up test types
                    Task.Run(async () => 
                    {
                        try 
                        { 
                            await testService.GetAllTestTypesAsync();
                        } 
                        catch { /* Ignore errors during warmup */ }
                    })
                };
                
                // Wait max 2 seconds for warmup tasks
                await Task.WhenAny(
                    Task.WhenAll(warmupTasks),
                    Task.Delay(2000)
                );
            }
            
            // This endpoint is specifically for waking up the server
            return Ok(new { 
                Status = "Awake",
                WakeTime = DateTime.UtcNow,
                IsColdStart = isColdStart,
                Message = isColdStart ? "Server was sleeping, now awake!" : "Server was already active"
            });
        }
        
        // Redis health check endpoint
        [HttpGet("redis")]
        [AllowAnonymous]
        [Microsoft.AspNetCore.Cors.EnableCors("AllowAll")]
        public async Task<IActionResult> RedisHealth([FromServices] IConnectionMultiplexer redis)
        {
            try
            {
                if (redis == null)
                {
                    return Ok(new
                    {
                        Status = "Unavailable",
                        Message = "Redis connection not configured"
                    });
                }
                
                var db = redis.GetDatabase();
                var ping = await db.PingAsync();
                
                return Ok(new
                {
                    Status = "Connected",
                    Latency = ping.TotalMilliseconds,
                    IsConnected = redis.IsConnected,
                    Configuration = redis.Configuration
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Redis health check failed");
                return Ok(new
                {
                    Status = "Error",
                    Message = ex.Message
                });
            }
        }
    }
}