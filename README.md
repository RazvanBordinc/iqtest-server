# 🧠 IQ Test App - Backend API

<div align="center">
  <img src="https://via.placeholder.com/800x400?text=IQ+Test+Backend+Architecture" alt="Backend Architecture" width="800"/>
  
  <p>
    <strong>A robust ASP.NET Core 9.0 API powering the IQ Test application</strong>
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

- **Comprehensive Test Management**: Generate, serve, and evaluate various cognitive tests
- **Secure Authentication**: JWT-based auth with refresh tokens and role-based permissions
- **Performance Optimization**: Redis caching for high-traffic endpoints
- **Persistent Storage**: Entity Framework Core with SQL Server database
- **Robust Scoring System**: Advanced algorithms for accurately assessing cognitive abilities
- **Leaderboard System**: Global and test-specific rankings with percentiles
- **User Profiles**: Secure storage and management of user data and test history
- **Scalable Architecture**: Designed for horizontal scaling in containerized environments

<div align="center">
  <img src="https://via.placeholder.com/700x350?text=System+Architecture+Diagram" alt="System Architecture" width="700"/>
</div>

## 🛠 Tech Stack

- **Framework**: [ASP.NET Core 9.0](https://docs.microsoft.com/en-us/aspnet/core/?view=aspnetcore-9.0)
- **Data Access**: [Entity Framework Core 9.0](https://docs.microsoft.com/en-us/ef/core/)
- **Database**: [SQL Server 2022](https://www.microsoft.com/en-us/sql-server)
- **Caching**: [Redis](https://redis.io/) via StackExchange.Redis
- **Authentication**: JWT with [Microsoft.AspNetCore.Authentication.JwtBearer](https://www.nuget.org/packages/Microsoft.AspNetCore.Authentication.JwtBearer)
- **API Documentation**: [Swagger/OpenAPI](https://swagger.io/specification/)
- **Deployment**: [Render](https://render.com/) with [Upstash](https://upstash.com/) for Redis

### Key Backend Features

- **Dependency Injection**: Clean service architecture with built-in DI container
- **Repository Pattern**: Separation of data access logic from business logic
- **Middleware Pipeline**: Custom middleware for error handling, rate limiting, and security
- **Data Protection**: Secure handling of sensitive user information
- **Async/Await Pattern**: Non-blocking I/O operations for improved performance
- **Cross-Origin Resource Sharing (CORS)**: Configured for secure cross-domain requests
- **Resilient Redis Integration**: Robust caching and rate limiting with fallback mechanisms

## 🏗 Architecture

The backend follows a layered architecture pattern:

1. **Presentation Layer**: Controllers handling HTTP requests and responses
2. **Business Logic Layer**: Services implementing core application functionality
3. **Data Access Layer**: Repository classes and Entity Framework DbContext
4. **Domain Layer**: Entity models representing business objects

<div align="center">
  <img src="https://via.placeholder.com/600x350?text=Layered+Architecture+Diagram" alt="Layered Architecture" width="600"/>
</div>

## 🚀 Getting Started

### Prerequisites

- [.NET 9 SDK](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [SQL Server](https://www.microsoft.com/en-us/sql-server/sql-server-downloads) or [SQL Server Express](https://www.microsoft.com/en-us/sql-server/sql-server-downloads#Express)
- [Redis](https://redis.io/download) (optional for development)

### Installation

```bash
# Clone the repository
git clone https://github.com/yourusername/iqtest.git

# Navigate to the project directory
cd iqtest/IqTest-server

# Restore dependencies
dotnet restore

# Update database with migrations
dotnet ef database update

# Run the application
dotnet run
```

The API will be available at [http://localhost:5164](http://localhost:5164).

## 📁 Project Structure

```
IqTest-server/                            # Backend root directory
├── Attributes/                           # Custom attributes
│   └── StrongPasswordAttribute.cs        # Password validation attribute
├── Controllers/                          # API endpoints
│   ├── AuthController.cs                 # Authentication and user management
│   ├── LeaderboardController.cs          # Rankings and percentiles
│   ├── ProfileController.cs              # User profile operations
│   ├── QuestionController.cs             # Question management
│   ├── ResultsController.cs              # Test results processing
│   └── TestController.cs                 # Test management
├── Data/                                 # Data access layer
│   ├── ApplicationDbContext.cs           # EF Core database context
│   └── EntityConfigurations/             # Entity type configurations
├── DTOs/                                 # Data transfer objects
│   ├── Auth/                             # Authentication DTOs
│   ├── Leaderboard/                      # Leaderboard DTOs
│   ├── Profile/                          # User profile DTOs
│   └── Test/                             # Test-related DTOs
├── Filters/                              # Action filters
│   └── ModelValidationActionFilter.cs    # Input validation
├── Middleware/                           # Custom middleware
│   ├── ErrorHandlingMiddleware.cs        # Global error handling
│   ├── RateLimitingMiddleware.cs         # Request rate limiting
│   └── SecurityHeadersMiddleware.cs      # Security headers
├── Migrations/                           # Database migrations
├── Models/                               # Domain models
│   ├── Answer.cs                         # Test answer
│   ├── Question.cs                       # Test question
│   ├── TestResult.cs                     # Test result
│   ├── TestType.cs                       # Test category
│   └── User.cs                           # User account
├── Services/                             # Business logic
│   ├── AuthService.cs                    # Authentication logic
│   ├── LeaderboardService.cs             # Ranking calculations
│   ├── QuestionService.cs                # Question management
│   ├── ScoreCalculationService.cs        # Scoring algorithms
│   └── TestService.cs                    # Test orchestration
├── Utilities/                            # Helper classes
│   ├── JwtHelper.cs                      # JWT token handling
│   └── PasswordHasher.cs                 # Password security
├── Program.cs                            # Application entry point
└── appsettings.json                      # Configuration settings
```

## 🔒 Security Features

- **Password Security**: Secure hashing with salt using modern algorithms
- **JWT Authentication**: Short-lived access tokens with refresh token rotation
- **HTTPS Enforcement**: Redirect all HTTP requests to HTTPS
- **CSRF Protection**: Anti-forgery tokens for sensitive operations
- **Rate Limiting**: Prevent abuse through request throttling
- **Input Validation**: Model validation to prevent injection attacks
- **Security Headers**: Comprehensive set of HTTP security headers

## 🔄 API Endpoints

### Authentication

| Method | Endpoint | Description |
|--------|----------|-------------|
| POST | `/api/auth/register` | Register a new user |
| POST | `/api/auth/login` | Authenticate and receive tokens |
| POST | `/api/auth/refresh` | Refresh the access token |
| POST | `/api/auth/logout` | Invalidate current tokens |
| POST | `/api/auth/check-username` | Check username availability |

### Tests

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/test/types` | Get available test types |
| GET | `/api/test/questions/{testTypeId}` | Get questions for a test |
| POST | `/api/test/submit` | Submit test answers |
| GET | `/api/test/results/{testId}` | Get test results |
| GET | `/api/test/availability/{testTypeId}` | Check test availability |

### User Profile

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/profile` | Get current user profile |
| PUT | `/api/profile/country` | Update user country |
| PUT | `/api/profile/age` | Update user age |
| GET | `/api/profile/test-history` | Get test history |

### Leaderboard

| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/leaderboard/global` | Get global leaderboard |
| GET | `/api/leaderboard/test-type/{testTypeId}` | Get test-specific leaderboard |
| GET | `/api/leaderboard/user-ranking` | Get current user ranking |

## ⚙️ Configuration

Key application settings in `appsettings.json`:

```json
{
    "ConnectionStrings": {
        "DefaultConnection": "Server=localhost;Database=IqTest;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=true"
    },
    "Jwt": {
        "Key": "your-secret-key-with-minimum-16-characters",
        "Issuer": "iqtest-api",
        "Audience": "iqtest-client",
        "DurationInMinutes": 15
    },
    "Redis": {
        "ConnectionString": "localhost:6379"
    },
    "AllowedCors": {
        "Origins": ["http://localhost:3000"]
    }
}
```

## 🚢 Deployment

The API is configured for deployment on [Render](https://render.com/) with [Upstash](https://upstash.com/) for Redis:

### Database Setup

1. Create a SQL Server database (Azure SQL, Managed SQL, etc.)
2. Update the connection string in environment variables

### Redis Cache

1. Set up a Redis instance on Upstash
2. Configure the Redis connection string in environment variables

### Render Configuration

```yaml
services:
    - type: web
      name: iqtest-api
      env: docker
      dockerfilePath: ./Dockerfile
      envVars:
          - key: ConnectionStrings__DefaultConnection
            value: your-production-connection-string
          - key: Jwt__Key
            value: your-secure-production-key
            isSecret: true
          - key: Redis__ConnectionString
            value: your-upstash-redis-url
            isSecret: true
```

## 📊 Monitoring & Logging

- **Serilog**: Structured logging to files and optional external providers
- **Application Insights**: Performance monitoring integration (optional)
- **Health Checks**: Endpoints for monitoring system health

## 📃 API Documentation

Swagger documentation is automatically generated and available at `/swagger` when running in Development environment.

## 📝 License

This project is [MIT](LICENSE) licensed.

---

<div align="center">
  <p>Made with ❤️ for cognitive assessment</p>
</div>