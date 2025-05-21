using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace IqTest_server.Services
{
    /// <summary>
    /// Service for centralized logging with additional Render/Upstash integration
    /// </summary>
    public class LoggingService
    {
        private readonly ILogger<LoggingService> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;
        private readonly string _environment;
        private readonly string _renderServiceId;
        private readonly string _renderApiKey;
        private readonly bool _isRender;
        private readonly bool _isDevelopment;
        private readonly JsonSerializerOptions _jsonOptions;
        
        // Queue for batch logging
        private readonly ConcurrentQueue<LogEntry> _logQueue = new ConcurrentQueue<LogEntry>();
        private readonly Timer _processTimer;
        private const int BATCH_SIZE = 10;
        private const int PROCESS_INTERVAL_MS = 5000; // 5 seconds
        
        /// <summary>
        /// Log entry structure for external logging systems
        /// </summary>
        private class LogEntry
        {
            public string Level { get; set; }
            public string Message { get; set; }
            public Dictionary<string, object> Metadata { get; set; }
            public string Source { get; set; } = "backend";
            public string Environment { get; set; }
            public string ServiceId { get; set; }
            public string Timestamp { get; set; } = DateTime.UtcNow.ToString("o");
        }

        public LoggingService(ILogger<LoggingService> logger, IConfiguration configuration, IHttpClientFactory httpClientFactory)
        {
            _logger = logger;
            _configuration = configuration;
            _httpClient = httpClientFactory.CreateClient("Logging");
            
            // Get environment details
            _environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";
            _isDevelopment = _environment == "Development";
            
            // Check if running on Render
            _renderServiceId = Environment.GetEnvironmentVariable("RENDER_SERVICE_ID");
            _renderApiKey = _configuration["Logging:Render:ApiKey"];
            _isRender = !string.IsNullOrEmpty(_renderServiceId);
            
            // JSON options for serialization
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            };
            
            // Set up timer for batch processing logs
            _processTimer = new Timer(ProcessLogQueue, null, PROCESS_INTERVAL_MS, PROCESS_INTERVAL_MS);
            
            // Log service start
            Log(LogLevel.Information, "LoggingService initialized", new Dictionary<string, object>
            {
                { "environment", _environment },
                { "isRender", _isRender },
                { "renderServiceId", _renderServiceId ?? "not-set" }
            });
        }
        
        /// <summary>
        /// Main logging method that handles all types of logs
        /// </summary>
        public void Log(LogLevel level, string message, Dictionary<string, object> metadata = null)
        {
            // Always log to standard logging system
            LogToStandardOutput(level, message, metadata);
            
            // Queue for external logging if configured
            if (_isRender || !_isDevelopment)
            {
                QueueForExternalLogging(level, message, metadata);
            }
        }
        
        /// <summary>
        /// Log to standard .NET logging
        /// </summary>
        private void LogToStandardOutput(LogLevel level, string message, Dictionary<string, object> metadata)
        {
            // Create structured logging message
            var state = metadata != null 
                ? metadata.Select(kvp => new KeyValuePair<string, object>(kvp.Key, kvp.Value)).ToList() 
                : new List<KeyValuePair<string, object>>();
            
            // Add environment info
            state.Add(new KeyValuePair<string, object>("environment", _environment));
            state.Add(new KeyValuePair<string, object>("isRender", _isRender));
            state.Add(new KeyValuePair<string, object>("timestamp", DateTime.UtcNow.ToString("o")));
            
            // Add the message to state for structured logging
            state.Add(new KeyValuePair<string, object>("message", message));
            
            // Log based on level
            switch (level)
            {
                case LogLevel.Trace:
                    _logger.LogTrace("{@state}", state);
                    break;
                case LogLevel.Debug:
                    _logger.LogDebug("{@state}", state);
                    break;
                case LogLevel.Information:
                    _logger.LogInformation("{@state}", state);
                    break;
                case LogLevel.Warning:
                    _logger.LogWarning("{@state}", state);
                    break;
                case LogLevel.Error:
                    _logger.LogError("{@state}", state);
                    break;
                case LogLevel.Critical:
                    _logger.LogCritical("{@state}", state);
                    break;
            }
        }
        
        /// <summary>
        /// Queue log for external logging systems (Render logs, etc.)
        /// </summary>
        private void QueueForExternalLogging(LogLevel level, string message, Dictionary<string, object> metadata)
        {
            var logEntry = new LogEntry
            {
                Level = level.ToString().ToLower(),
                Message = message,
                Metadata = metadata ?? new Dictionary<string, object>(),
                Environment = _environment,
                ServiceId = _renderServiceId
            };
            
            // Add timestamp and request ID if available
            if (!logEntry.Metadata.ContainsKey("timestamp"))
            {
                logEntry.Metadata["timestamp"] = DateTime.UtcNow.ToString("o");
            }
            
            // Add to queue for batch processing
            _logQueue.Enqueue(logEntry);
        }
        
        /// <summary>
        /// Process the log queue at intervals, sending batches to external systems
        /// </summary>
        private async void ProcessLogQueue(object state)
        {
            if (_logQueue.IsEmpty)
            {
                return;
            }
            
            try
            {
                var batch = new List<LogEntry>();
                
                // Dequeue up to BATCH_SIZE logs
                while (batch.Count < BATCH_SIZE && _logQueue.TryDequeue(out var logEntry))
                {
                    batch.Add(logEntry);
                }
                
                if (batch.Count > 0)
                {
                    // Send logs to Render if running there
                    if (_isRender && !string.IsNullOrEmpty(_renderApiKey))
                    {
                        await SendLogsToRender(batch);
                    }
                }
            }
            catch (Exception ex)
            {
                // Log locally if external logging fails
                _logger.LogError(ex, "Failed to process log queue");
            }
        }
        
        /// <summary>
        /// Send logs to Render's log drain API (if available)
        /// </summary>
        private async Task SendLogsToRender(List<LogEntry> logs)
        {
            try
            {
                // Render uses standard output for logging, which is collected
                // Format logs for Render's log collector
                foreach (var log in logs)
                {
                    // Render recommends a specific format for structured logging
                    var renderLog = new Dictionary<string, object>
                    {
                        { "level", log.Level },
                        { "message", log.Message },
                        { "service_id", _renderServiceId },
                        { "timestamp", log.Timestamp },
                        { "metadata", log.Metadata }
                    };
                    
                    // Output to console in JSON format for Render to collect
                    var jsonLog = JsonSerializer.Serialize(renderLog, _jsonOptions);
                    Console.WriteLine(jsonLog);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to send logs to Render");
            }
        }
        
        /// <summary>
        /// Log API request with timing
        /// </summary>
        public IDisposable LogApiRequest(string controller, string action, string requestMethod, Dictionary<string, object> additionalInfo = null)
        {
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            
            var metadata = additionalInfo ?? new Dictionary<string, object>();
            metadata["controller"] = controller;
            metadata["action"] = action;
            metadata["method"] = requestMethod;
            metadata["event"] = "api_request_start";
            
            Log(LogLevel.Information, $"API Request: {requestMethod} {controller}/{action}", metadata);
            
            return new ApiRequestLogger(this, stopwatch, controller, action, requestMethod, metadata);
        }
        
        /// <summary>
        /// Helper class to measure and log API request completion
        /// </summary>
        private class ApiRequestLogger : IDisposable
        {
            private readonly LoggingService _loggingService;
            private readonly Stopwatch _stopwatch;
            private readonly string _controller;
            private readonly string _action;
            private readonly string _requestMethod;
            private readonly Dictionary<string, object> _metadata;
            private bool _disposed = false;
            
            public ApiRequestLogger(LoggingService loggingService, Stopwatch stopwatch, string controller, string action, string requestMethod, Dictionary<string, object> metadata)
            {
                _loggingService = loggingService;
                _stopwatch = stopwatch;
                _controller = controller;
                _action = action;
                _requestMethod = requestMethod;
                _metadata = new Dictionary<string, object>(metadata);
            }
            
            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                
                _stopwatch.Stop();
                
                _metadata["duration_ms"] = _stopwatch.ElapsedMilliseconds;
                _metadata["event"] = "api_request_complete";
                
                _loggingService.Log(
                    LogLevel.Information, 
                    $"API Request completed: {_requestMethod} {_controller}/{_action} in {_stopwatch.ElapsedMilliseconds}ms",
                    _metadata
                );
            }
        }
        
        // Shorthand methods for common log levels
        public void LogInfo(string message, Dictionary<string, object> metadata = null) => Log(LogLevel.Information, message, metadata);
        public void LogError(string message, Exception ex = null, Dictionary<string, object> metadata = null)
        {
            var combinedMetadata = metadata ?? new Dictionary<string, object>();
            if (ex != null)
            {
                combinedMetadata["exception"] = ex.Message;
                combinedMetadata["stackTrace"] = ex.StackTrace;
                combinedMetadata["exceptionType"] = ex.GetType().Name;
            }
            Log(LogLevel.Error, message, combinedMetadata);
        }
        public void LogWarning(string message, Dictionary<string, object> metadata = null) => Log(LogLevel.Warning, message, metadata);
        public void LogDebug(string message, Dictionary<string, object> metadata = null) => Log(LogLevel.Debug, message, metadata);
        
        // User authentication and activity logging
        public void LogUserActivity(string userId, string activity, Dictionary<string, object> metadata = null)
        {
            var combinedMetadata = metadata ?? new Dictionary<string, object>();
            combinedMetadata["userId"] = userId;
            combinedMetadata["activity"] = activity;
            combinedMetadata["event"] = "user_activity";
            
            Log(LogLevel.Information, $"User activity: {activity}", combinedMetadata);
        }
        
        // Test activities logging
        public void LogTestActivity(string userId, string testType, string activity, Dictionary<string, object> metadata = null)
        {
            var combinedMetadata = metadata ?? new Dictionary<string, object>();
            combinedMetadata["userId"] = userId;
            combinedMetadata["testType"] = testType;
            combinedMetadata["activity"] = activity;
            combinedMetadata["event"] = "test_activity";
            
            Log(LogLevel.Information, $"Test activity: {activity} for {testType}", combinedMetadata);
        }
    }
}