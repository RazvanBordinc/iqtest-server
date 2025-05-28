using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.Middleware;
using IqTest_server.Services;
using IqTest_server.Utilities;
using IqTest_server.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.IdentityModel.Tokens;
 

var builder = WebApplication.CreateBuilder(args);

// For Render deployment detection (optional)
var isRender = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER_SERVICE_ID"));

// Configure thread pool settings for Redis (prevents timeouts)
// This MUST be done early in the application startup
if (!System.Threading.ThreadPool.SetMinThreads(200, 200))
{
    Console.WriteLine("WARNING: Failed to set minimum thread pool threads");
}
else
{
    System.Threading.ThreadPool.GetMinThreads(out int workerThreads, out int completionPortThreads);
    Console.WriteLine($"Thread pool configured: Min worker threads = {workerThreads}, Min I/O threads = {completionPortThreads}");
}

// Debug environment variables on Render
if (isRender)
{
    Console.WriteLine("=== Render Environment Variables ===");
    Console.WriteLine($"RENDER_SERVICE_ID: {Environment.GetEnvironmentVariable("RENDER_SERVICE_ID")}");
    Console.WriteLine($"REDIS_URL exists: {!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDIS_URL"))}");
    Console.WriteLine($"REDIS_CONNECTION_STRING exists: {!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING"))}");
    Console.WriteLine($"UPSTASH_REDIS_URL exists: {!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("UPSTASH_REDIS_URL"))}");
    Console.WriteLine("===================================");
}

// Add environment-specific configuration 
if (isRender)
{
    builder.Configuration.AddJsonFile($"appsettings.{builder.Environment.EnvironmentName}.json", optional: true, reloadOnChange: true);
}

// Logging configuration
builder.Logging.ClearProviders();
builder.Logging.AddConsole();
builder.Logging.AddDebug();

// Configure Kestrel
builder.WebHost.ConfigureKestrel(serverOptions =>
{
    serverOptions.Limits.MaxConcurrentConnections = 100;
    serverOptions.Limits.MaxConcurrentUpgradedConnections = 100;
    serverOptions.Limits.MaxRequestBodySize = 10 * 1024 * 1024; // 10MB
});

// Controllers and JSON configuration
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.WriteIndented = true;
        options.JsonSerializerOptions.Converters.Add(new IqTest_server.Converters.AnswerValueJsonConverter());
    });

// API endpoints explorer
builder.Services.AddEndpointsApiExplorer();

// Add HttpContextAccessor
builder.Services.AddHttpContextAccessor();

// Add HttpClient for LoggingService
builder.Services.AddHttpClient();

// Add Logging Service 
builder.Services.AddSingleton<LoggingService>();

// Database configuration
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Connection string debugging for Render deployment
if (isRender || builder.Environment.IsProduction())
{
    Console.WriteLine("=====================================");
    Console.WriteLine("DATABASE CONNECTION DIAGNOSTICS");
    Console.WriteLine("=====================================");
    Console.WriteLine($"Time: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} UTC");
    Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
    Console.WriteLine($"Is Render: {isRender}");
    Console.WriteLine($"Connection string from config (masked): {MaskConnectionString(connectionString ?? string.Empty)}");
    
    // Check if environment variable is set
    var envConnString = Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection");
    Console.WriteLine($"Environment variable set: {!string.IsNullOrEmpty(envConnString)}");
    if (!string.IsNullOrEmpty(envConnString))
    {
        Console.WriteLine($"Environment connection string (masked): {MaskConnectionString(envConnString)}");
    }
    
    // For Render deployment, if no external DB is configured, warn about localhost issue
    if (connectionString?.Contains("localhost", StringComparison.OrdinalIgnoreCase) == true)
    {
        Console.WriteLine("⚠️  WARNING: Connection string uses 'localhost' which won't work on Render!");
        Console.WriteLine("💡 You need to either:");
        Console.WriteLine("   1. Set ConnectionStrings__DefaultConnection environment variable with external DB");
        Console.WriteLine("   2. Use Render's internal networking if SQL Server is also on Render");
        Console.WriteLine("   3. The app will use fallback mode for database operations");
    }
    Console.WriteLine("=====================================");
}

// Validate connection string format to catch common issues early
if (string.IsNullOrEmpty(connectionString))
{
    throw new InvalidOperationException("Database connection string 'DefaultConnection' is missing or empty.");
}

// Add basic connection parameters if missing
if (!connectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";Connect Timeout=30";
}

if (!connectionString.Contains("Pooling", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";Pooling=true;Min Pool Size=5;Max Pool Size=100";
}

// Additional connection string validation for SQL Server
try
{
    var builder_test = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    
    // Check for required components
    if (string.IsNullOrWhiteSpace(builder_test.DataSource))
    {
        throw new InvalidOperationException("Connection string is missing 'Data Source' (server address).");
    }
    
    if (string.IsNullOrWhiteSpace(builder_test.InitialCatalog))
    {
        throw new InvalidOperationException("Connection string is missing 'Initial Catalog' (database name).");
    }
    
    if (!builder_test.IntegratedSecurity && string.IsNullOrWhiteSpace(builder_test.UserID))
    {
        throw new InvalidOperationException("Connection string must specify either 'Integrated Security=true' or provide 'User ID' and 'Password'.");
    }
    
    if (!string.IsNullOrWhiteSpace(builder_test.UserID) && string.IsNullOrWhiteSpace(builder_test.Password))
    {
        Console.WriteLine("WARNING: User ID specified but Password is empty. This may cause authentication failures.");
    }
    
    // Render-specific warnings
    if (isRender)
    {
        // Log environment details for debugging
        Console.WriteLine($"RENDER_SERVICE_ID: {Environment.GetEnvironmentVariable("RENDER_SERVICE_ID")}");
        Console.WriteLine($"Database server: {builder_test.DataSource}");
        
        // Warn about localhost usage in containerized environments
        if (builder_test.DataSource.Equals("localhost", StringComparison.OrdinalIgnoreCase) || 
            builder_test.DataSource.Equals("127.0.0.1", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("WARNING: Using localhost as database server in containerized environment. This may cause connection issues if SQL Server is in a separate container.");
        }
    }
}
catch (Exception ex)
{
    Console.WriteLine($"Connection string validation error: {ex.Message}");
    throw new InvalidOperationException($"Invalid SQL Server connection string: {ex.Message}. Please check the connection string format.");
}

builder.Services.AddDbContext<ApplicationDbContext>(options =>
{
    options.UseSqlServer(
        connectionString,
        sqlServerOptions => 
        {
            sqlServerOptions.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(10),
                errorNumbersToAdd: null);
            
            // Add command timeout for long-running queries
            sqlServerOptions.CommandTimeout(20);
            
            // For Render deployment, add migration assembly
            if (Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null)
            {
                sqlServerOptions.MigrationsAssembly("IqTest-server");
            }
        }
    )
    .ConfigureWarnings(warnings => 
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
    .EnableSensitiveDataLogging(builder.Environment.IsDevelopment()) // Only in development
    .EnableDetailedErrors(builder.Environment.IsDevelopment()); // Only in development
});

 
 


// CORS configuration - MUST be configured before building the app
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        // Use SetIsOriginAllowed for dynamic origin validation
        policy.SetIsOriginAllowed(origin =>
            {
                // Allow localhost origins (development)
                if (origin.StartsWith("http://localhost:") || origin.StartsWith("https://localhost:"))
                    return true;
                
                // Allow specific production domains
                if (origin == "https://iqtest-app.vercel.app" || 
                    origin == "https://iqtest-server-project.onrender.com")
                    return true;
                
                // Allow any Vercel app (including preview deployments)
                if (origin.StartsWith("https://") && origin.EndsWith(".vercel.app"))
                    return true;
                
                // Log rejected origins for debugging
                var logger = builder.Services.BuildServiceProvider().GetService<ILogger<Program>>();
                logger?.LogWarning("CORS: Rejected origin {Origin}", origin);
                
                return false;
            })
            .AllowCredentials()
            .AllowAnyMethod()
            .AllowAnyHeader()
            .WithExposedHeaders("X-Total-Count", "X-RateLimit-Limit", "X-RateLimit-Remaining", "X-RateLimit-Reset")
            .SetPreflightMaxAge(TimeSpan.FromMinutes(10));
    });
    
    // Specific policy for health endpoints (no credentials)
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Redis configuration - simplified for Upstash
// Check environment variables and configuration in the order Render uses them
var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_URL") ?? 
                           Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ??
                           Environment.GetEnvironmentVariable("UPSTASH_REDIS_URL") ??
                           builder.Configuration["Redis:ConnectionString"] ?? 
                           "localhost:6379";

Console.WriteLine($"Redis source: {(Environment.GetEnvironmentVariable("REDIS_URL") != null ? "REDIS_URL env var" : 
                 Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") != null ? "REDIS_CONNECTION_STRING env var" :
                 Environment.GetEnvironmentVariable("UPSTASH_REDIS_URL") != null ? "UPSTASH_REDIS_URL env var" :
                 builder.Configuration["Redis:ConnectionString"] != null ? "Redis:ConnectionString config" :
                 "localhost fallback")}");

// Log the Redis configuration (truncated for security)
var displayString = redisConnectionString.Contains("@") 
    ? redisConnectionString.Substring(0, redisConnectionString.IndexOf("@") + 1) + "..."
    : redisConnectionString;
Console.WriteLine($"Redis Configuration: {displayString}");

// Ensure we use rediss:// for Upstash in production
if (builder.Environment.IsProduction() && redisConnectionString.StartsWith("redis://"))
{
    redisConnectionString = redisConnectionString.Replace("redis://", "rediss://");
    Console.WriteLine("Converted Redis URL to use SSL (rediss://) for production");
}

// Create Redis configuration options first
ConfigurationOptions redisOptions = null!;
IConnectionMultiplexer redisMultiplexer = null!;
try
{
    ConfigurationOptions options;
    
    Console.WriteLine($"=== Redis Connection Attempt ===");
    Console.WriteLine($"Environment: {builder.Environment.EnvironmentName}");
    Console.WriteLine($"Is Render: {isRender}");
    Console.WriteLine($"Redis URL: {displayString}");
    
    // Parse Upstash URL if it's in URI format
    if (redisConnectionString.StartsWith("redis://") || redisConnectionString.StartsWith("rediss://"))
    {
        var uri = new Uri(redisConnectionString);
        var userInfo = uri.UserInfo.Split(':');
        
        options = new ConfigurationOptions
        {
            EndPoints = { { uri.Host, uri.Port } },
            Password = userInfo.Length > 1 ? userInfo[1] : "",
            Ssl = uri.Scheme == "rediss",
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12 | System.Security.Authentication.SslProtocols.Tls13,
            AbortOnConnectFail = false,
            ConnectTimeout = 15000,      // 15 seconds for initial connection (increased for Upstash)
            SyncTimeout = 10000,         // 10 seconds for sync operations (increased for Upstash)
            AsyncTimeout = 10000,        // 10 seconds for async operations (increased for Upstash)
            ConnectRetry = 5,            // Increased retry attempts
            KeepAlive = 30,              // Reduced keepalive for better connection health
            ReconnectRetryPolicy = new ExponentialRetry(3000, 30000), // Exponential backoff: 3s to 30s
            ResponseTimeout = 10000,     // 10 seconds response timeout
            DefaultDatabase = 0,
            AllowAdmin = false,
            HighPrioritySocketThreads = true,
            SocketManager = SocketManager.ThreadPool
        };
        
        Console.WriteLine($"Connecting to Upstash Redis at {uri.Host}:{uri.Port} with SSL={uri.Scheme == "rediss"}");
        Console.WriteLine($"Connection timeout: {options.ConnectTimeout}ms");
    }
    else
    {
        // Standard connection string
        options = ConfigurationOptions.Parse(redisConnectionString);
        options.AbortOnConnectFail = false;
        options.ConnectTimeout = 15000;  // Increased timeout
        options.SyncTimeout = 10000;     // Increased timeout
        options.AsyncTimeout = 10000;    // Increased timeout
        options.ConnectRetry = 5;
        options.ReconnectRetryPolicy = new ExponentialRetry(3000, 30000);
        options.ResponseTimeout = 10000;
        options.HighPrioritySocketThreads = true;
        options.SocketManager = SocketManager.ThreadPool;
        
        Console.WriteLine($"Using standard Redis connection string");
    }
    
    // Store the options for reuse
    redisOptions = options;
    
    Console.WriteLine("Initiating Redis connection...");
    redisMultiplexer = ConnectionMultiplexer.Connect(options);
    Console.WriteLine("Redis ConnectionMultiplexer created successfully");
    
    // Test the connection
    var db = redisMultiplexer.GetDatabase();
    Console.WriteLine("Got Redis database instance, testing ping...");
    var pong = db.Ping();
    Console.WriteLine($"Redis ping successful: {pong.TotalMilliseconds}ms");
    Console.WriteLine($"=== Redis Connection Success ===");
}
catch (Exception ex)
{
    Console.WriteLine($"Redis connection failed: {ex.Message}");
    Console.WriteLine($"Full exception: {ex}");
    
    // Don't set to null - this will cause NullReferenceException
    // Instead, let's try a simpler connection for localhost fallback
    if (!isRender)
    {
        try
        {
            Console.WriteLine("Attempting fallback Redis connection to localhost...");
            var fallbackOptions = ConfigurationOptions.Parse("localhost:6379");
            fallbackOptions.AbortOnConnectFail = false;
            fallbackOptions.ConnectTimeout = 1000;
            redisMultiplexer = ConnectionMultiplexer.Connect(fallbackOptions);
            redisOptions = fallbackOptions; // Store for cache configuration
            Console.WriteLine("Fallback Redis connection established");
        }
        catch (Exception fallbackEx)
        {
            Console.WriteLine($"Fallback Redis connection also failed: {fallbackEx.Message}");
            // Create a null multiplexer that RedisService can handle
            redisMultiplexer = null!;
            redisOptions = null!;
        }
    }
    else
    {
        // On Render, we really need Redis to work
        redisMultiplexer = null!;
        redisOptions = null!;
    }
}

// Register the Redis multiplexer as singleton - handle null case
if (redisMultiplexer != null)
{
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp => redisMultiplexer);
}
else
{
    // Register a null instance - RedisService will handle this gracefully
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp => null!);
}

// Redis for caching and rate limiting - use the same configuration
if (redisOptions != null && redisMultiplexer != null)
{
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.ConfigurationOptions = redisOptions;
        options.InstanceName = "IqTest";
    });
}
else
{
    // Fallback to connection string if options are not available
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = redisConnectionString;
        options.InstanceName = "IqTest";
    });
}

// Add memory caching
builder.Services.AddMemoryCache();

// Services
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtHelper>();
builder.Services.AddSingleton<ICacheService, CacheService>();
builder.Services.AddScoped<AuthService>();
builder.Services.AddScoped<QuestionService>();
builder.Services.AddScoped<TestService>();
builder.Services.AddScoped<LeaderboardService>();
builder.Services.AddScoped<QuestionGeneratorService>();
builder.Services.AddScoped<AnswerValidatorService>();
builder.Services.AddScoped<RedisService>();
builder.Services.AddScoped<GithubService>();
builder.Services.AddScoped<RateLimitingService>();
builder.Services.AddScoped<ScoreCalculationService>();
builder.Services.AddScoped<ProfileService>();

// Configure Data Protection (for key persistence across deployments)
var dataProtectionPath = Path.Combine(Directory.GetCurrentDirectory(), "data-protection-keys");
Directory.CreateDirectory(dataProtectionPath);

builder.Services.AddDataProtection()
    .SetApplicationName("IqTest")
    .PersistKeysToFileSystem(new DirectoryInfo(dataProtectionPath))
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

// JWT Authentication
var jwtSettings = builder.Configuration.GetSection("Jwt");
var key = Encoding.ASCII.GetBytes(jwtSettings["Key"]);

// Configure JWT token validation to be more lenient on time validation
builder.Services.Configure<JwtSecurityTokenHandler>(options =>
{
    options.SetDefaultTimesOnTokenCreation = false;
});

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.RequireHttpsMetadata = false;
    options.SaveToken = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(key),
        ValidateIssuer = true,
        ValidIssuer = jwtSettings["Issuer"],
        ValidateAudience = true,
        ValidAudience = jwtSettings["Audience"],
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5), // Allow 5 minutes of clock skew
        RequireExpirationTime = true
    };
    
    // Better error handling
    options.Events = new JwtBearerEvents
    {
        OnAuthenticationFailed = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogWarning("Authentication failed: {Message}", context.Exception.Message);
            
            if (context.Exception.GetType() == typeof(SecurityTokenExpiredException))
            {
                context.Response.Headers.Add("Token-Expired", "true");
            }
            
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("JWT Bearer challenge occurred: {Error}", context.Error);
            
            // Skip default behavior to avoid overwriting custom error responses
            if (!context.Response.HasStarted)
            {
                context.HandleResponse();
                context.Response.StatusCode = 401;
                context.Response.ContentType = "application/json";
                return context.Response.WriteAsync(System.Text.Json.JsonSerializer.Serialize(new 
                { 
                    error = "Unauthorized", 
                    message = context.ErrorDescription ?? "Authentication required"
                }));
            }
            
            return Task.CompletedTask;
        }
    };
});

// Conditional background services based on environment
if (!builder.Environment.IsProduction() || !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("ENABLE_BACKGROUND_SERVICES")))
{
    builder.Services.AddHostedService<QuestionsRefreshService>();
}

var app = builder.Build();

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    // Use custom error handling middleware in production
    app.UseMiddleware<ErrorHandlingMiddleware>();
}

// Security headers - MUST be early in pipeline
app.UseMiddleware<SecurityHeadersMiddleware>();

// CORS - MUST be before authentication and authorization
app.UseCors();

// Fallback CORS handler - ensures CORS headers are present for all requests
app.Use(async (context, next) =>
{
    // Only add headers if they're missing
    if (!context.Response.Headers.ContainsKey("Access-Control-Allow-Origin"))
    {
        var origin = context.Request.Headers["Origin"].FirstOrDefault();
        if (!string.IsNullOrEmpty(origin))
        {
            // Validate origin
            bool isAllowed = origin.StartsWith("http://localhost:") || 
                           origin.StartsWith("https://localhost:") ||
                           origin == "https://iqtest-app.vercel.app" ||
                           origin == "https://iqtest-server-project.onrender.com" ||
                           (origin.StartsWith("https://") && origin.EndsWith(".vercel.app"));
            
            if (isAllowed)
            {
                context.Response.Headers["Access-Control-Allow-Origin"] = origin;
                context.Response.Headers["Access-Control-Allow-Credentials"] = "true";
                
                if (context.Request.Method == "OPTIONS")
                {
                    context.Response.Headers["Access-Control-Allow-Methods"] = "GET, POST, PUT, DELETE, OPTIONS";
                    context.Response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization, X-Requested-With, cache-control";
                    context.Response.Headers["Access-Control-Max-Age"] = "600";
                    context.Response.StatusCode = 200;
                    return;
                }
            }
        }
    }
    
    await next();
});

// Conditional middleware based on environment
if (builder.Environment.IsProduction())
{
    // Rate limiting middleware - only in production
    app.UseMiddleware<RateLimitingMiddleware>();
    
    // Request logging middleware - reduced in production
    app.UseWhen(context => !context.Request.Path.StartsWithSegments("/api/health"), 
        appBuilder => appBuilder.UseMiddleware<RequestLoggingMiddleware>());
}
else
{
    // Full request logging in development
    app.UseMiddleware<RequestLoggingMiddleware>();
}

// Authentication & Authorization
app.UseAuthentication();
app.UseAuthorization();

// Map controllers
app.MapControllers();

// Database initialization
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try 
    {
        // Log the actual connection string being used (masked)
        var actualConnectionString = context.Database.GetConnectionString();
        logger.LogInformation("Attempting database migration with connection string: {ConnectionString}", 
            MaskConnectionString(actualConnectionString ?? string.Empty));
        
        // Apply migrations with detailed logging
        logger.LogInformation("Starting database migration...");
        
        // Test the connection first
        var canConnect = await context.Database.CanConnectAsync();
        if (!canConnect)
        {
            logger.LogError("Cannot connect to database. Please check the connection string.");
            if (isRender)
            {
                logger.LogError("On Render: Ensure ConnectionStrings__DefaultConnection environment variable is set correctly.");
            }
        }
        else
        {
            logger.LogInformation("Database connection successful. Applying migrations...");
            await context.Database.MigrateAsync();
            logger.LogInformation("Database migration completed successfully.");
        }
    }
    catch (Microsoft.Data.SqlClient.SqlException sqlEx)
    {
        logger.LogError(sqlEx, "SQL Server connection error during migration. Error Code: {ErrorCode}", sqlEx.Number);
        
        // Provide specific guidance based on error code
        switch (sqlEx.Number)
        {
            case 18456: // Login failed
                logger.LogError("Authentication failed. Check username and password in connection string.");
                break;
            case 4060: // Cannot open database
                logger.LogError("Cannot open database. Ensure the database name in 'Initial Catalog' is correct.");
                break;
            case -2: // Timeout
                logger.LogError("Connection timeout. The server may be unreachable or slow to respond.");
                break;
            case 2: // Server not found
                logger.LogError("Server not found. Check the 'Data Source' in your connection string.");
                break;
            default:
                logger.LogError("SQL Error details: {Message}", sqlEx.Message);
                break;
        }
        
        if (isRender)
        {
            logger.LogError("Render deployment detected. Common issues:");
            logger.LogError("1. Ensure ConnectionStrings__DefaultConnection environment variable is set");
            logger.LogError("2. Database server must be accessible from Render (not localhost)");
            logger.LogError("3. Check firewall rules allow connections from Render IP addresses");
        }
        
        // Don't throw in production to allow app to start (with limited functionality)
        if (!app.Environment.IsProduction())
        {
            throw;
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to migrate database");
        
        // Don't throw in production to allow app to start (with limited functionality)
        if (!app.Environment.IsProduction())
        {
            throw;
        }
    }
}

// Ensure test types are seeded
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    
    try
    {
        // Only seed if we can connect to the database
        if (await context.Database.CanConnectAsync())
        {
            await TestTypeSeeder.SeedTestTypesAsync(context, logger);
        }
        else
        {
            logger.LogWarning("Skipping test type seeding - database is not available");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to seed test types");
        // Don't throw - allow app to continue
    }
}

// Root endpoint to avoid 404 errors on health checks
app.MapGet("/", () => Results.Ok(new 
{
    Name = "IQ Test API",
    Version = "1.0.0",
    Status = "Running",
    Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production",
    Timestamp = DateTime.UtcNow
}));

// Special endpoint for checking logging that doesn't require the full controller setup
app.Map("/api/logging-status", (ILogger<Program> logger, LoggingService loggingService) => 
{
    logger.LogInformation("Logging status checked");
    loggingService.LogInfo("Logging status endpoint accessed", new Dictionary<string, object> 
    {
        { "event", "logging_status_check" },
        { "version", "1.0.1" },
        { "timestamp", DateTime.UtcNow.ToString("o") }
    });
    
    return Results.Ok(new 
    { 
        Status = "Logging system operational",
        Version = "1.0.1", 
        Timestamp = DateTime.UtcNow,
        Environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Production"
    });
});

app.Run();

// Helper method to mask sensitive connection string data
string MaskConnectionString(string connectionString)
{
    if (string.IsNullOrEmpty(connectionString))
        return "(empty)";
    
    var builder = new System.Text.StringBuilder();
    var parts = connectionString.Split(';');
    
    foreach (var part in parts)
    {
        if (string.IsNullOrWhiteSpace(part))
            continue;
            
        var keyValue = part.Split('=', 2);
        if (keyValue.Length == 2)
        {
            var key = keyValue[0].Trim();
            var value = keyValue[1].Trim();
            
            // Mask sensitive values
            if (key.Contains("Password", StringComparison.OrdinalIgnoreCase) ||
                key.Contains("Pwd", StringComparison.OrdinalIgnoreCase))
            {
                builder.Append($"{key}=****;");
            }
            else if (key.Contains("User", StringComparison.OrdinalIgnoreCase) ||
                     key.Contains("UID", StringComparison.OrdinalIgnoreCase))
            {
                // Partially mask username
                if (value.Length > 2)
                {
                    builder.Append($"{key}={value.Substring(0, 2)}***;");
                }
                else
                {
                    builder.Append($"{key}=***;");
                }
            }
            else
            {
                builder.Append($"{key}={value};");
            }
        }
        else
        {
            builder.Append($"{part};");
        }
    }
    
    return builder.ToString().TrimEnd(';');
}

// Test type seeder helper class
public static class TestTypeSeeder
{
    public static async Task SeedTestTypesAsync(ApplicationDbContext context, ILogger logger)
    {
        try
        {
            // Check if test types already exist
            if (await context.TestTypes.AnyAsync())
            {
                logger.LogInformation("Test types already seeded");
                return;
            }

            logger.LogInformation("Seeding test types...");

            var testTypes = new[]
            {
                new TestType 
                { 
                    Id = 1, 
                    Title = "Numerical Reasoning", 
                    Description = "Test your ability to work with numbers, patterns, and mathematical concepts",
                    LongDescription = "This test evaluates your numerical reasoning skills through a series of mathematical and logical problems. You'll encounter questions involving number sequences, mathematical operations, and quantitative reasoning that assess your ability to think analytically with numbers.",
                    TimeLimit = "25 minutes",
                    QuestionsCount = 20,
                    Difficulty = "Medium",
                    TypeId = "number-logic",
                    Icon = "calculator",
                    Color = "#4F46E5"
                },
                new TestType 
                { 
                    Id = 2, 
                    Title = "Verbal Intelligence", 
                    Description = "Assess your language skills, vocabulary, and verbal reasoning abilities",
                    LongDescription = "This comprehensive verbal intelligence test measures your command of language, vocabulary depth, and ability to understand complex verbal relationships. Questions include analogies, sentence completion, and reading comprehension to evaluate your linguistic intelligence.",
                    TimeLimit = "30 minutes",
                    QuestionsCount = 20,
                    Difficulty = "Medium",
                    TypeId = "word-logic",
                    Icon = "book",
                    Color = "#059669"
                },
                new TestType 
                { 
                    Id = 3, 
                    Title = "Memory & Recall", 
                    Description = "Challenge your memory capacity and recall abilities",
                    LongDescription = "Test your short-term and working memory through various challenging exercises. This assessment includes pattern recognition, sequence memorization, and visual memory tasks designed to measure your cognitive recall abilities under timed conditions.",
                    TimeLimit = "22 minutes",
                    QuestionsCount = 15,
                    Difficulty = "Hard",
                    TypeId = "memory",
                    Icon = "brain",
                    Color = "#DC2626"
                },
                new TestType 
                { 
                    Id = 4, 
                    Title = "Comprehensive IQ", 
                    Description = "A complete assessment covering numerical, verbal, and memory skills",
                    LongDescription = "Our most thorough intelligence assessment combining elements from all cognitive domains. This comprehensive test evaluates your overall intellectual capacity through a balanced mix of numerical, verbal, spatial, and memory challenges for a complete IQ profile.",
                    TimeLimit = "45 minutes",
                    QuestionsCount = 16,
                    Difficulty = "Mixed",
                    TypeId = "mixed",
                    Icon = "lightbulb",
                    Color = "#7C3AED"
                }
            };

            context.TestTypes.AddRange(testTypes);
            await context.SaveChangesAsync();

            logger.LogInformation("Successfully seeded {Count} test types", testTypes.Length);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error seeding test types");
            throw;
        }
    }
}