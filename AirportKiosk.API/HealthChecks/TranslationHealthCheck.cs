using Microsoft.Extensions.Diagnostics.HealthChecks;
using AirportKiosk.Services;

namespace AirportKiosk.API.HealthChecks
{
    public class TranslationHealthCheck : IHealthCheck
    {
        private readonly ITranslationService _translationService;
        private readonly ILogger<TranslationHealthCheck> _logger;

        public TranslationHealthCheck(
            ITranslationService translationService,
            ILogger<TranslationHealthCheck> logger)
        {
            _translationService = translationService ?? throw new ArgumentNullException(nameof(translationService));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        }

        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                _logger.LogDebug("Starting translation service health check");

                // Check if the translation service is available
                var isAvailable = await _translationService.IsServiceAvailableAsync();

                if (isAvailable)
                {
                    _logger.LogDebug("Translation service health check passed");

                    return HealthCheckResult.Healthy("Translation service is available", new Dictionary<string, object>
                    {
                        { "service", "MyMemory Translation API" },
                        { "status", "available" },
                        { "supportedLanguages", _translationService.GetSupportedLanguages() },
                        { "lastChecked", DateTime.UtcNow }
                    });
                }
                else
                {
                    _logger.LogWarning("Translation service health check failed - service unavailable");

                    return HealthCheckResult.Unhealthy("Translation service is not available", null, new Dictionary<string, object>
                    {
                        { "service", "MyMemory Translation API" },
                        { "status", "unavailable" },
                        { "lastChecked", DateTime.UtcNow }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Translation service health check failed with exception");

                return HealthCheckResult.Unhealthy("Translation service health check failed", ex, new Dictionary<string, object>
                {
                    { "service", "MyMemory Translation API" },
                    { "status", "error" },
                    { "error", ex.Message },
                    { "lastChecked", DateTime.UtcNow }
                });
            }
        }
    }
}