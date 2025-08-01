using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AirportKiosk.Core.Models;
using System.Net;

namespace AirportKiosk.Services
{
    public interface IApiClientService
    {
        Task<TranslationResponse> TranslateTextAsync(string text, string sourceLanguage, string targetLanguage, string sessionId = null);
        Task<string> QuickTranslateAsync(string text, string from = "auto", string to = "en", string sessionId = null);
        Task<HealthCheckResponse> GetApiHealthAsync();
        Task<Dictionary<string, string>> GetSupportedLanguagesAsync();
        Task<Dictionary<string, object>> DetectLanguageAsync(string text);
        Task<bool> IsApiAvailableAsync();
        event EventHandler<string> ConnectionStatusChanged;
    }

    public class ApiClientService : IApiClientService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<ApiClientService> _logger;
        private readonly string _baseUrl;
        private readonly JsonSerializerOptions _jsonOptions;
        private readonly int _maxRetryAttempts;
        private readonly int _retryDelayMs;
        private readonly int _timeoutSeconds;

        private bool _isOnline = true;
        private DateTime _lastHealthCheck = DateTime.MinValue;
        private readonly TimeSpan _healthCheckInterval = TimeSpan.FromMinutes(1);

        public event EventHandler<string> ConnectionStatusChanged;

        public ApiClientService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<ApiClientService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7001";
            _maxRetryAttempts = configuration.GetValue<int>("ApiSettings:RetryAttempts", 3);
            _retryDelayMs = configuration.GetValue<int>("ApiSettings:RetryDelayMs", 1000);
            _timeoutSeconds = configuration.GetValue<int>("ApiSettings:TimeoutSeconds", 30);

            _httpClient.BaseAddress = new Uri(_baseUrl);
            _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);

            // Configure JSON serialization
            _jsonOptions = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            _logger.LogInformation("API Client configured - BaseUrl: {BaseUrl}, Timeout: {Timeout}s, MaxRetries: {MaxRetries}",
                _baseUrl, _timeoutSeconds, _maxRetryAttempts);
        }

        public async Task<TranslationResponse> TranslateTextAsync(
            string text,
            string sourceLanguage,
            string targetLanguage,
            string sessionId = null)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    throw new ArgumentException("Text cannot be empty", nameof(text));
                }

                // Check API health before making request
                await EnsureApiHealthAsync();

                var request = new TranslationRequest
                {
                    Text = text,
                    SourceLanguage = sourceLanguage ?? "auto",
                    TargetLanguage = targetLanguage ?? "en",
                    SessionId = sessionId ?? Guid.NewGuid().ToString()
                };

                _logger.LogInformation("Translation request - Session: {SessionId}, {Source}→{Target}, Length: {Length}, Text: '{Text}'",
                    request.SessionId, request.SourceLanguage, request.TargetLanguage, text.Length, text);

                var response = await ExecuteWithRetryAsync(async () =>
                {
                    var json = JsonSerializer.Serialize(request, _jsonOptions);
                    _logger.LogDebug("Sending JSON to API: {Json}", json);
                    var content = new StringContent(json, Encoding.UTF8, "application/json");
                    return await _httpClient.PostAsync("/api/translation/translate", content);
                });

                _logger.LogDebug("API Response Status: {StatusCode}", response.StatusCode);

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var translationResponse = JsonSerializer.Deserialize<TranslationResponse>(responseJson, _jsonOptions);

                    var duration = DateTime.UtcNow - startTime;
                    _logger.LogInformation("Translation success - Session: {SessionId}, Duration: {Duration}ms, Confidence: {Confidence}",
                        request.SessionId, duration.TotalMilliseconds, translationResponse.Confidence);

                    SetOnlineStatus(true);
                    return translationResponse;
                }
                else
                {
                    var errorContent = await response.Content.ReadAsStringAsync();
                    var statusCodeName = response.StatusCode.ToString();

                    _logger.LogWarning("Translation API error: {Status} ({StatusCode}) - {Content}",
                        statusCodeName, (int)response.StatusCode, errorContent);

                    var errorMessage = response.StatusCode switch
                    {
                        System.Net.HttpStatusCode.BadRequest => "Invalid translation request - please check the text",
                        System.Net.HttpStatusCode.Unauthorized => "Translation service authentication failed",
                        System.Net.HttpStatusCode.Forbidden => "Translation service access denied",
                        System.Net.HttpStatusCode.NotFound => "Translation service endpoint not found",
                        System.Net.HttpStatusCode.TooManyRequests => "Translation service rate limit exceeded - please try again later",
                        System.Net.HttpStatusCode.InternalServerError => "Translation service internal error",
                        System.Net.HttpStatusCode.BadGateway => "Translation service gateway error",
                        System.Net.HttpStatusCode.ServiceUnavailable => "Translation service temporarily unavailable",
                        System.Net.HttpStatusCode.GatewayTimeout => "Translation service timeout",
                        _ => $"Translation service error: {statusCodeName} ({(int)response.StatusCode})"
                    };

                    return CreateErrorResponse(text, sourceLanguage, targetLanguage, request.SessionId, errorMessage);
                }
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "Network error during translation");
                SetOnlineStatus(false);
                return CreateErrorResponse(text, sourceLanguage, targetLanguage, sessionId,
                    "Network connection failed. Please check your internet connection.");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning("Translation request timeout");
                return CreateErrorResponse(text, sourceLanguage, targetLanguage, sessionId,
                    "Request timed out. Please try again.");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during translation");
                return CreateErrorResponse(text, sourceLanguage, targetLanguage, sessionId,
                    "Translation service temporarily unavailable.");
            }
        }

        public async Task<string> QuickTranslateAsync(
            string text,
            string from = "auto",
            string to = "en",
            string sessionId = null)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }

                await EnsureApiHealthAsync();

                var queryParams = $"?text={Uri.EscapeDataString(text)}&from={from}&to={to}";
                if (!string.IsNullOrEmpty(sessionId))
                {
                    queryParams += $"&sessionId={sessionId}";
                }

                var response = await ExecuteWithRetryAsync(async () =>
                {
                    return await _httpClient.GetAsync($"/api/translation/quick{queryParams}");
                });

                if (response.IsSuccessStatusCode)
                {
                    var translatedText = await response.Content.ReadAsStringAsync();

                    // Remove JSON quotes if present
                    if (translatedText.StartsWith("\"") && translatedText.EndsWith("\""))
                    {
                        translatedText = translatedText.Substring(1, translatedText.Length - 2);
                    }

                    SetOnlineStatus(true);
                    return translatedText;
                }
                else
                {
                    _logger.LogWarning("Quick translate failed: {Status}", response.StatusCode);
                    return text; // Return original text on error
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during quick translate");
                SetOnlineStatus(false);
                return text; // Return original text on error
            }
        }

        public async Task<HealthCheckResponse> GetApiHealthAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/api/translation/health");

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var healthResponse = JsonSerializer.Deserialize<HealthCheckResponse>(json, _jsonOptions);

                    SetOnlineStatus(healthResponse.IsHealthy);
                    _lastHealthCheck = DateTime.UtcNow;

                    return healthResponse;
                }
                else
                {
                    SetOnlineStatus(false);
                    return new HealthCheckResponse
                    {
                        IsHealthy = false,
                        Status = $"HTTP {response.StatusCode}",
                        Details = new Dictionary<string, object>
                        {
                            { "HttpStatus", response.StatusCode.ToString() }
                        }
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Health check failed");
                SetOnlineStatus(false);

                return new HealthCheckResponse
                {
                    IsHealthy = false,
                    Status = "error",
                    Details = new Dictionary<string, object>
                    {
                        { "Error", ex.Message }
                    }
                };
            }
        }

        public async Task<Dictionary<string, string>> GetSupportedLanguagesAsync()
        {
            try
            {
                var response = await ExecuteWithRetryAsync(async () =>
                {
                    return await _httpClient.GetAsync("/api/translation/languages");
                });

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    var languages = JsonSerializer.Deserialize<Dictionary<string, string>>(json, _jsonOptions);

                    SetOnlineStatus(true);
                    return languages;
                }
                else
                {
                    _logger.LogWarning("Failed to get supported languages: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error getting supported languages");
                SetOnlineStatus(false);
            }

            // Return fallback languages
            return new Dictionary<string, string>
            {
                { "en", "English" },
                { "ja", "Japanese" },
                { "auto", "Auto-detect" }
            };
        }

        public async Task<Dictionary<string, object>> DetectLanguageAsync(string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return CreateFallbackLanguageDetection(text);
                }

                var json = JsonSerializer.Serialize(text, _jsonOptions);
                var content = new StringContent(json, Encoding.UTF8, "application/json");

                var response = await ExecuteWithRetryAsync(async () =>
                {
                    return await _httpClient.PostAsync("/api/translation/detect", content);
                });

                if (response.IsSuccessStatusCode)
                {
                    var responseJson = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<Dictionary<string, object>>(responseJson, _jsonOptions);

                    SetOnlineStatus(true);
                    return result;
                }
                else
                {
                    _logger.LogWarning("Language detection failed: {Status}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting language");
                SetOnlineStatus(false);
            }

            // Fallback to simple detection
            return CreateFallbackLanguageDetection(text);
        }

        public async Task<bool> IsApiAvailableAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync("/health");
                var isAvailable = response.IsSuccessStatusCode;

                SetOnlineStatus(isAvailable);
                return isAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "API availability check failed");
                SetOnlineStatus(false);
                return false;
            }
        }

        private async Task<HttpResponseMessage> ExecuteWithRetryAsync(Func<Task<HttpResponseMessage>> operation)
        {
            HttpResponseMessage lastResponse = null;
            Exception lastException = null;

            for (int attempt = 1; attempt <= _maxRetryAttempts; attempt++)
            {
                try
                {
                    lastResponse = await operation();

                    // Consider success or client errors (4xx) as non-retryable
                    if (lastResponse.IsSuccessStatusCode ||
                        ((int)lastResponse.StatusCode >= 400 && (int)lastResponse.StatusCode < 500))
                    {
                        return lastResponse;
                    }

                    _logger.LogWarning("API request failed (attempt {Attempt}/{Max}): {Status}",
                        attempt, _maxRetryAttempts, lastResponse.StatusCode);
                }
                catch (HttpRequestException ex)
                {
                    lastException = ex;
                    _logger.LogWarning(ex, "Network error (attempt {Attempt}/{Max})", attempt, _maxRetryAttempts);
                }
                catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
                {
                    lastException = ex;
                    _logger.LogWarning("Request timeout (attempt {Attempt}/{Max})", attempt, _maxRetryAttempts);
                }

                // Don't delay after the last attempt
                if (attempt < _maxRetryAttempts)
                {
                    var delay = _retryDelayMs * attempt; // Exponential backoff
                    _logger.LogDebug("Retrying in {Delay}ms...", delay);
                    await Task.Delay(delay);
                }
            }

            // If we got a response, return it even if it's an error
            if (lastResponse != null)
            {
                return lastResponse;
            }

            // If we only got exceptions, rethrow the last one
            throw lastException ?? new HttpRequestException("All retry attempts failed");
        }

        private async Task EnsureApiHealthAsync()
        {
            // Check health periodically
            if (DateTime.UtcNow - _lastHealthCheck > _healthCheckInterval)
            {
                _logger.LogDebug("Performing periodic health check");
                await GetApiHealthAsync();
            }
        }

        private void SetOnlineStatus(bool isOnline)
        {
            if (_isOnline != isOnline)
            {
                _isOnline = isOnline;
                var status = isOnline ? "Online" : "Offline";

                _logger.LogInformation("API connection status changed: {Status}", status);
                ConnectionStatusChanged?.Invoke(this, status);
            }
        }

        private Dictionary<string, object> CreateFallbackLanguageDetection(string text)
        {
            var hasJapanese = !string.IsNullOrEmpty(text) && text.Any(c =>
                (c >= 0x3040 && c <= 0x309F) || // Hiragana
                (c >= 0x30A0 && c <= 0x30FF) || // Katakana
                (c >= 0x4E00 && c <= 0x9FAF));  // Kanji

            return new Dictionary<string, object>
            {
                { "language", hasJapanese ? "ja" : "en" },
                { "confidence", hasJapanese ? 0.8 : 0.7 },
                { "languageName", hasJapanese ? "Japanese" : "English" },
                { "fallback", true }
            };
        }

        private TranslationResponse CreateErrorResponse(
            string originalText,
            string sourceLanguage,
            string targetLanguage,
            string sessionId,
            string errorMessage)
        {
            return new TranslationResponse
            {
                TranslatedText = originalText, // Return original text on error
                SourceLanguage = sourceLanguage,
                TargetLanguage = targetLanguage,
                Success = false,
                ErrorMessage = errorMessage,
                SessionId = sessionId ?? Guid.NewGuid().ToString(),
                Confidence = 0.0f,
                Provider = "API Client (Error)",
                ProcessingTime = TimeSpan.Zero
            };
        }
    }
}