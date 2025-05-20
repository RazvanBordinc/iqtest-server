using System;
using System.IdentityModel.Tokens.Jwt;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.Middleware;
using IqTest_server.Services;
using IqTest_server.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.IdentityModel.Tokens;
 

var builder = WebApplication.CreateBuilder(args);

// CRITICAL: Disable default claim mapping
JwtSecurityTokenHandler.DefaultMapInboundClaims = false;

// Add services to the container
builder.Services.AddLogging(logging =>
{
    logging.ClearProviders();
    logging.AddConsole();
    logging.AddDebug();
});

// Configure Data Protection with in-memory keys for free tier
// This approach keeps keys in memory, which means they'll be regenerated on service restart
// It's not ideal for production, but works for a free tier with no persistent disk
if (builder.Environment.IsDevelopment())
{
    // In development, still use file system storage
    var keysDirectory = new DirectoryInfo("/mnt/c/Users/razva/OneDrive/Desktop/projects/iqtest/data-protection-keys");
    builder.Services.AddDataProtection()
        .PersistKeysToFileSystem(keysDirectory)
        .SetDefaultKeyLifetime(TimeSpan.FromDays(90));
}
else
{
    // In production (free tier), use default key storage with extended lifetime
    builder.Services.AddDataProtection()
        .SetDefaultKeyLifetime(TimeSpan.FromDays(365)); // Longer key lifetime to reduce key rotation frequency
}

// Database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()
    )
    .ConfigureWarnings(warnings => 
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
);

 
 


// CORS policy with specific origins including cloud platforms
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins(
                // Local development
                "http://localhost:3000",
                "https://localhost:3000",
                
                // Docker development
                "http://frontend:3000",
                "http://host.docker.internal:3000",
                
                // Vercel deployment - add your actual domains here
                "https://*.vercel.app",        // All Vercel preview deployments
                "https://iqtest-app.vercel.app"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();  // CRITICAL for cookies
    });
});

// Redis for caching and rate limiting - use the same options we'll create later
// Note: We'll create detailed options later, so just set a placeholder here 
// that will be overridden when we create the connection multiplexer
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = "localhost:6379"; // This will be overridden by the connection multiplexer
    options.InstanceName = "IqTest";
    // The actual Redis configuration is handled by the IConnectionMultiplexer registration
});

// Add Redis connection multiplexer with resilient configuration
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

// Create Redis configuration options programmatically instead of parsing
var redisOptions = new ConfigurationOptions
{
    AbortOnConnectFail = false, // Don't fail if Redis is temporarily unavailable
    ConnectRetry = 5, // Number of times to retry connecting
    ConnectTimeout = 10000, // Connection timeout in milliseconds
    Password = "", // Will be set below if present in connection string
    Ssl = false, // Will be set to true for Upstash Redis
    SyncTimeout = 10000 // Increase sync timeout for cloud-hosted Redis
};

// Create a logger for Redis configuration
// Use a simple console logger to avoid calling BuildServiceProvider
var redisLogger = LoggerFactory.Create(logging => 
{
    logging.AddConsole();
    logging.AddDebug();
}).CreateLogger("Redis");

// Parse the Redis connection string manually
try
{
    if (!string.IsNullOrEmpty(redisConnectionString))
    {
        if (redisConnectionString.Contains("@") && redisConnectionString.StartsWith("redis://"))
        {
            // Handle Upstash connection string format: redis://default:PASSWORD@HOST:PORT
            redisLogger.LogInformation("Configuring Upstash Redis connection");
            
            // Remove the protocol prefix
            var withoutProtocol = redisConnectionString.Substring("redis://".Length);
            
            // Split at @ to separate credentials from endpoint
            var parts = withoutProtocol.Split('@');
            if (parts.Length == 2)
            {
                // Extract credentials
                var credentials = parts[0].Split(':');
                if (credentials.Length >= 2)
                {
                    redisOptions.Password = credentials[1];
                }
                
                // Extract endpoint
                var endpoint = parts[1];
                var hostPort = endpoint.Split(':');
                if (hostPort.Length == 2)
                {
                    redisOptions.EndPoints.Add(hostPort[0], int.Parse(hostPort[1]));
                    redisOptions.Ssl = true; // Upstash Redis requires SSL
                    redisLogger.LogInformation("Successfully configured Upstash Redis connection to: {Host}", hostPort[0]);
                }
            }
        }
        else
        {
            // Handle simple host:port format (local Redis or Docker Redis)
            redisLogger.LogInformation("Configuring standard Redis connection: {Connection}", redisConnectionString);
            var hostPort = redisConnectionString.Split(':');
            if (hostPort.Length >= 1)
            {
                var port = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 6379;
                redisOptions.EndPoints.Add(hostPort[0], port);
                redisLogger.LogInformation("Successfully configured Redis connection to: {Host}:{Port}", hostPort[0], port);
            }
            else
            {
                redisLogger.LogWarning("Invalid Redis connection string format, using default localhost:6379");
                redisOptions.EndPoints.Add("localhost", 6379);
            }
        }
    }
    else
    {
        // Default to localhost if no connection string is provided
        redisLogger.LogInformation("No Redis connection string provided, using default localhost:6379");
        redisOptions.EndPoints.Add("localhost", 6379);
    }
}
catch (Exception ex)
{
    redisLogger.LogError(ex, "Failed to parse Redis connection string. Using default localhost:6379");
    redisOptions.EndPoints.Clear();
    redisOptions.EndPoints.Add("localhost", 6379);
}

builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
{
    try
    {
        return ConnectionMultiplexer.Connect(redisOptions);
    }
    catch (Exception ex)
    {
        var serviceLogger = sp.GetRequiredService<ILogger<Program>>();
        serviceLogger.LogError(ex, "Failed to connect to Redis. The application will continue without Redis functionality.");
        
        // Create a dummy multiplexer for services that depend on it
        return ConnectionMultiplexer.Connect("127.0.0.1:6379,abortConnect=false");
    }
});

// Services
builder.Services.AddSingleton<PasswordHasher>();
builder.Services.AddSingleton<JwtHelper>();
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

// Background services
builder.Services.AddHostedService<QuestionsRefreshService>();


// JWT Authentication with custom token extraction
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"],
        ValidAudience = builder.Configuration["Jwt:Audience"],
        IssuerSigningKey = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"])),
        ClockSkew = TimeSpan.Zero, // Remove default 5-minute clock skew
        NameClaimType = System.Security.Claims.ClaimTypes.NameIdentifier, // Ensure correct claim mapping
        RoleClaimType = System.Security.Claims.ClaimTypes.Role
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            // First try to get token from cookies
            var cookieToken = context.Request.Cookies["token"];

            // Then try Authorization header
            var authHeader = context.Request.Headers["Authorization"].FirstOrDefault();
            var headerToken = !string.IsNullOrEmpty(authHeader) && authHeader.StartsWith("Bearer ")
                ? authHeader.Substring("Bearer ".Length).Trim()
                : null;

            // Use the token from cookie or header
            context.Token = cookieToken ?? headerToken;

            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            // Token is valid
            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            // Authentication failed - will be handled by error middleware
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            // Skip challenge for OPTIONS requests
            if (context.Request.Method == "OPTIONS")
            {
                context.HandleResponse();
                return Task.CompletedTask;
            }
            return Task.CompletedTask;
        }
    };
});
builder.Services.AddHttpClient("GitHub", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
    client.DefaultRequestHeaders.Add("User-Agent", "IqTest-server");
});
builder.Services.AddControllers(options =>
    {
        options.SuppressAsyncSuffixInActionNames = false;
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null;
        options.JsonSerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never;
        options.JsonSerializerOptions.AllowTrailingCommas = true;
        options.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
        options.JsonSerializerOptions.WriteIndented = true;
    });
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations and seed database in development
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        var logger = services.GetRequiredService<ILogger<Program>>();
        
        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();
            
            // Apply migrations - this will create database if needed
            logger.LogInformation("Applying database migrations...");
            context.Database.Migrate();
            logger.LogInformation("Database migrations completed successfully");
        }
        catch (Microsoft.Data.SqlClient.SqlException sqlEx) 
        {
            logger.LogWarning($"SQL Exception during migration: {sqlEx.Message}");
            
            // Handle specific SQL exceptions
            if (sqlEx.Message.Contains("Invalid column name 'Country'"))
            {
                logger.LogWarning("Country column issue detected, attempting to fix...");
                
                try
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();
                    // Execute raw SQL to add the column if it doesn't exist
                    context.Database.ExecuteSqlRaw(@"
                        IF NOT EXISTS (
                            SELECT 1 
                            FROM sys.columns 
                            WHERE object_id = OBJECT_ID(N'[dbo].[LeaderboardEntries]') 
                            AND name = 'Country'
                        )
                        BEGIN
                            ALTER TABLE [dbo].[LeaderboardEntries] 
                            ADD [Country] nvarchar(100) NOT NULL DEFAULT N'United States';
                        END");
                    
                    // Try migrations again
                    context.Database.Migrate();
                    logger.LogInformation("Country column issue resolved");
                }
                catch (Exception columnEx)
                {
                    logger.LogError(columnEx, "Failed to fix Country column issue");
                }
            }
            else if (sqlEx.Message.Contains("Database") && sqlEx.Message.Contains("already exists"))
            {
                logger.LogWarning("Database already exists, continuing with existing database");
                
                // Try to apply any pending migrations
                try
                {
                    var context = services.GetRequiredService<ApplicationDbContext>();
                    context.Database.Migrate();
                    logger.LogInformation("Pending migrations applied successfully");
                }
                catch (Exception migrationEx)
                {
                    logger.LogError(migrationEx, "Failed to apply pending migrations");
                }
            }
            else
            {
                logger.LogError(sqlEx, "SQL error during database setup");
                throw;
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred during database setup");
            throw;
        }
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// CRITICAL: CORS must come before authentication
app.UseCors("AllowSpecificOrigin");

// CSRF protection
app.UseMiddleware<CsrfProtectionMiddleware>();

// Rate limiting middleware
app.UseMiddleware<RateLimitingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<JsonExceptionMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapControllers();

app.Run();