using AirportKiosk.Maui.Pages;

namespace AirportKiosk.Maui;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Register routes for navigation
        Routing.RegisterRoute(nameof(TranslationPage), typeof(TranslationPage));
    }
}