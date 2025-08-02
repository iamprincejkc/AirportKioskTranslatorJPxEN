using AirportKiosk.Maui.Pages;

namespace AirportKiosk.Maui;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();

        MainPage = new AppShell();
    }
}