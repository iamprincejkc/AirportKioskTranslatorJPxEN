using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Collections.ObjectModel;
using AirportKiosk.Maui.Services;
using AirportKiosk.Maui.Models;

namespace AirportKiosk.Maui.ViewModels;

public class BostonFlightBoardViewModel : INotifyPropertyChanged
{
    private readonly IFlightService _flightService;
    private bool _isLoading = false;
    private string _statusMessage = "Loading flights...";
    private DateTime _selectedDate = DateTime.Today;
    private bool _isServiceAvailable = false;

    public BostonFlightBoardViewModel(IFlightService flightService)
    {
        _flightService = flightService;

        // Initialize commands
        RefreshCommand = new Command(async () => await RefreshFlights());
        GoBackCommand = new Command(async () => await GoBack());
        SelectDateCommand = new Command<DateTime>(async (date) => await SelectDate(date));

        // Initialize collections
        Departures = new ObservableCollection<FlightSchedule>();

        // Load initial data
        _ = Task.Run(async () => await InitializeAsync());
    }

    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            OnPropertyChanged();
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            _statusMessage = value;
            OnPropertyChanged();
        }
    }

    public DateTime SelectedDate
    {
        get => _selectedDate;
        set
        {
            _selectedDate = value;
            OnPropertyChanged();
        }
    }

    public bool IsServiceAvailable
    {
        get => _isServiceAvailable;
        set
        {
            _isServiceAvailable = value;
            OnPropertyChanged();
        }
    }

    public string AirportName => "Boston Logan International (BOS)";
    public string CurrentTime => DateTime.Now.ToString("HH:mm");
    public string CurrentDate => DateTime.Now.ToString("dddd, MMMM dd, yyyy");
    public string FlightCount => $"{Departures.Count} Flights";

    public ObservableCollection<FlightSchedule> Departures { get; }

    public ICommand RefreshCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand SelectDateCommand { get; }

    private async Task InitializeAsync()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = true;
                StatusMessage = "Checking flight service...";
            });

            IsServiceAvailable = await _flightService.IsServiceAvailableAsync();

            if (!IsServiceAvailable)
            {
                await MainThread.InvokeOnMainThreadAsync(() =>
                {
                    StatusMessage = "Flight service unavailable. Please check your API credentials.";
                    IsLoading = false;
                });
                return;
            }

            await LoadFlights();
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusMessage = $"Error initializing: {ex.Message}";
                IsLoading = false;
            });
            System.Diagnostics.Debug.WriteLine($"Initialization error: {ex.Message}");
        }
    }

    private async Task LoadFlights()
    {
        try
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                IsLoading = true;
                StatusMessage = "Loading flights...";
            });

            var departures = await _flightService.GetDeparturesAsync("BOS", SelectedDate);

            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                Departures.Clear();
                foreach (var departure in departures)
                {
                    Departures.Add(departure);
                }

                StatusMessage = $"Found {departures.Count} flights";
                OnPropertyChanged(nameof(FlightCount));
                IsLoading = false;
            });
        }
        catch (Exception ex)
        {
            await MainThread.InvokeOnMainThreadAsync(() =>
            {
                StatusMessage = $"Error loading flights: {ex.Message}";
                IsLoading = false;
            });
            System.Diagnostics.Debug.WriteLine($"Load flights error: {ex.Message}");
        }
    }

    private async Task RefreshFlights()
    {
        await LoadFlights();
    }

    private async Task SelectDate(DateTime date)
    {
        SelectedDate = date;
        await LoadFlights();
    }

    private async Task GoBack()
    {
        await Shell.Current.GoToAsync("..");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}