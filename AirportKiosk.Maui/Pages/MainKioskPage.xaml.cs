using AirportKiosk.Maui.ViewModels;

namespace AirportKiosk.Maui.Pages;

public partial class MainKioskPage : ContentPage
{
    public MainKioskPage(MainKioskViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}