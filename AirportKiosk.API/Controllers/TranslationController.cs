using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using AirportKiosk.Core.Models;
using AirportKiosk.Services;

namespace AirportKiosk.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class TranslationController : ControllerBase
    {
        private readonly ITranslationService _translationService;
        private readonly ILogger<TranslationController> _logger;

        public TranslationController(
            ITranslationService translationService,
            ILogger<TranslationController> logger)
        {
            _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        /// <summary>
        /// Translates text between English and Japanese
        /// </summary>
        /// <param name="request">Translation request containing text and language preferences</param>
        /// <returns>Translation response with translated text and metadata</returns>
        [HttpPost("translate")]
        [ProducesResponseType(typeof(TranslationResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status500InternalServerError)]
        public async Task<ActionResult<TranslationResponse>> Translate([FromBody] TranslationRequest request)
        {
            try
            {
                // Generate request ID for tracking
                var requestId = HttpContext.TraceIdentifier ?? Guid.NewGuid().ToString();

                _logger.LogInformation("Translation request received - ID: {RequestId}, Session: {SessionId}, " +
                                     "Source: {Source}, Target: {Target}, Length: {Length}",
                    requestId, request.SessionId, request.SourceLanguage, request.TargetLanguage,
                    request.Text?.Length ?? 0);

                // Validate request
                if (!ModelState.IsValid)
                {
                    var errors = ModelState
                        .SelectMany(x => x.Value.Errors)
                        .Select(x => x.ErrorMessage)
                        .ToList();

                    _logger.LogWarning("Invalid translation request - ID: {RequestId}, Errors: {Errors}",
                        requestId, string.Join(", ", errors));

                    return BadRequest(new ApiError
                    {
                        Message = "Invalid request parameters",
                        Code = "VALIDATION_ERROR",
                        RequestId = requestId,
                        Details = new Dictionary<string, object> { { "Errors", errors } }
                    });
                }

                // Set session ID if not provided
                if (string.IsNullOrEmpty(request.SessionId))
                {
                    request.SessionId = requestId;
                }

                // Perform translation
                var response = await _translationService.TranslateAsync(request);

                if (response.Success)
                {
                    _logger.LogInformation("Translation completed successfully - ID: {RequestId}, " +
                                         "Confidence: {Confidence}, Duration: {Duration}ms",
                        requestId, response.Confidence, response.ProcessingTime.TotalMilliseconds);
                }
                else
                {
                    _logger.LogWarning("Translation failed - ID: {RequestId}, Error: {Error}",
                        requestId, response.ErrorMessage);
                }

                return Ok(response);
            }
            catch (Exception ex)
            {
                var requestId = HttpContext.TraceIdentifier ?? Guid.NewGuid().ToString();

                _logger.LogError(ex, "Unexpected error during translation - ID: {RequestId}", requestId);

                return StatusCode(StatusCodes.Status500InternalServerError, new ApiError
                {
                    Message = "An unexpected error occurred during translation",
                    Code = "INTERNAL_ERROR",
                    RequestId = requestId,
                    Details = new Dictionary<string, object>
                    {
                        { "ExceptionType", ex.GetType().Name }
                    }
                });
            }
        }

        /// <summary>
        /// Quick translation endpoint for simple text
        /// </summary>
        /// <param name="text">Text to translate</param>
        /// <param name="from">Source language (en/ja/auto)</param>
        /// <param name="to">Target language (en/ja)</param>
        /// <param name="sessionId">Optional session ID</param>
        /// <returns>Translated text</returns>
        [HttpGet("quick")]
        [ProducesResponseType(typeof(string), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public async Task<ActionResult<string>> QuickTranslate(
            [FromQuery][Required] string text,
            [FromQuery] string from = "auto",
            [FromQuery] string to = "en",
            [FromQuery] string sessionId = null)
        {
            try
            {
                var requestId = HttpContext.TraceIdentifier ?? Guid.NewGuid().ToString();

                var request = new TranslationRequest
                {
                    Text = text,
                    SourceLanguage = from,
                    TargetLanguage = to,
                    SessionId = sessionId ?? requestId
                };

                var response = await _translationService.TranslateAsync(request);

                if (response.Success)
                {
                    return Ok(response.TranslatedText);
                }
                else
                {
                    return BadRequest(new ApiError
                    {
                        Message = response.ErrorMessage,
                        Code = "TRANSLATION_ERROR",
                        RequestId = requestId
                    });
                }
            }
            catch (Exception ex)
            {
                var requestId = HttpContext.TraceIdentifier ?? Guid.NewGuid().ToString();

                _logger.LogError(ex, "Error in quick translation - ID: {RequestId}", requestId);

                return BadRequest(new ApiError
                {
                    Message = "Translation failed",
                    Code = "INTERNAL_ERROR",
                    RequestId = requestId
                });
            }
        }

        /// <summary>
        /// Check translation service health and availability
        /// </summary>
        /// <returns>Health status information</returns>
        [HttpGet("health")]
        [ProducesResponseType(typeof(HealthCheckResponse), StatusCodes.Status200OK)]
        public async Task<ActionResult<HealthCheckResponse>> GetHealth()
        {
            try
            {
                var isAvailable = await _translationService.IsServiceAvailableAsync();
                var supportedLanguages = _translationService.GetSupportedLanguages();

                var healthResponse = new HealthCheckResponse
                {
                    IsHealthy = isAvailable,
                    Status = isAvailable ? "healthy" : "unhealthy",
                    Details = new Dictionary<string, object>
                    {
                        { "ServiceAvailable", isAvailable },
                        { "SupportedLanguages", supportedLanguages },
                        { "Provider", "MyMemory" },
                        { "Version", "1.0.0" }
                    }
                };

                return Ok(healthResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking translation service health");

                return Ok(new HealthCheckResponse
                {
                    IsHealthy = false,
                    Status = "error",
                    Details = new Dictionary<string, object>
                    {
                        { "Error", ex.Message }
                    }
                });
            }
        }

        /// <summary>
        /// Get supported languages and language pairs
        /// </summary>
        /// <returns>List of supported languages</returns>
        [HttpGet("languages")]
        [ProducesResponseType(typeof(Dictionary<string, string>), StatusCodes.Status200OK)]
        public ActionResult<Dictionary<string, string>> GetSupportedLanguages()
        {
            var languages = new Dictionary<string, string>
            {
                { "en", "English" },
                { "ja", "Japanese" },
                { "auto", "Auto-detect" }
            };

            return Ok(languages);
        }

        /// <summary>
        /// Detect the language of provided text
        /// </summary>
        /// <param name="text">Text to analyze</param>
        /// <returns>Detected language code</returns>
        [HttpPost("detect")]
        [ProducesResponseType(typeof(Dictionary<string, object>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ApiError), StatusCodes.Status400BadRequest)]
        public ActionResult<Dictionary<string, object>> DetectLanguage([FromBody] string text)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BadRequest(new ApiError
                    {
                        Message = "Text cannot be empty",
                        Code = "VALIDATION_ERROR",
                        RequestId = HttpContext.TraceIdentifier
                    });
                }

                // Simple language detection
                var hasJapanese = text.Any(c =>
                    (c >= 0x3040 && c <= 0x309F) || // Hiragana
                    (c >= 0x30A0 && c <= 0x30FF) || // Katakana
                    (c >= 0x4E00 && c <= 0x9FAF));  // Kanji

                var detectedLanguage = hasJapanese ? "ja" : "en";
                var confidence = hasJapanese ?
                    (text.Count(c => c >= 0x3040 && c <= 0x9FAF) / (float)text.Length) :
                    0.8f;

                var result = new Dictionary<string, object>
                {
                    { "language", detectedLanguage },
                    { "confidence", Math.Min(1.0f, confidence + 0.2f) },
                    { "languageName", detectedLanguage == "ja" ? "Japanese" : "English" }
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error detecting language");

                return BadRequest(new ApiError
                {
                    Message = "Language detection failed",
                    Code = "DETECTION_ERROR",
                    RequestId = HttpContext.TraceIdentifier
                });
            }
        }
    }
}