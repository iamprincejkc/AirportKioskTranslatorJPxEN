using AirportKiosk.Maui.Pages;
using AirportKiosk.Maui.Services;
using AirportKiosk.Maui.ViewModels;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Reflection;

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

        var assembly = Assembly.GetExecutingAssembly();


        builder.Configuration.AddUserSecrets<App>();

        builder.Services.AddHttpClient();

        // Register Services
        builder.Services.AddSingleton<ITranslationService, TranslationService>();
        builder.Services.AddSingleton<ISpeechService, SpeechService>();
        builder.Services.AddSingleton<IFlightService, FlightBoardService>();

        // Register ViewModels
        builder.Services.AddTransient<MainKioskViewModel>();
        builder.Services.AddTransient<TranslationViewModel>();
        builder.Services.AddTransient<BostonFlightBoardViewModel>();

        // Register Pages
        builder.Services.AddTransient<MainKioskPage>();
        builder.Services.AddTransient<TranslationPage>();
        builder.Services.AddTransient<BostonFlightBoardPage>();

        // Add logging
        builder.Services.AddLogging(configure => configure.AddDebug());

        return builder.Build();
    }
}