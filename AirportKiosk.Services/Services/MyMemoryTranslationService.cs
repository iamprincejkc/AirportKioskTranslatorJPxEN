using System;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using AirportKiosk.Core.Models;

namespace AirportKiosk.Services
{
    public interface ITranslationService
    {
        Task<TranslationResponse> TranslateAsync(TranslationRequest request);
        Task<bool> IsServiceAvailableAsync();
        string GetSupportedLanguages();
    }

    public class MyMemoryTranslationService : ITranslationService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger<MyMemoryTranslationService> _logger;
        private readonly string _baseUrl;
        private readonly int _timeoutSeconds;

        public MyMemoryTranslationService(
            HttpClient httpClient,
            IConfiguration configuration,
            ILogger<MyMemoryTranslationService> logger)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));

            _baseUrl = configuration["Translation:MyMemory:BaseUrl"] ?? "https://api.mymemory.translated.net";
            _timeoutSeconds = configuration.GetValue<int>("Translation:MyMemory:TimeoutSeconds", 10);

            _httpClient.Timeout = TimeSpan.FromSeconds(_timeoutSeconds);
        }

        public async Task<TranslationResponse> TranslateAsync(TranslationRequest request)
        {
            var startTime = DateTime.UtcNow;

            try
            {
                _logger.LogInformation("Starting translation from {Source} to {Target} for session {Session}",
                    request.SourceLanguage, request.TargetLanguage, request.SessionId);

                // Validate request
                if (string.IsNullOrWhiteSpace(request.Text))
                {
                    return CreateErrorResponse(request, "Text cannot be empty", startTime);
                }

                if (request.Text.Length > 5000)
                {
                    return CreateErrorResponse(request, "Text too long (max 5000 characters)", startTime);
                }

                // Prepare language pair
                var langPair = GetLanguagePair(request.SourceLanguage, request.TargetLanguage);

                _logger.LogDebug("MyMemory language pair: {LangPair} (from {Source} to {Target})",
                    langPair, request.SourceLanguage, request.TargetLanguage);

                // Build request URL
                var encodedText = HttpUtility.UrlEncode(request.Text);
                var requestUrl = $"{_baseUrl}/get?q={encodedText}&langpair={langPair}";

                _logger.LogDebug("Making request to MyMemory API: {Url}", requestUrl);

                // Make API call
                var response = await _httpClient.GetAsync(requestUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogWarning("MyMemory API returned error status: {Status}", response.StatusCode);
                    return CreateErrorResponse(request, $"Translation service error: {response.StatusCode}", startTime);
                }

                var jsonContent = await response.Content.ReadAsStringAsync();
                _logger.LogDebug("MyMemory API response: {Response}", jsonContent);

                // Parse response
                var apiResponse = JsonSerializer.Deserialize<MyMemoryApiResponse>(jsonContent, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    Converters = { new FlexibleStringConverter() }
                });

                if (apiResponse?.ResponseData == null)
                {
                    _logger.LogWarning("MyMemory API returned null response data");
                    return CreateErrorResponse(request, "Invalid response from translation service", startTime);
                }

                // Check for API errors
                if (!string.IsNullOrEmpty(apiResponse.ResponseData.TranslatedText) &&
                    apiResponse.ResponseData.TranslatedText.StartsWith("PLEASE SELECT"))
                {
                    return CreateErrorResponse(request, "Translation service quota exceeded", startTime);
                }

                var processingTime = DateTime.UtcNow - startTime;

                // Create successful response
                var translationResponse = new TranslationResponse
                {
                    TranslatedText = apiResponse.ResponseData.TranslatedText ?? request.Text,
                    DetectedSourceLanguage = request.SourceLanguage == "auto" ?
                        DetectLanguage(request.Text) : request.SourceLanguage,
                    SourceLanguage = request.SourceLanguage,
                    TargetLanguage = request.TargetLanguage,
                    Confidence = ParseConfidence(apiResponse.ResponseData.Match),
                    Provider = "MyMemory",
                    ProcessingTime = processingTime,
                    Success = true,
                    SessionId = request.SessionId,
                    ResponseTime = DateTime.UtcNow
                };

                _logger.LogInformation("Translation completed successfully in {Duration}ms for session {Session}",
                    processingTime.TotalMilliseconds, request.SessionId);

                return translationResponse;
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException)
            {
                _logger.LogWarning("Translation request timed out for session {Session}", request.SessionId);
                return CreateErrorResponse(request, "Translation service timeout", startTime);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "HTTP error during translation for session {Session}", request.SessionId);
                return CreateErrorResponse(request, "Network error connecting to translation service", startTime);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex, "JSON parsing error for session {Session}", request.SessionId);
                return CreateErrorResponse(request, "Invalid response format from translation service", startTime);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error during translation for session {Session}", request.SessionId);
                return CreateErrorResponse(request, "Unexpected translation service error", startTime);
            }
        }

        public async Task<bool> IsServiceAvailableAsync()
        {
            try
            {
                _logger.LogDebug("Checking MyMemory service availability");

                var testUrl = $"{_baseUrl}/get?q=test&langpair=en|ja";
                var response = await _httpClient.GetAsync(testUrl);

                var isAvailable = response.IsSuccessStatusCode;
                _logger.LogInformation("MyMemory service availability check: {Available}", isAvailable);

                return isAvailable;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Service availability check failed");
                return false;
            }
        }

        public string GetSupportedLanguages()
        {
            return "English (en), Japanese (ja)";
        }

        private TranslationResponse CreateErrorResponse(TranslationRequest request, string errorMessage, DateTime startTime)
        {
            var processingTime = DateTime.UtcNow - startTime;

            // Provide more user-friendly error messages
            var userFriendlyMessage = errorMessage switch
            {
                var msg when msg.Contains("JSON") => "Translation service data format error",
                var msg when msg.Contains("timeout") => "Translation service is taking too long - please try again",
                var msg when msg.Contains("network") => "Network connection problem - please check internet",
                var msg when msg.Contains("quota") => "Translation service daily limit reached",
                var msg when msg.Contains("Invalid response") => "Translation service returned invalid data",
                _ => errorMessage
            };

            return new TranslationResponse
            {
                TranslatedText = request.Text, // Return original text on error
                DetectedSourceLanguage = request.SourceLanguage,
                SourceLanguage = request.SourceLanguage,
                TargetLanguage = request.TargetLanguage,
                Confidence = 0.0f,
                Provider = "MyMemory",
                ProcessingTime = processingTime,
                Success = false,
                ErrorMessage = userFriendlyMessage,
                SessionId = request.SessionId,
                ResponseTime = DateTime.UtcNow
            };
        }

        private string GetLanguagePair(string sourceLanguage, string targetLanguage)
        {
            // Handle auto-detection
            if (sourceLanguage == "auto")
            {
                sourceLanguage = "en"; // Default to English for auto-detection
            }

            // Normalize language codes for MyMemory API
            var sourceLang = NormalizeLanguageCode(sourceLanguage);
            var targetLang = NormalizeLanguageCode(targetLanguage);

            var langPair = $"{sourceLang}|{targetLang}";

            _logger.LogDebug("Language pair created: {LangPair} (source: {Source} → {SourceNorm}, target: {Target} → {TargetNorm})",
                langPair, sourceLanguage, sourceLang, targetLanguage, targetLang);

            return langPair;
        }

        private string NormalizeLanguageCode(string languageCode)
        {
            var normalized = languageCode?.ToLower() switch
            {
                "en" or "english" or "en-us" => "en",
                "ja" or "japanese" or "jp" or "ja-jp" => "ja",
                "it" or "italian" or "it-it" => "it",
                "ko" or "korean" or "kr" or "ko-kr" => "ko",
                _ => "en"
            };

            _logger.LogDebug("Language code normalized: {Original} → {Normalized}", languageCode, normalized);
            return normalized;
        }

        private string DetectLanguage(string text)
        {
            // Simple language detection based on character patterns
            // This is basic - in production, you'd use a proper language detection service

            if (string.IsNullOrWhiteSpace(text))
                return "en";

            // Check for Japanese characters (Hiragana, Katakana, Kanji)
            var hasJapanese = false;
            foreach (char c in text)
            {
                if ((c >= 0x3040 && c <= 0x309F) || // Hiragana
                    (c >= 0x30A0 && c <= 0x30FF) || // Katakana
                    (c >= 0x4E00 && c <= 0x9FAF))   // Kanji
                {
                    hasJapanese = true;
                    break;
                }
            }

            return hasJapanese ? "ja" : "en";
        }

        private float ParseConfidence(string matchValue)
        {
            try
            {
                if (string.IsNullOrEmpty(matchValue))
                    return 0.5f;

                if (float.TryParse(matchValue, out float confidence))
                {
                    return Math.Max(0.0f, Math.Min(1.0f, confidence));
                }

                // MyMemory returns match quality as strings sometimes
                return matchValue.ToLower() switch
                {
                    "1" or "exact" => 1.0f,
                    "0.9" or "high" => 0.9f,
                    "0.8" => 0.8f,
                    "0.7" => 0.7f,
                    "0.6" => 0.6f,
                    "0.5" or "medium" => 0.5f,
                    "0.4" => 0.4f,
                    "0.3" => 0.3f,
                    "0.2" => 0.2f,
                    "0.1" or "low" => 0.1f,
                    _ => 0.5f
                };
            }
            catch
            {
                return 0.5f;
            }
        }
    }

    // MyMemory API Response Models
    internal class MyMemoryApiResponse
    {
        public MyMemoryResponseData ResponseData { get; set; }
        public string QuotaFinished { get; set; }
        public string MtLangSupported { get; set; }
        public string ResponseDetails { get; set; }
        public int ResponseStatus { get; set; }
        public string ResponderId { get; set; }
        public object Exception_code { get; set; }
        public MyMemoryMatch[] Matches { get; set; }
    }

    internal class MyMemoryResponseData
    {
        public string TranslatedText { get; set; }

        [JsonConverter(typeof(FlexibleStringConverter))]
        public string Match { get; set; }
    }

    internal class MyMemoryMatch
    {
        public string Id { get; set; }
        public string Segment { get; set; }
        public string Translation { get; set; }
        public string Source { get; set; }
        public string Target { get; set; }
        public string Quality { get; set; }
        public string Reference { get; set; }

        [JsonPropertyName("usage-count")]
        public string Usage_count { get; set; }
        public string Subject { get; set; }

        [JsonPropertyName("created-by")]
        public string Created_by { get; set; }

        [JsonPropertyName("last-updated-by")]
        public string Last_updated_by { get; set; }

        [JsonPropertyName("create-date")]
        public string Create_date { get; set; }

        [JsonPropertyName("last-update-date")]
        public string Last_update_date { get; set; }

        [JsonConverter(typeof(FlexibleStringConverter))]
        public string Match { get; set; }
        public string Penalty { get; set; }
    }

    // Custom JSON converter to handle both string and number values
    internal class FlexibleStringConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            switch (reader.TokenType)
            {
                case JsonTokenType.String:
                    return reader.GetString();
                case JsonTokenType.Number:
                    return reader.GetDecimal().ToString();
                case JsonTokenType.True:
                    return "true";
                case JsonTokenType.False:
                    return "false";
                case JsonTokenType.Null:
                    return null;
                default:
                    return reader.GetString();
            }
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}