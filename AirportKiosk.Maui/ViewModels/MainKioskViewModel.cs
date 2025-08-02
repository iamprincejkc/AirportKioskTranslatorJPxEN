using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using AirportKiosk.Maui.Pages;

namespace AirportKiosk.Maui.ViewModels;

public class MainKioskViewModel : INotifyPropertyChanged
{
    private string _currentTime = DateTime.Now.ToString("HH:mm");
    private string _currentDate = DateTime.Now.ToString("dddd, MMMM dd, yyyy");
    private string _welcomeMessage = "Welcome to the Airport";
    private Timer? _timer;

    public MainKioskViewModel()
    {
        StartTimer();

        // Initialize commands
        NavigateToTranslationCommand = new Command(async () => await NavigateToTranslation());
        ShowFlightInfoCommand = new Command(async () => await ShowFlightInfo());
        ShowDirectionsCommand = new Command(async () => await ShowDirections());
        ShowServicesCommand = new Command(async () => await ShowServices());
        CallAssistanceCommand = new Command(async () => await CallAssistance());
    }

    public string CurrentTime
    {
        get => _currentTime;
        set
        {
            _currentTime = value;
            OnPropertyChanged();
        }
    }

    public string CurrentDate
    {
        get => _currentDate;
        set
        {
            _currentDate = value;
            OnPropertyChanged();
        }
    }

    public string WelcomeMessage
    {
        get => _welcomeMessage;
        set
        {
            _welcomeMessage = value;
            OnPropertyChanged();
        }
    }

    public ICommand NavigateToTranslationCommand { get; }
    public ICommand ShowFlightInfoCommand { get; }
    public ICommand ShowDirectionsCommand { get; }
    public ICommand ShowServicesCommand { get; }
    public ICommand CallAssistanceCommand { get; }

    private void StartTimer()
    {
        _timer = new Timer(UpdateTime, null, TimeSpan.Zero, TimeSpan.FromSeconds(1));
    }

    private void UpdateTime(object? state)
    {
        var now = DateTime.Now;
        CurrentTime = now.ToString("HH:mm");
        CurrentDate = now.ToString("dddd, MMMM dd, yyyy");
    }

    private async Task NavigateToTranslation()
    {
        await Shell.Current.GoToAsync(nameof(TranslationPage));
    }

    private async Task ShowFlightInfo()
    {
        await Application.Current.MainPage.DisplayAlert(
            "Flight Information",
            "Flight information feature coming soon!",
            "OK");
    }

    private async Task ShowDirections()
    {
        await Application.Current.MainPage.DisplayAlert(
            "Airport Directions",
            "Interactive airport map coming soon!",
            "OK");
    }

    private async Task ShowServices()
    {
        await Application.Current.MainPage.DisplayAlert(
            "Airport Services",
            "Information about restaurants, shops, and facilities coming soon!",
            "OK");
    }

    private async Task CallAssistance()
    {
        bool answer = await Application.Current.MainPage.DisplayAlert(
            "Call for Assistance",
            "Would you like to request assistance from airport staff?",
            "Yes", "No");

        if (answer)
        {
            await Application.Current.MainPage.DisplayAlert(
                "Assistance Requested",
                "Airport staff has been notified and will assist you shortly.",
                "OK");
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}