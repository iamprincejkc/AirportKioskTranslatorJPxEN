using AirportKiosk.Core.Models;
using AirportKiosk.Services;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Windows;
using System.Windows.Controls;

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
                _logger.LogDebug("Auto-clearing conversation due to inactivity");
                ClearConversation();
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
                "Welcome! Tap the button below to speak in English" :
                "Welcome! Translation service is offline - limited functionality available";

            JapaneseTextBlock.Text = _isApiOnline ?
                "いらっしゃいませ！下のボタンをタップして日本語で話してください" :
                "いらっしゃいませ！翻訳サービスがオフラインです";
        }

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

        private async void EnglishPlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || _lastRecordedAudio == null) return;
            ResetAutoClearTimer();

            await PlayLastRecording();
        }

        #endregion

        #region Japanese Section Events

        private async void JapaneseSpeakButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing) return;
            ResetAutoClearTimer();

            if (!_isListening)
            {
                await StartListening("ja-JP", JapaneseSpeakButton, "🔴 聞いています... タップして停止");
            }
            else
            {
                await StopListening();
            }
        }

        private async void JapanesePlayButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isProcessing || _lastRecordedAudio == null) return;
            ResetAutoClearTimer();

            await PlayLastRecording();
        }

        #endregion

        #region Speech Recognition Methods

        private async Task StartListening(string language, Button button, string listeningText)
        {
            try
            {
                _isListening = true;
                _currentRecordingLanguage = language;

                // Update button appearance
                button.Content = listeningText;
                button.Style = (Style)FindResource("RecordingButtonStyle");

                // Show status
                ShowStatus("Starting speech recognition...");

                // Start speech recognition
                await _speechService.StartListeningAsync(language);

                // Also start audio recording for playback
                await _audioService.StartRecordingAsync();
                _isRecording = true;

                // Update status to show listening is active
                ShowStatus($"🎤 Listening for {(language == "en-US" ? "English" : "Japanese")}... Speak now");

                _logger.LogInformation("Started listening for language: {Language} in session: {SessionId}",
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

                _isListening = false;

                // Reset buttons
                ResetButtons();

                // Show processing status
                ShowStatus("Processing speech...");

                // Stop speech recognition
                await _speechService.StopListeningAsync();

                // Stop audio recording
                if (_isRecording)
                {
                    _lastRecordedAudio = await _audioService.StopRecordingAsync();
                    _isRecording = false;
                }

                _logger.LogInformation("Stopped listening in session: {SessionId}",
                    _conversationManager.CurrentSessionId);

                // If we have recognized text, translate it
                if (!string.IsNullOrEmpty(_lastRecognizedText))
                {
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
                _isListening = false;
                _isRecording = false;
                _currentRecordingLanguage = "";
                _lastRecognizedText = "";

                if (_speechService != null)
                {
                    await _speechService.StopListeningAsync();
                }

                if (_audioService != null && _isRecording)
                {
                    await _audioService.StopRecordingAsync();
                }

                ResetButtons();
                HideStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error resetting listening state");
            }
        }

        private void ResetButtons()
        {
            // Reset English button
            EnglishSpeakButton.Content = "🎤 TAP TO SPEAK ENGLISH";
            EnglishSpeakButton.Style = (Style)FindResource("SpeakButtonStyle");

            // Reset Japanese button
            JapaneseSpeakButton.Content = "🎤 日本語で話す (TAP TO SPEAK)";
            JapaneseSpeakButton.Style = (Style)FindResource("SpeakButtonStyle");
        }

        #endregion

        #region Translation Methods

        private async Task TranslateRecognizedText(string recognizedText)
        {
            try
            {
                ShowStatus("Translating...");

                // Determine source and target languages
                var sourceLanguage = _currentRecordingLanguage == "en-US" ? "en" : "ja";
                var targetLanguage = sourceLanguage == "en" ? "ja" : "en";

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

                    ShowError($"Translation failed: {translationResponse.ErrorMessage}");
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
                    EnglishTextBlock.Text = originalText;
                    JapaneseTextBlock.Text = translatedText;
                }
                else
                {
                    JapaneseTextBlock.Text = originalText;
                    EnglishTextBlock.Text = translatedText;
                }

                // Show play buttons
                EnglishPlayButton.Visibility = Visibility.Visible;
                JapanesePlayButton.Visibility = Visibility.Visible;

                // Add confidence indicator to status (briefly)
                var confidencePercent = (confidence * 100).ToString("F0");
                var statusMessage = _isApiOnline ?
                    $"✅ Translation complete (Confidence: {confidencePercent}%)" :
                    "✅ Speech recognized (Offline mode)";

                ShowStatus(statusMessage);

                // Auto-hide status after 3 seconds
                Task.Delay(3000).ContinueWith(_ => Dispatcher.Invoke(HideStatus));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating translation display");
            }
        }

        #endregion

        #region Event Handlers

        private void OnSpeechRecognized(object sender, SpeechRecognizedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _lastRecognizedText = e.Text;

                    _logger.LogInformation("Speech recognized: '{Text}' (Confidence: {Confidence}) - Session: {SessionId}",
                        e.Text, e.Confidence, _conversationManager.CurrentSessionId);

                    // Update status to show what was recognized
                    ShowStatus($"Recognized: \"{e.Text}\" (Confidence: {e.Confidence:P0})");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling speech recognition result");
                }
            }));
        }

        private void OnSpeechRejected(object sender, SpeechRecognitionRejectedEventArgs e)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                try
                {
                    _logger.LogDebug("Speech recognition rejected: {Reason} - Session: {SessionId}",
                        e.Reason, _conversationManager.CurrentSessionId);

                    ShowStatus($"Speech not clear - {e.Reason}. Please try again.");
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error handling speech recognition rejection");
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
                        var languageName = _currentRecordingLanguage == "en-US" ? "English" : "Japanese";
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

        #region Audio Playback

        private async Task PlayLastRecording()
        {
            try
            {
                if (_lastRecordedAudio == null || _lastRecordedAudio.Length == 0)
                {
                    ShowError("No recorded audio to play.");
                    return;
                }

                ShowStatus("Playing recorded audio...");

                await _audioService.PlayAudioAsync(_lastRecordedAudio);

                HideStatus();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audio playback failed");
                ShowError($"Audio playback failed: {ex.Message}");
            }
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
                // Clear conversation
                _conversationManager.ClearCurrentSession();

                // Reset UI
                ShowWelcomeMessage();

                // Hide play buttons
                EnglishPlayButton.Visibility = Visibility.Collapsed;
                JapanesePlayButton.Visibility = Visibility.Collapsed;

                // Clear recorded data
                _lastRecordedAudio = null;
                _lastRecognizedText = "";

                HideStatus();

                _logger.LogInformation("Conversation auto-cleared - New session: {SessionId}",
                    _conversationManager.CurrentSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error auto-clearing conversation");
            }
        }

        #endregion

        #region Clear/Reset Button

        private async void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Stop any active listening/recording
                if (_isListening)
                {
                    await ResetListeningState();
                }

                // Clear conversation
                ClearConversation();

                // Reset auto-clear timer
                ResetAutoClearTimer();

                _logger.LogInformation("Manual conversation clear - New session: {SessionId}",
                    _conversationManager.CurrentSessionId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error clearing conversation manually");
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