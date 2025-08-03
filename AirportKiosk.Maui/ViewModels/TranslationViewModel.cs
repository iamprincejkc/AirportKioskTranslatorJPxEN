using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Input;
using System.Collections.ObjectModel;
using AirportKiosk.Maui.Services;

namespace AirportKiosk.Maui.ViewModels;

public class TranslationViewModel : INotifyPropertyChanged
{
    private readonly ITranslationService _translationService;
    private readonly ISpeechService _speechService;
    private string _inputText = string.Empty;
    private string _translatedText = string.Empty;
    private LanguageInfo? _selectedFromLanguage;
    private LanguageInfo? _selectedToLanguage;
    private bool _isTranslating = false;
    private bool _isListening = false;
    private string _statusMessage = "Ready to translate";
    private CancellationTokenSource? _listeningCancellationSource;

    public TranslationViewModel(ITranslationService translationService, ISpeechService speechService)
    {
        _translationService = translationService;
        _speechService = speechService;

        // Initialize commands
        TranslateTextCommand = new Command(async () => await TranslateText());
        ClearTextCommand = new Command(ClearText);
        SwapLanguagesCommand = new Command(SwapLanguages);
        CopyTranslationCommand = new Command(async () => await CopyTranslation());
        GoBackCommand = new Command(async () => await GoBack());
        StartListeningCommand = new Command(async () => await StartListening(), () => !IsListening && IsSpeechSupported);
        StopListeningCommand = new Command(async () => await StopListening(), () => IsListening);

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
            ((Command)StartListeningCommand).ChangeCanExecute();
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

    public bool IsListening
    {
        get => _isListening;
        set
        {
            _isListening = value;
            OnPropertyChanged();
            ((Command)StartListeningCommand).ChangeCanExecute();
            ((Command)StopListeningCommand).ChangeCanExecute();
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

    public bool IsSpeechSupported => _speechService.IsSupported;

    public ObservableCollection<LanguageInfo> Languages { get; }

    public ICommand TranslateTextCommand { get; }
    public ICommand ClearTextCommand { get; }
    public ICommand SwapLanguagesCommand { get; }
    public ICommand CopyTranslationCommand { get; }
    public ICommand GoBackCommand { get; }
    public ICommand StartListeningCommand { get; }
    public ICommand StopListeningCommand { get; }

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

    private async Task StartListening()
    {
        if (SelectedFromLanguage == null)
        {
            StatusMessage = "Please select a source language first";
            return;
        }

        try
        {
            _listeningCancellationSource = new CancellationTokenSource();
            IsListening = true;
            StatusMessage = "Listening... Speak now";

            var result = await _speechService.ListenAsync(
                SelectedFromLanguage.Code,
                _listeningCancellationSource.Token);

            if (result.IsSuccessful && !string.IsNullOrWhiteSpace(result.Text))
            {
                InputText = result.Text;
                StatusMessage = "Speech recognized. Ready to translate.";

                // Auto-translate if we have both languages selected
                if (SelectedToLanguage != null)
                {
                    await TranslateText();
                }
            }
            else
            {
                StatusMessage = result.ErrorMessage ?? "No speech recognized. Try again.";
            }
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Speech recognition cancelled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Speech recognition error: {ex.Message}";
        }
        finally
        {
            IsListening = false;
            _listeningCancellationSource?.Dispose();
            _listeningCancellationSource = null;
        }
    }

    private async Task StopListening()
    {
        try
        {
            _listeningCancellationSource?.Cancel();
            await _speechService.StopListeningAsync();
            StatusMessage = "Speech recognition stopped";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Error stopping speech recognition: {ex.Message}";
        }
        finally
        {
            IsListening = false;
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
        // Stop listening if active
        if (IsListening)
        {
            await StopListening();
        }

        await Shell.Current.GoToAsync("..");
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}