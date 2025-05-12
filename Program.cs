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
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using StackExchange.Redis;

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
);

// Redis configuration
builder.Services.AddSingleton<IConnectionMultiplexer>(provider =>
{
    var redisConnection = builder.Configuration["Redis:ConnectionString"];
    if (string.IsNullOrEmpty(redisConnection))
    {
        redisConnection = "localhost:6379"; // Default fallback
    }
    return ConnectionMultiplexer.Connect(redisConnection);
});

// HttpClient for openAI API
builder.Services.AddHttpClient();

// CORS policy with container-specific origins
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowSpecificOrigin", policy =>
    {
        policy.WithOrigins(
                "http://localhost:3000",
                "http://frontend:3000",  // Docker container service name
                "http://host.docker.internal:3000"  // Docker host networking
            )
            .AllowAnyMethod()
            .AllowAnyHeader()
            .AllowCredentials();  // CRITICAL for cookies
    });
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
builder.Services.AddScoped<OpenAIService>();

// Add background service for question generation
builder.Services.AddHostedService<BackgroundQuestionGenerationService>();

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

            // Log what we found
            Console.WriteLine($"OnMessageReceived - Cookie token: {(cookieToken != null ? "Found" : "Not found")}");
            Console.WriteLine($"OnMessageReceived - Header token: {(headerToken != null ? "Found" : "Not found")}");
            Console.WriteLine($"OnMessageReceived - Request Path: {context.Request.Path}");
            Console.WriteLine($"OnMessageReceived - Request Method: {context.Request.Method}");

            // Use the token from cookie or header
            context.Token = cookieToken ?? headerToken;

            if (!string.IsNullOrEmpty(context.Token))
            {
                Console.WriteLine("Token successfully extracted");
            }
            else
            {
                Console.WriteLine("No token found in cookies or headers");
            }

            return Task.CompletedTask;
        },
        OnTokenValidated = context =>
        {
            Console.WriteLine("Token validated successfully");
            var userId = context.Principal?.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
            Console.WriteLine($"User ID from token: {userId}");

            // Log all claims for debugging
            var claims = context.Principal?.Claims?.ToList();
            if (claims != null)
            {
                Console.WriteLine("All claims:");
                foreach (var claim in claims)
                {
                    Console.WriteLine($"  - {claim.Type}: {claim.Value}");
                }
            }

            return Task.CompletedTask;
        },
        OnAuthenticationFailed = context =>
        {
            Console.WriteLine($"Authentication failed: {context.Exception.GetType().Name}: {context.Exception.Message}");
            Console.WriteLine($"Failed token: {context.Request.Cookies["token"] ?? context.Request.Headers["Authorization"].FirstOrDefault()}");
            return Task.CompletedTask;
        },
        OnChallenge = context =>
        {
            Console.WriteLine($"OnChallenge called for path: {context.Request.Path}");
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

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// Apply migrations and seed database in development
if (app.Environment.IsDevelopment())
{
    using (var scope = app.Services.CreateScope())
    {
        var services = scope.ServiceProvider;
        try
        {
            var context = services.GetRequiredService<ApplicationDbContext>();

            // Only migrate, don't create database if it exists
            if (context.Database.GetPendingMigrations().Any())
            {
                context.Database.Migrate();
            }
        }
        catch (Exception ex)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError(ex, "An error occurred during database migration.");
        }
    }

    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

// CRITICAL: CORS must come before authentication
app.UseCors("AllowSpecificOrigin");

app.UseAuthentication();
app.UseAuthorization();

app.UseMiddleware<ErrorHandlingMiddleware>();

app.MapControllers();

app.Run();