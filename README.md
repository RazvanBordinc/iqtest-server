# 🧠 IQ Test App - Backend API

<div align="center">
  <img src="https://img.shields.io/badge/.NET-9.0-512BD4?style=for-the-badge&logo=dotnet" alt=".NET 9.0"/>
  <img src="https://img.shields.io/badge/Entity_Framework-9.0-512BD4?style=for-the-badge&logo=.net" alt="EF Core"/>
  <img src="https://img.shields.io/badge/SQL_Server-2022-CC2927?style=for-the-badge&logo=microsoft-sql-server" alt="SQL Server"/>
  <img src="https://img.shields.io/badge/Redis-Cache-DC382D?style=for-the-badge&logo=redis" alt="Redis"/>
  <img src="https://img.shields.io/badge/Render-Deployed-46E3B7?style=for-the-badge&logo=render" alt="Render"/>
  
  <p>
    <strong>A robust ASP.NET Core 9.0 API powering the IQ Test application with advanced cognitive assessment capabilities</strong>
  </p>

  <p>
    <a href="#-features">Features</a> •
    <a href="#-tech-stack">Tech Stack</a> •
    <a href="#-architecture">Architecture</a> •
    <a href="#-getting-started">Getting Started</a> •
    <a href="#-api-endpoints">API Endpoints</a> •
    <a href="#-deployment">Deployment</a>
  </p>
</div>

## ✨ Features

### 🧪 Test Management
- **Multiple Test Types**: Numerical reasoning, verbal intelligence, memory & recall, and comprehensive IQ tests
- **GitHub-based Questions**: Questions fetched from external GitHub repository with caching
- **Smart Availability System**: 24-hour cooldown periods with precise timing
- **Advanced Scoring**: Sophisticated algorithms for accurate cognitive assessment

### 🔐 Security & Authentication
- **JWT-based Authentication**: Secure token system with 15-minute access tokens
- **Refresh Token Rotation**: Enhanced security with automatic token renewal
- **Password Security**: Modern hashing algorithms with salt protection
- **Rate Limiting**: Request throttling to prevent abuse
- **CORS Protection**: Secure cross-origin request handling

### 🚀 Performance & Scalability
- **Redis Caching**: High-performance caching for frequently accessed data
- **Async Operations**: Non-blocking I/O for improved throughput
- **Database Optimization**: Efficient queries with Entity Framework Core
- **Health Monitoring**: Built-in health checks and server wake-up endpoints

### 📊 Analytics & Leaderboards
- **Global Rankings**: Comprehensive leaderboard system with percentiles
- **Test-Specific Rankings**: Category-based performance tracking
- **User Statistics**: Detailed performance analytics and history
- **Country-based Analytics**: Regional performance comparisons

### 🛡️ Middleware & Security
- **Error Handling**: Global exception handling with user-friendly responses
- **Security Headers**: Comprehensive HTTP security headers
- **CSRF Protection**: Anti-forgery token validation
- **Input Validation**: Model validation to prevent injection attacks

## 🛠 Tech Stack

### Core Framework
- **[ASP.NET Core 9.0](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-9.0)** - High-performance web framework
- **[Entity Framework Core 9.0](https://docs.microsoft.com/en-us/ef/core/)** - Object-relational mapping
- **[SQL Server 2022](https://www.microsoft.com/en-us/sql-server)** - Primary database
- **[Redis](https://redis.io/)** - In-memory caching via StackExchange.Redis

### Authentication & Security
- **JWT Bearer Authentication** - Secure token-based auth
- **BCrypt** - Password hashing with salt
- **HTTPS Enforcement** - SSL/TLS encryption
- **CORS Configuration** - Cross-origin security

### Development & Deployment
- **[Swagger/OpenAPI](https://swagger.io/specification/)** - API documentation
- **[Render](https://render.com/)** - Cloud hosting platform
- **[Upstash](https://upstash.com/)** - Managed Redis service
- **Docker** - Containerization support

## 🏗 Architecture

The backend follows a **Clean Architecture** pattern with clear separation of concerns:

```
┌─────────────────────┐
│   Presentation      │  ← Controllers, Filters, Middleware
├─────────────────────┤
│   Application       │  ← Services, DTOs, Business Logic
├─────────────────────┤
│   Domain           │  ← Models, Entities, Core Logic
├─────────────────────┤
│   Infrastructure   │  ← Data Access, External Services
└─────────────────────┘
```

### Layer Responsibilities

1. **Controllers**: Handle HTTP requests/responses and route to services
2. **Services**: Implement business logic and coordinate between layers
3. **Models**: Define domain entities and business rules
4. **Data**: Entity Framework context and repository patterns
5. **Utilities**: Helper classes and shared functionality

## 🚀 Getting Started

### Prerequisites

```bash
# Required
.NET 9 SDK
SQL Server (Express/LocalDB for development)

# Optional
Redis (for caching - falls back gracefully if unavailable)
Docker (for containerized development)
```

### Quick Start

```bash
# Clone the repository
git clone <repository-url>
cd iqtest/IqTest-server

# Restore NuGet packages
dotnet restore

# Update database with latest migrations
dotnet ef database update

# Run the application
dotnet run

# Alternative: Run with hot reload
dotnet watch run
```

The API will be available at:
- **HTTP**: http://localhost:5164
- **HTTPS**: https://localhost:7164
- **Swagger UI**: http://localhost:5164/swagger (Development only)

### Environment Setup

Create `appsettings.Development.json` for local development:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=IqTestDev;Trusted_Connection=True;MultipleActiveResultSets=true"
  },
  "Jwt": {
    "Key": "your-development-secret-key-minimum-32-characters",
    "Issuer": "iqtest-dev",
    "Audience": "iqtest-dev-client",
    "DurationInMinutes": 15
  },
  "Redis": {
    "ConnectionString": "localhost:6379"
  },
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore": "Warning"
    }
  }
}
```

## 📁 Project Structure

```
IqTest-server/
├── 📁 Attributes/                    # Custom validation attributes
│   └── StrongPasswordAttribute.cs
├── 📁 Controllers/                   # API endpoints
│   ├── AuthController.cs             # Authentication & user management
│   ├── BaseController.cs             # Common controller functionality
│   ├── HealthController.cs           # Health checks & server wake-up
│   ├── LeaderboardController.cs      # Rankings & statistics
│   ├── ProfileController.cs          # User profile management
│   ├── QuestionController.cs         # Question management
│   ├── ResultsController.cs          # Test results & scoring
│   ├── TestController.cs             # Test orchestration
│   └── UserDataController.cs         # User data operations
├── 📁 Converters/                    # JSON serialization
│   └── AnswerValueJsonConverter.cs
├── 📁 Data/                          # Data access layer
│   ├── ApplicationDbContext.cs       # EF Core database context
│   └── 📁 EntityConfigurations/      # Entity type configurations
├── 📁 DTOs/                          # Data transfer objects
│   ├── 📁 Auth/                      # Authentication DTOs
│   ├── 📁 Leaderboard/               # Leaderboard DTOs
│   ├── 📁 Profile/                   # Profile DTOs
│   └── 📁 Test/                      # Test-related DTOs
├── 📁 Filters/                       # Action filters
│   └── ModelValidationActionFilter.cs
├── 📁 Middleware/                    # Custom middleware pipeline
│   ├── CsrfProtectionMiddleware.cs   # CSRF protection
│   ├── DynamicCorsMiddleware.cs      # CORS handling
│   ├── ErrorHandlingMiddleware.cs    # Global error handling
│   ├── RateLimitingMiddleware.cs     # Request rate limiting
│   ├── RequestLoggingMiddleware.cs   # Request/response logging
│   └── SecurityHeadersMiddleware.cs  # Security headers
├── 📁 Migrations/                    # EF Core database migrations
├── 📁 Models/                        # Domain entities
│   ├── Answer.cs                     # Test answer entity
│   ├── HardcodedQuestion.cs          # Predefined questions
│   ├── LeaderboardEntry.cs           # Leaderboard entries
│   ├── Question.cs                   # Test questions
│   ├── TestResult.cs                 # Test results
│   ├── TestType.cs                   # Test categories
│   └── User.cs                       # User accounts
├── 📁 Services/                      # Business logic layer
│   ├── AnswerValidatorService.cs     # Answer validation
│   ├── AuthService.cs                # Authentication logic
│   ├── CacheService.cs               # In-memory caching service
│   ├── GithubService.cs              # GitHub question fetching
│   ├── HardcodedTestData.cs          # Test type definitions
│   ├── LeaderboardService.cs         # Ranking calculations
│   ├── LoggingService.cs             # Application logging
│   ├── ProfileService.cs             # Profile management
│   ├── QuestionGeneratorService.cs   # Fallback question generation
│   ├── QuestionService.cs            # Question management
│   ├── QuestionsRefreshService.cs    # Question pool refresh
│   ├── RateLimitingService.cs        # Rate limit enforcement
│   ├── RedisService.cs               # Redis caching service
│   ├── ScoreCalculationService.cs    # Scoring algorithms
│   └── TestService.cs                # Test orchestration
├── 📁 Utilities/                     # Helper utilities
│   ├── JwtHelper.cs                  # JWT token management
│   └── PasswordHasher.cs             # Password security
├── Program.cs                        # Application entry point
├── appsettings.json                  # Production configuration
└── appsettings.Development.json      # Development configuration
```

## 🔒 Security Features

### Authentication & Authorization
- **JWT Tokens**: Short-lived access tokens (15 minutes) with secure refresh mechanism
- **Token Validation**: Comprehensive token validation with issuer/audience checks
- **Password Security**: BCrypt hashing with salt for maximum security
- **Role-based Access**: Extensible role system for future admin features

### Request Security
- **Rate Limiting**: Configurable request throttling per IP/user
- **CORS Protection**: Strict cross-origin request policies
- **CSRF Protection**: Anti-forgery tokens for state-changing operations
- **Input Validation**: Comprehensive model validation and sanitization

### Headers & Protocols
- **Security Headers**: X-Frame-Options, X-Content-Type-Options, X-XSS-Protection
- **HTTPS Enforcement**: Automatic HTTP to HTTPS redirection
- **HSTS**: HTTP Strict Transport Security for enhanced protection

## 🔄 API Endpoints

### 🔐 Authentication
```http
POST   /api/auth/register          # Register new user
POST   /api/auth/login             # User authentication
POST   /api/auth/refresh           # Refresh access token
POST   /api/auth/logout            # Invalidate tokens
POST   /api/auth/check-username    # Check username availability
POST   /api/auth/create-user       # Alternative registration endpoint
```

### 🧪 Test Management
```http
GET    /api/test/types                      # Available test types
GET    /api/question/test/{testTypeId}      # Get test questions (authenticated)
POST   /api/test/submit                     # Submit test answers
GET    /api/test/availability/{testTypeId}  # Check test availability
POST   /api/test/availability/batch         # Check multiple test availability
GET    /api/test/stats/{testTypeId}         # Test statistics
POST   /api/test/clear-cooldowns            # Clear test cooldowns (debug)
```

### 👤 User Profile
```http
GET    /api/profile                 # Current user profile
PUT    /api/profile/country         # Update user country
PUT    /api/profile/age            # Update user age
GET    /api/profile/test-history   # User's test history
DELETE /api/profile/data           # Delete user data (GDPR)
```

### 🏆 Leaderboards
```http
GET    /api/leaderboard/global              # Global rankings
GET    /api/leaderboard/test-type/{id}      # Test-specific rankings
GET    /api/leaderboard/user-ranking        # Current user's ranking
GET    /api/leaderboard/country/{country}   # Country-specific rankings
```

### 📊 System Health & Maintenance
```http
GET    /api/health                     # Health check endpoint
GET    /api/health/wake                # Server wake-up endpoint
DELETE /api/maintenance/clear-cache    # Clear Redis cache (admin)
DELETE /api/maintenance/clear-all-cache # Clear all caches including rate limiting
POST   /api/maintenance/refresh-questions # Refresh questions from GitHub
```

## ⚙️ Configuration

### Application Settings

**Production Configuration** (`appsettings.json`):
```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=localhost;Database=IqTest;Integrated Security=true;TrustServerCertificate=true"
  },
  "Jwt": {
    "Key": "your-production-secret-key-minimum-32-characters",
    "Issuer": "iqtest-api",
    "Audience": "iqtest-client",
    "DurationInMinutes": 15,
    "RefreshTokenDurationInDays": 7
  },
  "Redis": {
    "ConnectionString": "localhost:6379",
    "DefaultDatabase": 0,
    "KeyPrefix": "iqtest:"
  },
  "RateLimit": {
    "EnableLimiting": true,
    "GeneralRequestsPerMinute": 100,
    "AuthRequestsPerMinute": 10
  },
  "Cors": {
    "AllowedOrigins": ["https://iqtest-app.vercel.app"],
    "AllowCredentials": true
  }
}
```

### Environment Variables

For production deployment, use environment variables:
```bash
ConnectionStrings__DefaultConnection=your-db-connection-string
Jwt__Key=your-secure-production-key
Redis__ConnectionString=your-redis-connection-string
ASPNETCORE_ENVIRONMENT=Production
```

## 🚢 Deployment

### Render Deployment

The application is configured for deployment on **Render** with the following setup:

#### Build Settings
```yaml
# render.yaml
services:
  - type: web
    name: iqtest-server
    env: docker
    dockerfilePath: ./IqTest-server/Dockerfile
    dockerContext: ./IqTest-server
    plan: free
    region: oregon
    envVars:
      - key: ASPNETCORE_ENVIRONMENT
        value: Production
      - key: ConnectionStrings__DefaultConnection
        sync: false
      - key: Jwt__Key
        sync: false
      - key: Redis__ConnectionString
        sync: false
```

#### Database Setup
1. **SQL Server**: Use Azure SQL Database or similar managed service
2. **Connection String**: Set via environment variable for security
3. **Migrations**: Auto-applied on startup in production

#### Redis Cache
1. **Upstash Redis**: Recommended managed Redis service
2. **Configuration**: Set Redis connection string in environment variables
3. **Fallback**: Application gracefully handles Redis unavailability

### Docker Configuration

**Dockerfile**:
```dockerfile
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["IqTest-server.csproj", "."]
RUN dotnet restore "IqTest-server.csproj"
COPY . .
RUN dotnet build "IqTest-server.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "IqTest-server.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "IqTest-server.dll"]
```

## 📊 Monitoring & Logging

### Logging Configuration
- **Serilog**: Structured logging with JSON output
- **Log Levels**: Configurable logging levels per namespace
- **Request Logging**: Automatic HTTP request/response logging
- **Error Tracking**: Comprehensive exception logging with context

### Health Checks
- **Database Connectivity**: Entity Framework health check
- **Redis Availability**: Cache service health monitoring
- **Custom Checks**: Business logic health validation

### Performance Monitoring
- **Response Times**: Built-in response time tracking
- **Cache Hit Rates**: Redis performance metrics
- **Database Performance**: EF Core query performance tracking

## 🧪 Testing

### Unit Tests
```bash
# Run unit tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Integration Tests
- **Database Tests**: In-memory database testing
- **API Tests**: Full endpoint integration testing
- **Authentication Tests**: JWT token flow validation

### Load Testing
- **Stress Tests**: High-concurrency scenarios
- **Performance Benchmarks**: Response time measurements
- **Cache Performance**: Redis optimization validation

## 📝 Development Guidelines

### Code Standards
- **C# Conventions**: Follow Microsoft C# coding conventions
- **Async/Await**: Use async patterns for I/O operations
- **Dependency Injection**: Leverage built-in DI container
- **Exception Handling**: Use global exception middleware

### Database Guidelines
- **Migrations**: Always create migrations for schema changes
- **Indexing**: Proper indexing for query performance
- **Relationships**: Use proper entity relationships
- **Validation**: Model-level and database-level validation

### API Guidelines
- **RESTful Design**: Follow REST principles
- **HTTP Status Codes**: Use appropriate status codes
- **Error Responses**: Consistent error response format
- **Versioning**: API versioning strategy for future changes

## 📄 License

This project is [MIT](../LICENSE) licensed.

---

<div align="center">
  <p>Built with ❤️ using ASP.NET Core 9.0</p>
  <p>
    <a href="https://iqtest-server-project.onrender.com/swagger">Live API Documentation</a> •
    <a href="../iqtest/README.md">Frontend Documentation</a> •
    <a href="../.claude/README.md">Project Overview</a>
  </p>
</div>