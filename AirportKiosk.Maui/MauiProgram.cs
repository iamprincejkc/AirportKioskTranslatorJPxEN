using AirportKiosk.Maui.Services;
using AirportKiosk.Maui.ViewModels;
using AirportKiosk.Maui.Pages;
using Microsoft.Extensions.Logging;

namespace AirportKiosk.Maui;

public static class MauiProgram
{
    public static MauiApp CreateMauiApp()
    {
        var builder = MauiApp.CreateBuilder();
        builder
            .UseMauiApp<App>()
            .ConfigureFonts(fonts =>
            {
                fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
                fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
            });

#if DEBUG
        builder.Services.AddLogging(configure => configure.AddDebug());
#endif

        // Register HttpClient - using the built-in AddHttpClient
        builder.Services.AddSingleton<HttpClient>();

        // Register services
        builder.Services.AddSingleton<ITranslationService, TranslationService>();
        builder.Services.AddSingleton<ISpeechService, SpeechService>();

        // Register ViewModels
        builder.Services.AddTransient<MainKioskViewModel>();
        builder.Services.AddTransient<TranslationViewModel>();

        // Register Pages
        builder.Services.AddTransient<MainKioskPage>();
        builder.Services.AddTransient<TranslationPage>();

        return builder.Build();
    }
}