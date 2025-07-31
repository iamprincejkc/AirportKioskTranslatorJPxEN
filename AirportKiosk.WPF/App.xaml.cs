using System;
using System.IO;
using System.Windows;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using AirportKiosk.Services;

namespace AirportKiosk.WPF
{
    public partial class App : Application
    {
        private ServiceProvider _serviceProvider;
        private ILogger<App> _logger;

        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                var serviceCollection = new ServiceCollection();

                // Configuration
                var configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production"}.json", optional: true)
                    .AddEnvironmentVariables()
                    .Build();

                serviceCollection.AddSingleton<IConfiguration>(configuration);

                // Logging
                serviceCollection.AddLogging(builder =>
                {
                    builder.ClearProviders();
                    builder.AddConfiguration(configuration.GetSection("Logging"));
                    builder.AddConsole();
                    builder.AddDebug();

                    // Add file logging if enabled
                    var fileLoggingEnabled = configuration.GetValue<bool>("Logging:File:Enabled", false);
                    if (fileLoggingEnabled)
                    {
                        var logPath = configuration.GetValue<string>("Logging:File:Path", "logs/kiosk.log");
                        var logDirectory = Path.GetDirectoryName(logPath);
                        if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                        {
                            Directory.CreateDirectory(logDirectory);
                        }
                        // Note: You would need to add a file logging provider here
                        // For example: builder.AddFile(logPath);
                    }
                });

                // HTTP Client for API communication
                serviceCollection.AddHttpClient<IApiClientService, ApiClientService>(client =>
                {
                    var baseUrl = configuration["ApiSettings:BaseUrl"] ?? "https://localhost:7001";
                    var timeoutSeconds = configuration.GetValue<int>("ApiSettings:TimeoutSeconds", 30);

                    client.BaseAddress = new Uri(baseUrl);
                    client.Timeout = TimeSpan.FromSeconds(timeoutSeconds);
                    client.DefaultRequestHeaders.Add("User-Agent", "AirportKiosk-WPF/1.0");
                });

                // Register services
                serviceCollection.AddSingleton<IAudioService, AudioService>();
                serviceCollection.AddSingleton<ISpeechRecognitionService, WindowsSpeechRecognitionService>();
                serviceCollection.AddScoped<IApiClientService, ApiClientService>();
                serviceCollection.AddSingleton<IConversationManager, ConversationManager>();

                // Register main window
                serviceCollection.AddTransient<MainWindow>();

                // Build service provider
                _serviceProvider = serviceCollection.BuildServiceProvider();

                // Initialize logging
                _logger = _serviceProvider.GetRequiredService<ILogger<App>>();
                _logger.LogInformation("Airport Kiosk application starting...");

                // Validate configuration
                ValidateConfiguration(configuration);

                // Start main window using DI
                var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
                mainWindow.Show();

                _logger.LogInformation("Airport Kiosk application started successfully");

                base.OnStartup(e);
            }
            catch (Exception ex)
            {
                var errorMessage = $"Failed to start Airport Kiosk application: {ex.Message}";
                MessageBox.Show(errorMessage, "Startup Error", MessageBoxButton.OK, MessageBoxImage.Error);

                _logger?.LogCritical(ex, "Application startup failed");
                Shutdown(1);
            }
        }

        private void ValidateConfiguration(IConfiguration configuration)
        {
            var errors = new List<string>();

            // Validate API settings
            var apiBaseUrl = configuration["ApiSettings:BaseUrl"];
            if (string.IsNullOrWhiteSpace(apiBaseUrl))
            {
                errors.Add("API BaseUrl is not configured");
            }
            else if (!Uri.TryCreate(apiBaseUrl, UriKind.Absolute, out _))
            {
                errors.Add("API BaseUrl is not a valid URL");
            }

            // Validate speech recognition settings
            var confidenceThreshold = configuration.GetValue<float>("SpeechRecognition:ConfidenceThreshold", 0.7f);
            if (confidenceThreshold < 0.0f || confidenceThreshold > 1.0f)
            {
                errors.Add("Speech recognition confidence threshold must be between 0.0 and 1.0");
            }

            // Validate audio settings
            var sampleRate = configuration.GetValue<int>("AudioSettings:SampleRate", 44100);
            if (sampleRate <= 0)
            {
                errors.Add("Audio sample rate must be greater than 0");
            }

            if (errors.Any())
            {
                var errorMessage = "Configuration errors found:\n" + string.Join("\n", errors);
                _logger?.LogError("Configuration validation failed: {Errors}", string.Join(", ", errors));
                throw new InvalidOperationException(errorMessage);
            }

            _logger?.LogInformation("Configuration validation passed");
        }

        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                _logger?.LogInformation("Airport Kiosk application shutting down...");

                _serviceProvider?.Dispose();

                _logger?.LogInformation("Airport Kiosk application shutdown complete");
            }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "Error during application shutdown");
            }
            finally
            {
                base.OnExit(e);
            }
        }

        private void Application_DispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
        {
            try
            {
                _logger?.LogCritical(e.Exception, "Unhandled exception occurred");

                var errorMessage = $"An unexpected error occurred:\n{e.Exception.Message}\n\nThe application will continue running.";
                MessageBox.Show(errorMessage, "Unexpected Error", MessageBoxButton.OK, MessageBoxImage.Error);

                // Mark the exception as handled so the application doesn't crash
                e.Handled = true;
            }
            catch (Exception ex)
            {
                // Last resort error handling
                MessageBox.Show($"Critical error in error handler: {ex.Message}", "Critical Error",
                    MessageBoxButton.OK, MessageBoxImage.Stop);
            }
        }
    }
}