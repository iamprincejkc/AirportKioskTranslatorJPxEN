using AirportKiosk.Maui.ViewModels;

namespace AirportKiosk.Maui.Pages;

public partial class TranslationPage : ContentPage
{
    public TranslationPage(TranslationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }

    protected override void OnDisappearing()
    {
        // Ensure we stop listening when navigating away
        if (BindingContext is TranslationViewModel vm && vm.IsListening)
        {
            vm.StopListeningCommand.Execute(null);
        }
        base.OnDisappearing();
    }
}