using System;
using System.IdentityModel.Tokens.Jwt;
using System.Text;
using System.Threading.Tasks;
using IqTest_server.Data;
using IqTest_server.Middleware;
using IqTest_server.Services;
using IqTest_server.Utilities;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
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

// Database context
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(
        builder.Configuration.GetConnectionString("DefaultConnection"),
        sqlServerOptions => sqlServerOptions.EnableRetryOnFailure()
    )
    .ConfigureWarnings(warnings => 
        warnings.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.RelationalEventId.PendingModelChangesWarning))
);

 
 


// CORS policy with container-specific origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://frontend:3000",  // Docker container service name
                "http://host.docker.internal:3000",  // Docker host networking
                "http://127.0.0.1:3000",  // Loopback address
                "https://localhost:3000",  // HTTPS variant
                "https://127.0.0.1:3000"  // HTTPS loopback
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();  // CRITICAL for cookies
    });
});

// Redis for caching and rate limiting
builder.Services.AddStackExchangeRedisCache(options =>
{
    options.Configuration = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
    options.InstanceName = "IqTest";
});

// Add Redis connection multiplexer
var redisConnectionString = builder.Configuration["Redis:ConnectionString"] ?? "localhost:6379";
builder.Services.AddSingleton<StackExchange.Redis.IConnectionMultiplexer>(sp =>
    StackExchange.Redis.ConnectionMultiplexer.Connect(redisConnectionString));

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