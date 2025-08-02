using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Collections.ObjectModel;
using AirportKiosk.Maui.Services;

namespace AirportKiosk.Maui.ViewModels;

public class TranslationViewModel : INotifyPropertyChanged
{
    private readonly ITranslationService _translationService;
    private string _inputText = string.Empty;
    private string _translatedText = string.Empty;
    private LanguageInfo? _selectedFromLanguage;
    private LanguageInfo? _selectedToLanguage;
    private bool _isTranslating = false;
    private string _statusMessage = "Ready to translate";

    public TranslationViewModel(ITranslationService translationService)
    {
        _translationService = translationService;

        // Initialize commands
        TranslateTextCommand = new Command(async () => await TranslateText());
        ClearTextCommand = new Command(ClearText);
        SwapLanguagesCommand = new Command(SwapLanguages);
        CopyTranslationCommand = new Command(async () => await CopyTranslation());
        GoBackCommand = new Command(async () => await GoBack());

        Languages = new ObservableCollection<LanguageInfo>();
        LoadLanguages();
    }

    public string InputText
    {
        get => _inputText;
        set
        {
            _inputText = value;
            OnPropertyChanged();
        }
    }

    public string TranslatedText
    {
        get => _translatedText;
        set
        {
            _translatedText = value;
            OnPropertyChanged();
        }
    }

    public LanguageInfo? SelectedFromLanguage
    {
        get => _selectedFromLanguage;
        set
        {
            _selectedFromLanguage = value;
            OnPropertyChanged();
        }
    }

    public LanguageInfo? SelectedToLanguage
    {
        get => _selectedToLanguage;
        set
        {
            _selectedToLanguage = value;
            OnPropertyChanged();
        }
    }

    public bool IsTranslating
    {
        get => _isTranslating;
        set
        {
            _isTranslating = value;
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

    public ObservableCollection<LanguageInfo> Languages { get; }

    public ICommand TranslateTextCommand { get; }
    public ICommand ClearTextCommand { get; }
    public ICommand SwapLanguagesCommand { get; }
    public ICommand CopyTranslationCommand { get; }
    public ICommand GoBackCommand { get; }

    private async void LoadLanguages()
    {
        try
        {
            var languages = await _translationService.GetSupportedLanguagesAsync();
            foreach (var language in languages)
            {
                Languages.Add(language);
            }

            // Set default languages
            SelectedFromLanguage = Languages.FirstOrDefault(l => l.Code == "en");
            SelectedToLanguage = Languages.FirstOrDefault(l => l.Code == "es");
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error loading languages: {ex.Message}";
        }
    }

    private async Task TranslateText()
    {
        if (string.IsNullOrWhiteSpace(InputText) ||
            SelectedFromLanguage == null ||
            SelectedToLanguage == null)
        {
            StatusMessage = "Please enter text and select languages";
            return;
        }

        if (SelectedFromLanguage.Code == SelectedToLanguage.Code)
        {
            TranslatedText = InputText;
            StatusMessage = "Same language selected";
            return;
        }

        try
        {
            IsTranslating = true;
            StatusMessage = "Translating...";

            var result = await _translationService.TranslateTextAsync(
                InputText,
                SelectedFromLanguage.Code,
                SelectedToLanguage.Code);

            TranslatedText = result;
            StatusMessage = "Translation completed";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Translation error: {ex.Message}";
            TranslatedText = string.Empty;
        }
        finally
        {
            IsTranslating = false;
        }
    }

    private void ClearText()
    {
        InputText = string.Empty;
        TranslatedText = string.Empty;
        StatusMessage = "Ready to translate";
    }

    private void SwapLanguages()
    {
        if (SelectedFromLanguage != null && SelectedToLanguage != null)
        {
            var temp = SelectedFromLanguage;
            SelectedFromLanguage = SelectedToLanguage;
            SelectedToLanguage = temp;

            // If we have translation, swap the texts too
            if (!string.IsNullOrWhiteSpace(TranslatedText))
            {
                var tempText = InputText;
                InputText = TranslatedText;
                TranslatedText = tempText;
            }
        }
    }

    private async Task CopyTranslation()
    {
        if (!string.IsNullOrWhiteSpace(TranslatedText))
        {
            await Clipboard.SetTextAsync(TranslatedText);
            StatusMessage = "Translation copied to clipboard";
        }
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