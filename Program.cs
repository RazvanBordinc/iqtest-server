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
    
    // Configure logging levels based on environment
    logging.SetMinimumLevel(LogLevel.Information);
    
    // Set minimum log levels for specific categories
    logging.AddFilter("Microsoft", LogLevel.Warning);
    logging.AddFilter("System", LogLevel.Warning);
    logging.AddFilter("Microsoft.AspNetCore.Mvc", LogLevel.Warning);
    logging.AddFilter("Microsoft.AspNetCore.Hosting", LogLevel.Information);
    
    // Custom logging configuration for Render deployment
    if (Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null)
    {
        logging.AddFilter("IqTest_server", LogLevel.Information);
    }
    
    // Development specific logging
    if (builder.Environment.IsDevelopment())
    {
        logging.AddFilter("IqTest_server", LogLevel.Debug);
    }
});

// Register custom HTTP client for logging service
builder.Services.AddHttpClient("Logging", client =>
{
    client.DefaultRequestHeaders.Add("Accept", "application/json");
});

// Add the custom logging service
builder.Services.AddSingleton<IqTest_server.Services.LoggingService>();

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

// Database context with connection string validation
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Log connection string diagnostics (only in production for debugging)
var isRender = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null;
var isDevelopment = builder.Environment.IsDevelopment();

if (isRender)
{
    Console.WriteLine("=== CONNECTION STRING DIAGNOSTICS ===");
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

// Check for common problematic patterns that cause SQL Server keyword errors
bool connectionStringFixed = false;

// Fix "userid" keyword issue
if (connectionString.Contains("userid=", StringComparison.OrdinalIgnoreCase) && 
    !connectionString.Contains("User ID=", StringComparison.Ordinal))
{
    connectionString = connectionString.Replace("userid=", "User ID=", StringComparison.OrdinalIgnoreCase);
    Console.WriteLine("WARNING: Fixed connection string 'userid' -> 'User ID' keyword issue automatically.");
    connectionStringFixed = true;
}

// Fix "UserId" keyword issue (another common variant)
if (connectionString.Contains("UserId=", StringComparison.Ordinal) && 
    !connectionString.Contains("User ID=", StringComparison.Ordinal))
{
    connectionString = connectionString.Replace("UserId=", "User ID=", StringComparison.Ordinal);
    Console.WriteLine("WARNING: Fixed connection string 'UserId' -> 'User ID' keyword issue automatically.");
    connectionStringFixed = true;
}

if (connectionStringFixed)
{
    Console.WriteLine($"Updated connection string: {connectionString}");
}

// Ensure essential connection parameters are present
if (!connectionString.Contains("Connect Timeout", StringComparison.OrdinalIgnoreCase) && 
    !connectionString.Contains("Connection Timeout", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";Connect Timeout=60";
    Console.WriteLine("Added Connect Timeout=60 to connection string");
    connectionStringFixed = true;
}

if (!connectionString.Contains("ConnectRetryCount", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";ConnectRetryCount=3";
    Console.WriteLine("Added ConnectRetryCount=3 to connection string");
    connectionStringFixed = true;
}

if (!connectionString.Contains("ConnectRetryInterval", StringComparison.OrdinalIgnoreCase))
{
    connectionString += ";ConnectRetryInterval=10";
    Console.WriteLine("Added ConnectRetryInterval=10 to connection string");
    connectionStringFixed = true;
}

if (connectionStringFixed)
{
    Console.WriteLine($"Final connection string: {connectionString}");
}

// Additional connection string validation for SQL Server
try
{
    var builder_test = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(connectionString);
    Console.WriteLine($"Connection string validation passed. Server: {builder_test.DataSource}, Database: {builder_test.InitialCatalog}");
    
    // Log connection details for debugging (without sensitive info)
    Console.WriteLine($"Connection details - Server: {builder_test.DataSource}, Database: {builder_test.InitialCatalog}, Timeout: {builder_test.ConnectTimeout}");
    
    // Additional validation for Render environment
    if (Environment.GetEnvironmentVariable("RENDER_SERVICE_ID") != null)
    {
        Console.WriteLine("Detected Render environment");
        
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
                maxRetryCount: 5,
                maxRetryDelay: TimeSpan.FromSeconds(30),
                errorNumbersToAdd: null);
            
            // Add command timeout for long-running queries
            sqlServerOptions.CommandTimeout(30);
            
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

 
 


// SIMPLIFIED CORS policy that addresses the remote hosting issues
builder.Services.AddCors(options =>
{
    // For API requests with credentials
    options.AddPolicy("AllowedOrigins", policy =>
    {
        policy.WithOrigins(
                "https://iqtest-app.vercel.app", 
                "https://iqtest-server-tkhl.onrender.com",
                "http://localhost:3000",
                "https://localhost:3000"
             )
             .AllowAnyMethod()
             .AllowAnyHeader()
             .AllowCredentials();
    });
    
    // For health checks and preflight requests
    options.AddPolicy("AllowAll", policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

// Get Redis connection string first
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";

// Redis for caching and rate limiting
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = redisConnectionString; // Use the same connection string
    options.InstanceName = "IqTest";
});

// Add Redis connection multiplexer with resilient configuration

// Create Redis configuration options programmatically instead of parsing
var redisOptions = new ConfigurationOptions
{
    AbortOnConnectFail = false, // Don't fail if Redis is temporarily unavailable
    ConnectRetry = 2, // Reduced retries
    ConnectTimeout = 2000, // 2 second timeout
    Password = "", // Will be set below if present in connection string
    Ssl = false, // Will be set to true for Upstash Redis
    SyncTimeout = 2000, // 2 second timeout
    AsyncTimeout = 2000, // 2 second timeout
    KeepAlive = 60, // Add keep-alive option (seconds)
    ReconnectRetryPolicy = new LinearRetry(1000), // Simple linear retry with 1 second delay
    DefaultDatabase = 0,
    AllowAdmin = false, // Security: disable admin commands
    CommandMap = CommandMap.Default // Use default command map
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
                else
                {
                    redisLogger.LogWarning("Invalid Redis connection string format, using default localhost:6379");
                    redisOptions.EndPoints.Add("localhost", 6379);
                }
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
        // Format endpoints manually with correct type casting for DnsEndPoint
        string endpoints = string.Join(", ", redisOptions.EndPoints.Select(ep => {
            if (ep is System.Net.DnsEndPoint dnsEp)
                return $"{dnsEp.Host}:{dnsEp.Port}";
            else if (ep is System.Net.IPEndPoint ipEp)
                return $"{ipEp.Address}:{ipEp.Port}";
            else
                return ep.ToString();
        }));
            
        serviceLogger.LogInformation("Attempting to connect to Redis with the following settings: " +
            $"Endpoints: {endpoints} " +
            $"ConnectTimeout: {redisOptions.ConnectTimeout}ms " +
            $"AbortOnConnectFail: {redisOptions.AbortOnConnectFail} " +
            $"ConnectRetry: {redisOptions.ConnectRetry} attempts");
        
        var multiplexer = ConnectionMultiplexer.Connect(redisOptions);
        
        // Verify connection is successful
        var connectionState = multiplexer.IsConnected ? "Connected" : "Disconnected";
        serviceLogger.LogInformation("Redis connection established. Connection state: {State}", connectionState);
        
        // Register error and reconnect handlers
        multiplexer.ConnectionFailed += (sender, args) => {
            serviceLogger.LogError("Redis connection failed. Endpoint: {Endpoint}, Exception: {Exception}", 
                args.EndPoint, args.Exception?.Message ?? "No exception details");
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
        // Format endpoints manually with correct type casting
        string endpointList = string.Join(", ", redisOptions.EndPoints.Select(ep => {
            if (ep is System.Net.DnsEndPoint dnsEp)
                return $"{dnsEp.Host}:{dnsEp.Port}";
            else if (ep is System.Net.IPEndPoint ipEp)
                return $"{ipEp.Address}:{ipEp.Port}";
            else
                return ep.ToString();
        }));
        
        serviceLogger.LogError("Redis connection details: Endpoints: {Endpoints}, ConnectTimeout: {Timeout}ms", 
            endpointList, redisOptions.ConnectTimeout);
        
        // Create a dummy multiplexer that won't attempt to connect
        var configOptions = new ConfigurationOptions
        {
            AbortOnConnectFail = false,
            EndPoints = { { "127.0.0.1", 6379 } },
            ConnectTimeout = 100, // Very short timeout since we know it will fail
            ConnectRetry = 0, // No retries
            ReconnectRetryPolicy = new LinearRetry(1000) // Simple retry policy
        };
        
        serviceLogger.LogWarning("Using dummy Redis connection multiplexer to prevent application failure");
        return ConnectionMultiplexer.Connect(configOptions);
    }
});

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
            Encoding.UTF8.GetBytes(builder.Configuration["Jwt:Key"] ?? throw new InvalidOperationException("JWT Key is missing"))),
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

            // Basic validation to avoid passing malformed tokens to the JWT handler
            // Only set the token if it's not empty and has at least one dot (indicating JWT format)
            var tokenToUse = cookieToken ?? headerToken;
            if (!string.IsNullOrEmpty(tokenToUse) && tokenToUse.Contains("."))
            {
                context.Token = tokenToUse;
            }

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

// Add Response Caching middleware
builder.Services.AddResponseCaching();

var app = builder.Build();

// Helper function to mask sensitive connection string data
static string MaskConnectionString(string connectionString)
{
    if (string.IsNullOrEmpty(connectionString)) return "null";
    
    return System.Text.RegularExpressions.Regex.Replace(connectionString, 
        @"(password|pwd|user id|uid)\s*=\s*[^;]+", 
        "$1=***", 
        System.Text.RegularExpressions.RegexOptions.IgnoreCase);
}

// Database migration is now handled separately - not on startup
// This improves startup performance and prevents migration conflicts in scaled environments
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILogger<Program>>();
    
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        
        // Only check if database can connect, don't run migrations
        logger.LogInformation("Checking database connection...");
        var canConnect = await context.Database.CanConnectAsync();
        if (canConnect)
        {
            logger.LogInformation("Database connection successful");
        }
        else
        {
            logger.LogWarning("Cannot connect to database - application may have limited functionality");
        }
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error checking database connection");
        // Continue running even if database is not available
    }
}

// Configure the HTTP request pipeline
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// Response caching middleware (must come early in pipeline)
app.UseResponseCaching();

// Security headers
app.UseMiddleware<SecurityHeadersMiddleware>();

// CRITICAL: CORS must come before authentication
// Use our configured CORS policies - AllowedOrigins for auth endpoints,
// AllowAll as a default fallback with no credentials
app.UseCors();

// CSRF protection
app.UseMiddleware<CsrfProtectionMiddleware>();

// Rate limiting middleware
app.UseMiddleware<RateLimitingMiddleware>();

// Request logging middleware (must be before auth to capture all requests)
app.UseMiddleware<RequestLoggingMiddleware>();

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<JsonExceptionMiddleware>();
app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapControllers();

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