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
                
                // Vercel deployment 
                "https://*.vercel.app",        // All Vercel preview deployments
                "https://iqtest-app.vercel.app",
                
                // Production domains
                "https://iqtest.com",
                "https://www.iqtest.com",
                
                // Allow all domains as a fallback (will be used if AllowCredentials is false)
                "*"
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .SetIsOriginAllowedToAllowWildcardSubdomains();
            
        // For the wildcard fallback, we need a separate policy that doesn't set AllowCredentials
        // This will be determined at runtime in middleware
    });
    
    // Add a fallback policy that allows all origins but without credentials
    // This is used when a request comes from an origin not in our list
    options.AddPolicy("AllowAnyOrigin", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
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
    ConnectRetry = 10, // Increase retry attempts
    ConnectTimeout = 30000, // Increase connection timeout to 30 seconds
    Password = "", // Will be set below if present in connection string
    Ssl = false, // Will be set to true for Upstash Redis
    SyncTimeout = 30000, // Increase sync timeout to 30 seconds
    ResponseTimeout = 30000, // Add response timeout
    KeepAlive = 60, // Add keep-alive option (seconds)
    ReconnectRetryPolicy = new ExponentialRetry(5000, 60000), // Use exponential retry with max 60 seconds
    DefaultDatabase = 0
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
            // Handle both simple host:port format and more complex connection string format
            redisLogger.LogInformation("Configuring standard Redis connection");
            
            // Check if we have a more complex connection string with password
            if (redisConnectionString.Contains(","))
            {
                // This looks like a complex connection string, try to parse it
                var parts = redisConnectionString.Split(',');
                string host = "localhost";
                int port = 6379;
                
                // Extract host:port from the first part
                var hostPortPart = parts[0];
                var hostPortSplit = hostPortPart.Split(':');
                if (hostPortSplit.Length >= 1)
                {
                    host = hostPortSplit[0];
                    if (hostPortSplit.Length > 1)
                    {
                        port = int.Parse(hostPortSplit[1]);
                    }
                }
                
                // Add the endpoint
                redisOptions.EndPoints.Add(host, port);
                
                // Process other parts (like password, abortConnect, etc.)
                foreach (var part in parts.Skip(1))
                {
                    var keyValue = part.Split('=');
                    if (keyValue.Length == 2)
                    {
                        var key = keyValue[0].Trim().ToLower();
                        var value = keyValue[1].Trim();
                        
                        switch (key)
                        {
                            case "password":
                                redisOptions.Password = value;
                                break;
                            case "abortconnect":
                                if (bool.TryParse(value, out bool abortConnect))
                                {
                                    redisOptions.AbortOnConnectFail = abortConnect;
                                }
                                break;
                            case "ssl":
                                if (bool.TryParse(value, out bool ssl))
                                {
                                    redisOptions.Ssl = ssl;
                                }
                                break;
                            // Add more parameters as needed
                        }
                    }
                }
                
                redisLogger.LogInformation("Successfully configured Redis connection to: {Host}:{Port} with extended options", host, port);
            }
            else
            {
                // Simple host:port format
                var hostPort = redisConnectionString.Split(':');
                if (hostPort.Length >= 1)
                {
                    var host = hostPort[0];
                    var port = hostPort.Length > 1 ? int.Parse(hostPort[1]) : 6379;
                    redisOptions.EndPoints.Add(host, port);
                    redisLogger.LogInformation("Successfully configured Redis connection to: {Host}:{Port}", host, port);
                }
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
        var serviceLogger = sp.GetRequiredService<ILogger<Program>>();
        serviceLogger.LogInformation("Attempting to connect to Redis with the following settings: " +
            $"Endpoints: {string.Join(", ", redisOptions.EndPoints.Select(ep => $"{ep.Host}:{ep.Port}"))} " +
            $"ConnectTimeout: {redisOptions.ConnectTimeout}ms " +
            $"AbortOnConnectFail: {redisOptions.AbortOnConnectFail} " +
            $"ConnectRetry: {redisOptions.ConnectRetry} attempts");
        
        var multiplexer = ConnectionMultiplexer.Connect(redisOptions);
        
        // Verify connection is successful
        var connectionState = multiplexer.GetStatus();
        serviceLogger.LogInformation("Redis connection established. Connection state: {State}", connectionState);
        
        // Register error and reconnect handlers
        multiplexer.ConnectionFailed += (sender, args) => {
            serviceLogger.LogError("Redis connection failed. Endpoint: {Endpoint}, Exception: {Exception}", 
                args.EndPoint, args.Exception.Message);
        };
        
        multiplexer.ConnectionRestored += (sender, args) => {
            serviceLogger.LogInformation("Redis connection restored. Endpoint: {Endpoint}", args.EndPoint);
        };
        
        multiplexer.ErrorMessage += (sender, args) => {
            serviceLogger.LogWarning("Redis error: {Message}", args.Message);
        };
        
        return multiplexer;
    }
    catch (Exception ex)
    {
        var serviceLogger = sp.GetRequiredService<ILogger<Program>>();
        serviceLogger.LogError(ex, "Failed to connect to Redis. The application will continue without Redis functionality.");
        
        // Add more detailed error information
        var endpointList = string.Join(", ", redisOptions.EndPoints.Select(ep => $"{ep.Host}:{ep.Port}"));
        serviceLogger.LogError("Redis connection details: Endpoints: {Endpoints}, ConnectTimeout: {Timeout}ms", 
            endpointList, redisOptions.ConnectTimeout);
        
        // Create a dummy multiplexer that won't attempt to connect
        var configOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            EndPoints = { { "127.0.0.1", 6379 } },
            ConnectTimeout = 100, // Very short timeout since we know it will fail
            ConnectRetry = 0, // No retries
            ReconnectRetryPolicy = null, // No reconnect policy
        };
        
        serviceLogger.LogWarning("Using dummy Redis connection multiplexer to prevent application failure");
        return ConnectionMultiplexer.Connect(configOptions);
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
// Use our custom dynamic CORS middleware
app.UseMiddleware<DynamicCorsMiddleware>();
// Then use the standard CORS middleware 
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