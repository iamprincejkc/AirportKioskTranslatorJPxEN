using System;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
using AirportKiosk.Services;
using System.Windows.Media;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using AirportKiosk.Core.Models;
using System.Linq;

namespace AirportKiosk.WPF
{
    public partial class MainWindow : Window
    {
        private readonly IAudioService _audioService;
        private readonly ISpeechRecognitionService _speechService;
        private readonly IApiClientService _apiClient;
        private readonly IConversationManager _conversationManager;
        private readonly ILogger<MainWindow> _logger;
        private readonly IConfiguration _configuration;

        private bool _isRecording = false;
        private bool _isProcessing = false;
        private bool _isListening = false;
        private string _currentRecordingLanguage = "";
        private string _selectedTargetLanguage = "";
        private string _targetLanguageCode = "";
        private byte[] _lastRecordedAudio;
        private string _lastRecognizedText = "";
        private bool _isApiOnline = true;

        // Auto-clear timer
        private System.Windows.Threading.DispatcherTimer _autoClearTimer;
        private readonly TimeSpan _autoClearTimeout;

        public MainWindow(
            IAudioService audioService,
            ISpeechRecognitionService speechService,
            IApiClientService apiClient,
            IConversationManager conversationManager,
            ILogger<MainWindow> logger,
            IConfiguration configuration)
        {
            InitializeComponent();

            _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
            _speechService = speechService ?? throw new ArgumentNullException(nameof(speechService));
            _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
            _conversationManager = conversationManager ?? throw new ArgumentNullException(nameof(conversationManager));
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Load configuration
            _autoClearTimeout = TimeSpan.FromSeconds(_configuration.GetValue<int>("UI:AutoClearTimeout", 300));

            InitializeWindow();
            InitializeServices();
            SetupAutoClearTimer();
        }

        #region Window Initialization

        private void InitializeWindow()
        {
            // Set window to full screen and disable system buttons
            var fullScreen = _configuration.GetValue<bool>("UI:FullScreen", true);
            if (fullScreen)
            {
                WindowState = WindowState.Maximized;
                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
            }

            // Center the window on screen
            Left = 0;
            Top = 0;
            Width = SystemParameters.PrimaryScreenWidth;
            Height = SystemParameters.PrimaryScreenHeight;

            _logger.LogInformation("Main window initialized - Session: {SessionId}",
                _conversationManager.CurrentSessionId);
        }

        private async void InitializeServices()
        {
            try
            {
                ShowStatus("Initializing kiosk services...");

                // Subscribe to events
                _audioService.AudioLevelChanged += OnAudioLevelChanged;
                _audioService.AudioDeviceError += OnAudioDeviceError;

                _speechService.SpeechRecognized += OnSpeechRecognized;
                _speechService.SpeechRejected += OnSpeechRejected;
                _speechService.AudioLevelChanged += OnSpeechAudioLevelChanged;
                _speechService.RecognitionError += OnRecognitionError;

                _apiClient.ConnectionStatusChanged += OnApiConnectionStatusChanged;

                _conversationManager.EntryAdded += OnConversationEntryAdded;
                _conversationManager.SessionCleared += OnSessionCleared;

                // Initialize audio system
                var audioInitialized = await _audioService.InitializeAsync();
                if (!audioInitialized)
                {
                    ShowError("Failed to initialize audio system. Please check your microphone and speakers.");
                    return;
                }

                // Initialize speech recognition
                var speechInitialized = await _speechService.InitializeAsync();
                if (!speechInitialized)
                {
                    ShowError("Failed to initialize speech recognition. Please check Windows Speech Recognition is enabled.");
                    return;
                }

                // Check API availability
                ShowStatus("Connecting to translation service...");
                var apiAvailable = await _apiClient.IsApiAvailableAsync();
                if (!apiAvailable)
                {
                    ShowWarning("Translation API is not available. The kiosk will work with limited functionality.");
                    _isApiOnline = false;
                }

                // Log system status
                LogSystemStatus();

                HideStatus();
                ShowWelcomeMessage();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize services");
                ShowError($"Service initialization error: {ex.Message}");
            }
        }

        private void SetupAutoClearTimer()
        {
            _autoClearTimer = new System.Windows.Threading.DispatcherTimer();
            _autoClearTimer.Interval = _autoClearTimeout;
            _autoClearTimer.Tick += OnAutoClearTimer;
            _autoClearTimer.Start();
        }

        private void OnAutoClearTimer(object sender, EventArgs e)
        {
            if (!_isListening && !_isProcessing)
            {
                _logger.LogDebug("Auto-resetting conversation due to inactivity");
                Dispatcher.BeginInvoke(new Action(async () =>
                {
                    try
                    {
                        await ResetListeningState();
                        ClearConversation();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error during auto-reset");
                    }
                }));
            }
            else
            {
                // Reset timer if user is active
                _autoClearTimer.Stop();
                _autoClearTimer.Start();
            }
        }

        private void ShowWelcomeMessage()
        {
            EnglishTextBlock.Text = _isApiOnline ?
                "Select a language above, then tap the button below to speak in English" :
                "Translation service is offline - Select a language above first";

            if (string.IsNullOrEmpty(_selectedTargetLanguage))
            {
                TargetLanguageTextBlock.Text = "Choose your preferred language from the options above";
            }
        }

        #endregion

        #region Language Selection Events

        private void ItalianButton_Click(object sender, RoutedEventArgs e)
        {
            SetTargetLanguage("ITALIANO", "it", "🇮🇹", "#27ae60");
        }

        private void JapaneseButton_Click(object sender, RoutedEventArgs e)
        {
            SetTargetLanguage("日本語 (JAPANESE)", "ja", "🇯🇵", "#e74c3c");
        }

        private void KoreanButton_Click(object sender, RoutedEventArgs e)
        {
            SetTargetLanguage("한국어 (KOREAN)", "ko", "🇰🇷", "#3498db");
        }

        private void SetTargetLanguage(string displayName, string languageCode, string flag, string color)
        {
            _selectedTargetLanguage = displayName;
            _targetLanguageCode = languageCode;

            // Update UI
            TargetLanguageTitle.Text = displayName;
            TargetLanguageTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

            // Update target language content based on selected language
            var instructions = languageCode switch
            {
                "it" => "Tocca il pulsante qui sotto per parlare in italiano",
                "ja" => "下のボタンをタップして日本語で話してください",
                "ko" => "아래 버튼을 탭하여 한국어로 말하세요",
                _ => "Tap the button below to speak"
            };

            TargetLanguageTextBlock.Text = instructions;

            // Enable and update target language button
            var buttonText = languageCode switch
            {
                "it" => $"{flag} TOCCA PER PARLARE ITALIANO",
                "ja" => $"{flag} 日本語で話す (TAP TO SPEAK)",
                "ko" => $"{flag} 한국어로 말하기 (TAP TO SPEAK)",
                _ => $"{flag} TAP TO SPEAK"
            };

            TargetLanguageSpeakButton.Content = buttonText;
            TargetLanguageSpeakButton.IsEnabled = true;
            TargetLanguageSpeakButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));

            _logger.LogInformation("Target language selected: {Language} ({Code})", displayName, languageCode);
            ResetAutoClearTimer();
        }

        #endregion

        #region English Section Events

        private async void EnglishSpeakButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            ResetAutoClearTimer();

            if (!_isListening)
            {
                await StartListening("en-US", EnglishSpeakButton, "🔴 LISTENING... TAP TO STOP");
            }
            else
            {
                await StopListening();
            }
        }

        #endregion

        #region Target Language Section Events

        private async void TargetLanguageSpeakButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || string.IsNullOrEmpty(_targetLanguageCode)) return;
            ResetAutoClearTimer();

            var languageMapping = _targetLanguageCode switch
            {
                "ja" => "ja-JP",
                "it" => "it-IT",
                "ko" => "ko-KR",
                _ => "ja-JP"
            };

            var listeningText = _targetLanguageCode switch
            {
                "it" => "🔴 ASCOLTANDO... TOCCA PER FERMARE",
                "ja" => "🔴 聞いています... タップして停止",
                "ko" => "🔴 듣고 있습니다... 탭하여 중지",
                _ => "🔴 LISTENING... TAP TO STOP"
            };

            if (!_isListening)
            {
                await StartListening(languageMapping, TargetLanguageSpeakButton, listeningText);
            }
            else
            {
                await StopListening();
            }
        }

        #endregion

        #region Speech Recognition Methods

        private async Task StartListening(string language, Button button, string listeningText)
        {
            try
            {
                // Check prerequisites
                if (!_speechService.GetAvailableRecognizers().Any())
                {
                    ShowError("No speech recognition engines available. Please check Windows Speech Recognition is installed.");
                    return;
                }

                if (_isListening)
                {
                    _logger.LogWarning("Already listening - stopping current session first");
                    await StopListening();
                    await Task.Delay(500); // Give time for cleanup
                }

                _logger.LogInformation("Starting listening for language: {Language}", language);

                _isListening = true;
                _currentRecordingLanguage = language;

                // Update button appearance
                button.Content = listeningText;
                button.Style = (Style)FindResource("RecordingButtonStyle");

                // Show status
                ShowStatus("Starting speech recognition...");

                // Start speech recognition first
                await _speechService.StartListeningAsync(language);
                _logger.LogDebug("Speech recognition started successfully");

                // Then start audio recording for playback
                try
                {
                    await _audioService.StartRecordingAsync();
                    _isRecording = true;
                    _logger.LogDebug("Audio recording started successfully");
                }
                catch (Exception audioEx)
                {
                    _logger.LogWarning(audioEx, "Failed to start audio recording, continuing with speech recognition only");
                    _isRecording = false;
                }

                // Update status to show listening is active
                var languageName = language switch
                {
                    "en-US" => "English",
                    "ja-JP" => "Japanese",
                    "it-IT" => "Italian",
                    "ko-KR" => "Korean",
                    _ => "Unknown"
                };

                ShowStatus($"🎤 Listening for {languageName}... Speak now");

                _logger.LogInformation("Successfully started listening for language: {Language} in session: {SessionId}",
                    language, _conversationManager.CurrentSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start listening for language: {Language}", language);
                ShowError($"Failed to start listening: {ex.Message}");
                await ResetListeningState();
            }
        }

        private async Task StopListening()
        {
            try
            {
                if (!_isListening) return;

                _logger.LogInformation("Stopping listening in session: {SessionId}",
                    _conversationManager.CurrentSessionId);

                _isListening = false;

                // Reset buttons first
                ResetButtons();

                // Stop speech recognition
                await _speechService.StopListeningAsync();

                // Stop audio recording
                if (_isRecording)
                {
                    _lastRecordedAudio = await _audioService.StopRecordingAsync();
                    _isRecording = false;
                }

                // If we have recognized text, translate it
                if (!string.IsNullOrEmpty(_lastRecognizedText))
                {
                    ShowStatus($"Translating: \"{_lastRecognizedText}\"...");
                    await TranslateRecognizedText(_lastRecognizedText);
                }
                else
                {
                    ShowStatus("No speech recognized. Please try speaking more clearly.");
                    await Task.Delay(3000);
                    HideStatus();
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping listening");
                ShowError($"Processing failed: {ex.Message}");
                await ResetListeningState();
            }
        }

        private async Task ResetListeningState()
        {
            try
            {
                _logger.LogDebug("Resetting listening state");

                _isListening = false;
                _isRecording = false;
                _currentRecordingLanguage = "";
                _lastRecognizedText = "";

                // Stop services safely
                try
                {
                    if (_speechService != null)
                    {
                        await _speechService.StopListeningAsync();
                        _logger.LogDebug("Speech service stopped successfully");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping speech service during reset");
                }

                try
                {
                    if (_audioService != null)
                    {
                        await _audioService.StopRecordingAsync();
                        _logger.LogDebug("Audio service stopped successfully");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Error stopping audio service during reset");
                }

                ResetButtons();
                HideStatus();

                _logger.LogDebug("Listening state reset completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting listening state");

                // Force reset the state even if there were errors
                _isListening = false;
                _isRecording = false;
                _currentRecordingLanguage = "";
                _lastRecognizedText = "";

                ResetButtons();
                HideStatus();
            }
        }

        private void ResetButtons()
        {
            // Reset English button
            EnglishSpeakButton.Content = "🎤 TAP TO SPEAK ENGLISH";
            EnglishSpeakButton.Style = (Style)FindResource("SpeakButtonStyle");

            // Reset target language button
            if (!string.IsNullOrEmpty(_targetLanguageCode))
            {
                var flag = _targetLanguageCode switch
                {
                    "it" => "🇮🇹",
                    "ja" => "🇯🇵",
                    "ko" => "🇰🇷",
                    _ => "🎤"
                };

                var buttonText = _targetLanguageCode switch
                {
                    "it" => $"{flag} TOCCA PER PARLARE ITALIANO",
                    "ja" => $"{flag} 日本語で話す (TAP TO SPEAK)",
                    "ko" => $"{flag} 한국어로 말하기 (TAP TO SPEAK)",
                    _ => $"{flag} TAP TO SPEAK"
                };

                TargetLanguageSpeakButton.Content = buttonText;
                TargetLanguageSpeakButton.Style = (Style)FindResource("SpeakButtonStyle");
            }
        }

        #endregion

        #region Translation Methods

        private async Task TranslateRecognizedText(string recognizedText)
        {
            try
            {
                if (string.IsNullOrEmpty(_targetLanguageCode))
                {
                    ShowError("Please select a target language first.");
                    return;
                }

                ShowStatus("Translating...");

                // Determine source and target languages
                var sourceLanguage = _currentRecordingLanguage == "en-US" ? "en" : _targetLanguageCode;
                var targetLanguage = sourceLanguage == "en" ? _targetLanguageCode : "en";

                _logger.LogInformation("Translating: '{Text}' from {Source} to {Target} - Session: {SessionId}",
                    recognizedText, sourceLanguage, targetLanguage, _conversationManager.CurrentSessionId);

                TranslationResponse translationResponse;

                if (_isApiOnline)
                {
                    // Call translation API
                    translationResponse = await _apiClient.TranslateTextAsync(
                        recognizedText, sourceLanguage, targetLanguage, _conversationManager.CurrentSessionId);
                }
                else
                {
                    // Offline mode - just echo the text
                    translationResponse = new TranslationResponse
                    {
                        TranslatedText = $"[OFFLINE] {recognizedText}",
                        SourceLanguage = sourceLanguage,
                        TargetLanguage = targetLanguage,
                        Success = true,
                        Confidence = 0.5f,
                        Provider = "Offline Mode"
                    };
                }

                if (translationResponse.Success)
                {
                    // Add to conversation history
                    _conversationManager.AddEntry(
                        recognizedText,
                        translationResponse.TranslatedText,
                        sourceLanguage,
                        targetLanguage,
                        translationResponse.Confidence);

                    // Update UI with results
                    UpdateTranslationDisplay(recognizedText, translationResponse.TranslatedText,
                        sourceLanguage, targetLanguage, translationResponse.Confidence);

                    _logger.LogInformation("Translation completed successfully - Confidence: {Confidence}, " +
                                         "Duration: {Duration}ms - Session: {SessionId}",
                        translationResponse.Confidence, translationResponse.ProcessingTime.TotalMilliseconds,
                        _conversationManager.CurrentSessionId);
                }
                else
                {
                    _logger.LogWarning("Translation failed: {Error} - Session: {SessionId}",
                        translationResponse.ErrorMessage, _conversationManager.CurrentSessionId);

                    // Show the original text even if translation failed
                    UpdateTranslationDisplayWithError(recognizedText, translationResponse.ErrorMessage,
                        sourceLanguage, targetLanguage);

                    // Also show error popup for user awareness
                    ShowTranslationError($"Translation failed: {translationResponse.ErrorMessage}");
                }

                HideStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during translation process");
                ShowError($"Translation error: {ex.Message}");
            }
        }

        private void UpdateTranslationDisplay(string originalText, string translatedText,
            string sourceLanguage, string targetLanguage, float confidence)
        {
            try
            {
                // Update text displays based on source language
                if (sourceLanguage == "en")
                {
                    EnglishTextBlock.Text = $"You said: \"{originalText}\"";
                    TargetLanguageTextBlock.Text = $"Translation: \"{translatedText}\"";
                }
                else
                {
                    TargetLanguageTextBlock.Text = $"You said: \"{originalText}\"";
                    EnglishTextBlock.Text = $"Translation: \"{translatedText}\"";
                }

                // Add confidence indicator to status (briefly)
                var confidencePercent = (confidence * 100).ToString("F0");
                var statusMessage = _isApiOnline ?
                    $"✅ Translation complete (Confidence: {confidencePercent}%)" :
                    "✅ Speech recognized (Offline mode)";

                ShowStatus(statusMessage);

                // Auto-hide status after 4 seconds
                Task.Delay(4000).ContinueWith(_ => Dispatcher.Invoke(HideStatus));

                _logger.LogInformation("UI updated - Original: '{Original}' → Translation: '{Translation}'",
                    originalText, translatedText);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating translation display");
            }
        }

        private void UpdateTranslationDisplayWithError(string originalText, string errorMessage,
            string sourceLanguage, string targetLanguage)
        {
            try
            {
                // Update text displays based on source language
                if (sourceLanguage == "en")
                {
                    EnglishTextBlock.Text = $"You said: \"{originalText}\"";
                    TargetLanguageTextBlock.Text = $"❌ Translation Error: {errorMessage}";
                }
                else
                {
                    TargetLanguageTextBlock.Text = $"You said: \"{originalText}\"";
                    EnglishTextBlock.Text = $"❌ Translation Error: {errorMessage}";
                }

                ShowStatus("❌ Translation failed - please try again");

                // Auto-hide status after 4 seconds
                Task.Delay(4000).ContinueWith(_ => Dispatcher.Invoke(HideStatus));

                _logger.LogInformation("UI updated with translation error - Original: '{Original}', Error: '{Error}'",
                    originalText, errorMessage);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating translation display with error");
            }
        }

        private void ShowTranslationError(string message)
        {
            try
            {
                _logger.LogWarning("Showing translation error to user: {Message}", message);

                // Show a less intrusive error message
                ShowStatus($"⚠️ {message} - Speech was recognized correctly");

                // Auto-hide after 6 seconds
                Task.Delay(6000).ContinueWith(_ => Dispatcher.Invoke(() =>
                {
                    if (StatusText.Text.Contains("Translation failed"))
                    {
                        HideStatus();
                    }
                }));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error showing translation error");
            }
        }

        #endregion

        #region Event Handlers

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    _lastRecognizedText = e.Text;

                    _logger.LogInformation("Speech recognized: '{Text}' (Confidence: {Confidence}) - Session: {SessionId}",
                        e.Text, e.Confidence, _conversationManager.CurrentSessionId);

                    // Show what was recognized immediately
                    ShowStatus($"Recognized: \"{e.Text}\" - Processing...");

                    // Auto-stop listening after recognizing speech
                    if (_isListening)
                    {
                        _logger.LogInformation("Auto-stopping speech recognition after successful recognition");
                        await StopListening();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling speech recognition result");
                }
            }));
        }

        private void OnSpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(async () =>
            {
                try
                {
                    _logger.LogDebug("Speech recognition rejected: {Reason} - Session: {SessionId}",
                        e.Reason, _conversationManager.CurrentSessionId);

                    ShowStatus($"Speech not clear - {e.Reason}. Please try again...");
                    await Task.Delay(2000);

                    // Auto-restart listening if we're still in listening mode
                    if (!_isListening && !string.IsNullOrEmpty(_currentRecordingLanguage))
                    {
                        _logger.LogInformation("Auto-restarting speech recognition after rejection");

                        Button button;
                        string listeningText;

                        if (_currentRecordingLanguage == "en-US")
                        {
                            button = EnglishSpeakButton;
                            listeningText = "🔴 LISTENING... TAP TO STOP";
                        }
                        else
                        {
                            button = TargetLanguageSpeakButton;
                            listeningText = _targetLanguageCode switch
                            {
                                "it" => "🔴 ASCOLTANDO... TOCCA PER FERMARE",
                                "ja" => "🔴 聞いています... タップして停止",
                                "ko" => "🔴 듣고 있습니다... 탭하여 중지",
                                _ => "🔴 LISTENING... TAP TO STOP"
                            };
                        }

                        await StartListening(_currentRecordingLanguage, button, listeningText);
                    }
                    else
                    {
                        HideStatus();
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling speech recognition rejection");
                    HideStatus();
                }
            }));
        }

        private void OnSpeechAudioLevelChanged(object sender, AudioLevelUpdatedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    // Update UI with audio level visualization
                    if (_isListening && e.AudioLevel > 0)
                    {
                        var levelBars = new string('▊', Math.Min(10, e.AudioLevel / 10));
                        var languageName = _currentRecordingLanguage switch
                        {
                            "en-US" => "English",
                            "ja-JP" => "Japanese",
                            "it-IT" => "Italian",
                            "ko-KR" => "Korean",
                            _ => "Unknown"
                        };
                        StatusText.Text = $"🎤 Listening for {languageName}... {levelBars}";
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error updating speech audio level");
                }
            }));
        }

        private void OnRecognitionError(object sender, string errorMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logger.LogError("Speech recognition error: {Error} - Session: {SessionId}",
                    errorMessage, _conversationManager.CurrentSessionId);
                ShowError($"Speech recognition error: {errorMessage}");
            }));
        }

        private void OnAudioLevelChanged(object sender, AudioLevelEventArgs e)
        {
            // This is for the raw audio recording level
            // We primarily use the speech recognition audio level for UI updates
        }

        private void OnAudioDeviceError(object sender, string errorMessage)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _logger.LogError("Audio device error: {Error} - Session: {SessionId}",
                    errorMessage, _conversationManager.CurrentSessionId);
                ShowError($"Audio Error: {errorMessage}");
            }));
        }

        private void OnApiConnectionStatusChanged(object sender, string status)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                _isApiOnline = status == "Online";
                _logger.LogInformation("API connection status: {Status}", status);

                if (!_isApiOnline)
                {
                    ShowWarning("Translation service is offline. Speech recognition will continue but translation is limited.");
                }
            }));
        }

        private void OnConversationEntryAdded(object sender, ConversationEntry entry)
        {
            _logger.LogDebug("Conversation entry added: {EntryId} - {OriginalText} → {TranslatedText}",
                entry.Id, entry.OriginalText, entry.TranslatedText);
        }

        private void OnSessionCleared(object sender, EventArgs e)
        {
            _logger.LogInformation("Conversation session cleared");
        }

        #endregion

        #region UI Status Methods

        private void ShowStatus(string message)
        {
            _isProcessing = true;
            StatusText.Text = message;
            StatusOverlay.Visibility = Visibility.Visible;
        }

        private void HideStatus()
        {
            _isProcessing = false;
            StatusOverlay.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region Error Handling

        private void ShowError(string message)
        {
            _logger.LogWarning("Showing error to user: {Message}", message);
            MessageBox.Show(message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            HideStatus();
            _isProcessing = false;
        }

        private void ShowWarning(string message)
        {
            _logger.LogInformation("Showing warning to user: {Message}", message);
            MessageBox.Show(message, "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        #endregion

        #region Utility Methods

        private void ResetAutoClearTimer()
        {
            _autoClearTimer?.Stop();
            _autoClearTimer?.Start();
        }

        private void LogSystemStatus()
        {
            var inputDevices = _audioService.GetAvailableInputDevices();
            var outputDevices = _audioService.GetAvailableOutputDevices();
            var recognizers = _speechService.GetAvailableRecognizers();

            _logger.LogInformation("System status - Audio Input: {InputCount}, Audio Output: {OutputCount}, " +
                                 "Speech Recognizers: {RecognizerCount}, API Online: {ApiOnline}",
                inputDevices.Count, outputDevices.Count, recognizers.Count, _isApiOnline);
        }

        private void ClearConversation()
        {
            try
            {
                _logger.LogDebug("Clearing conversation and resetting UI");

                // Clear conversation
                _conversationManager.ClearCurrentSession();

                // Reset UI to initial state
                ShowWelcomeMessage();

                // Clear recorded data
                _lastRecordedAudio = null;
                _lastRecognizedText = "";

                // Reset language selection if needed
                if (string.IsNullOrEmpty(_selectedTargetLanguage))
                {
                    TargetLanguageTitle.Text = "SELECT LANGUAGE ABOVE";
                    TargetLanguageTextBlock.Text = "Choose your preferred language from the options above";
                    TargetLanguageSpeakButton.Content = "🎤 SELECT LANGUAGE FIRST";
                    TargetLanguageSpeakButton.IsEnabled = false;
                    TargetLanguageSpeakButton.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#95a5a6"));
                }

                HideStatus();

                _logger.LogInformation("Conversation cleared - New session: {SessionId}",
                    _conversationManager.CurrentSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing conversation");
            }
        }

        #endregion

        #region Reset Button

        private async void ResetButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _logger.LogInformation("Reset button clicked - performing full reset");

                // Stop any active listening/recording first
                if (_isListening || _isRecording)
                {
                    ShowStatus("Stopping current session...");
                    await ResetListeningState();
                    await Task.Delay(500); // Give time for cleanup
                }

                // Clear conversation and reset UI
                ClearConversation();

                // Reset auto-clear timer
                ResetAutoClearTimer();

                ShowStatus("✅ Reset complete");
                await Task.Delay(1500);
                HideStatus();

                _logger.LogInformation("Full reset completed - New session: {SessionId}",
                    _conversationManager.CurrentSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during reset");
                ShowError($"Reset failed: {ex.Message}");
            }
        }

        #endregion

        #region Window Events

        protected override void OnKeyDown(System.Windows.Input.KeyEventArgs e)
        {
            // Allow ESC key to exit in development mode
            if (e.Key == System.Windows.Input.Key.Escape)
            {
                this.Close();
            }

            base.OnKeyDown(e);
        }

        protected override async void OnClosed(EventArgs e)
        {
            try
            {
                _logger.LogInformation("Application closing - Session: {SessionId}",
                    _conversationManager.CurrentSessionId);

                // Stop timer
                _autoClearTimer?.Stop();

                // Cleanup services
                if (_isListening)
                {
                    await _speechService.StopListeningAsync();
                }

                if (_isRecording)
                {
                    await _audioService.StopRecordingAsync();
                }

                _speechService?.Dispose();
                _audioService?.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during cleanup");
            }

            base.OnClosed(e);
        }

        #endregion
    }
}