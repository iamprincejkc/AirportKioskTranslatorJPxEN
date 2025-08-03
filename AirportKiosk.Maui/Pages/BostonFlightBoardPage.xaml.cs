using AirportKiosk.Maui.ViewModels;

namespace AirportKiosk.Maui.Pages;

public partial class BostonFlightBoardPage : ContentPage
{
    public BostonFlightBoardPage(BostonFlightBoardViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}