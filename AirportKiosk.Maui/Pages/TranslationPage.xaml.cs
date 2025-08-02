using AirportKiosk.Maui.ViewModels;

namespace AirportKiosk.Maui.Pages;

public partial class TranslationPage : ContentPage
{
    public TranslationPage(TranslationViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = viewModel;
    }
}