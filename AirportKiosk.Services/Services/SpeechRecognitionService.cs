using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Speech.Recognition;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.IO;

namespace AirportKiosk.Services
{
    public interface ISpeechRecognitionService
    {
        event EventHandler<SpeechRecognizedEventArgs> SpeechRecognized;
        event EventHandler<SpeechRecognitionRejectedEventArgs> SpeechRejected;
        event EventHandler<AudioLevelUpdatedEventArgs> AudioLevelChanged;
        event EventHandler<string> RecognitionError;

        Task<bool> InitializeAsync();
        Task StartListeningAsync(string language = "en-US");
        Task StopListeningAsync();
        Task<string> RecognizeSpeechFromAudioAsync(byte[] audioData, string language = "en-US");
        List<RecognizerInfo> GetAvailableRecognizers();
        bool IsLanguageSupported(string language);
        void Dispose();
    }

    public class SpeechRecognizedEventArgs : EventArgs
    {
        public string Text { get; set; }
        public float Confidence { get; set; }
        public string Language { get; set; }
        public TimeSpan RecognitionTime { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class SpeechRecognitionRejectedEventArgs : EventArgs
    {
        public string Reason { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class AudioLevelUpdatedEventArgs : EventArgs
    {
        public int AudioLevel { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
    }

    public class WindowsSpeechRecognitionService : ISpeechRecognitionService, IDisposable
    {
        private SpeechRecognitionEngine _recognitionEngine;
        private readonly ILogger<WindowsSpeechRecognitionService> _logger;
        private readonly IConfiguration _configuration;
        private bool _isInitialized = false;
        private bool _isListening = false;
        private string _currentLanguage = "en-US";
        private readonly object _lockObject = new object();

        // Configuration fields
        private readonly float _confidenceThreshold;
        private readonly int _maxSpeechTimeoutMs;
        private readonly int _initialSilenceTimeoutMs;
        private readonly bool _enableDebugging;
        private readonly bool _useSharedRecognizer; // Added this missing field

        public event EventHandler<SpeechRecognizedEventArgs> SpeechRecognized;
        public event EventHandler<SpeechRecognitionRejectedEventArgs> SpeechRejected;
        public event EventHandler<AudioLevelUpdatedEventArgs> AudioLevelChanged;
        public event EventHandler<string> RecognitionError;

        public WindowsSpeechRecognitionService(
            ILogger<WindowsSpeechRecognitionService> logger,
            IConfiguration configuration)
        {
            _logger = logger ?? throw new ArgumentNullException(nameof(logger));
            _configuration = configuration ?? throw new ArgumentNullException(nameof(configuration));

            // Load configuration
            _confidenceThreshold = _configuration.GetValue<float>("SpeechRecognition:ConfidenceThreshold", 0.4f);
            _maxSpeechTimeoutMs = _configuration.GetValue<int>("SpeechRecognition:MaxSpeechTimeoutMs", 15000);
            _initialSilenceTimeoutMs = _configuration.GetValue<int>("SpeechRecognition:InitialSilenceTimeoutMs", 5000);
            _enableDebugging = _configuration.GetValue<bool>("SpeechRecognition:EnableDebugging", true);
            _useSharedRecognizer = _configuration.GetValue<bool>("SpeechRecognition:UseSharedRecognizer", true); // Added this line

            _logger.LogInformation("Speech Recognition initialized - Confidence threshold: {Threshold}, Debugging: {Debug}, UseSharedRecognizer: {UseShared}",
                _confidenceThreshold, _enableDebugging, _useSharedRecognizer);
        }

        public async Task<bool> InitializeAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_isInitialized)
                        {
                            _logger.LogWarning("Speech recognition service already initialized");
                            return;
                        }

                        _logger.LogInformation("Initializing Windows Speech Recognition");

                        // Check available recognizers
                        var recognizers = SpeechRecognitionEngine.InstalledRecognizers();
                        if (!recognizers.Any())
                        {
                            throw new InvalidOperationException("No speech recognition engines installed");
                        }

                        _logger.LogInformation("Found {Count} speech recognition engines", recognizers.Count);
                        foreach (var recognizer in recognizers)
                        {
                            _logger.LogDebug("Available recognizer: {Name} - {Culture}",
                                recognizer.Name, recognizer.Culture.Name);
                        }

                        // Initialize with default language (English)
                        InitializeRecognitionEngine("en-US");
                        _isInitialized = true;

                        _logger.LogInformation("Speech recognition service initialized successfully");
                    }
                });

                return true;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize speech recognition service");
                RecognitionError?.Invoke(this, $"Initialization failed: {ex.Message}");
                return false;
            }
        }

        public async Task StartListeningAsync(string language = "en-US")
        {
            try
            {
                if (!_isInitialized)
                {
                    throw new InvalidOperationException("Speech recognition service not initialized");
                }

                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (_isListening)
                        {
                            _logger.LogWarning("Already listening for speech");
                            return;
                        }

                        // Switch language if needed
                        if (_currentLanguage != language)
                        {
                            SwitchLanguage(language);
                        }

                        _logger.LogInformation("Starting speech recognition for language: {Language}", language);

                        // Use multiple recognition mode for continuous listening
                        _recognitionEngine.RecognizeAsync(RecognizeMode.Multiple);
                        _isListening = true;

                        _logger.LogDebug("Speech recognition started successfully in multiple mode");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start speech recognition");
                RecognitionError?.Invoke(this, $"Failed to start listening: {ex.Message}");
                throw;
            }
        }

        public async Task StopListeningAsync()
        {
            try
            {
                await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        if (!_isListening)
                        {
                            _logger.LogWarning("Speech recognition not currently listening");
                            return;
                        }

                        _logger.LogInformation("Stopping speech recognition");

                        _recognitionEngine?.RecognizeAsyncStop();
                        _isListening = false;

                        _logger.LogDebug("Speech recognition stopped successfully");
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error stopping speech recognition");
                RecognitionError?.Invoke(this, $"Failed to stop listening: {ex.Message}");
            }
        }

        public async Task<string> RecognizeSpeechFromAudioAsync(byte[] audioData, string language = "en-US")
        {
            try
            {
                if (audioData == null || audioData.Length == 0)
                {
                    throw new ArgumentException("No audio data provided");
                }

                _logger.LogInformation("Recognizing speech from audio data, length: {Length} bytes, language: {Language}", 
                    audioData.Length, language);

                return await Task.Run(() =>
                {
                    lock (_lockObject)
                    {
                        try
                        {
                            // Create a temporary recognition engine for this specific task
                            SpeechRecognitionEngine tempEngine = null;
                            
                            try
                            {
                                // Create engine for the specified language
                                var culture = new CultureInfo(language);
                                
                                if (_useSharedRecognizer)
                                {
                                    tempEngine = new SpeechRecognitionEngine();
                                }
                                else
                                {
                                    var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
                                        .FirstOrDefault(r => r.Culture.Equals(culture));
                                    
                                    tempEngine = recognizer != null 
                                        ? new SpeechRecognitionEngine(recognizer)
                                        : new SpeechRecognitionEngine();
                                }

                                // Create a memory stream from the audio data
                                using (var audioStream = new System.IO.MemoryStream(audioData))
                                {
                                    // Set input to the audio stream
                                    tempEngine.SetInputToWaveStream(audioStream);

                                    // Load basic dictation grammar
                                    var dictationGrammar = new DictationGrammar();
                                    dictationGrammar.Name = "TempDictation";
                                    dictationGrammar.Enabled = true;
                                    tempEngine.LoadGrammar(dictationGrammar);

                                    // Add airport phrases if enabled
                                    var enableAirportPhrases = _configuration.GetValue<bool>("SpeechRecognition:EnableAirportPhrases", true);
                                    if (enableAirportPhrases)
                                    {
                                        var airportPhrases = GetAirportPhrases(language);
                                        if (airportPhrases.Any())
                                        {
                                            var choicesBuilder = new Choices(airportPhrases.ToArray());
                                            var grammarBuilder = new GrammarBuilder(choicesBuilder);
                                            var airportGrammar = new Grammar(grammarBuilder);
                                            airportGrammar.Name = "TempAirportPhrases";
                                            airportGrammar.Enabled = true;
                                            tempEngine.LoadGrammar(airportGrammar);
                                        }
                                    }

                                    // Use a TaskCompletionSource to handle the async recognition
                                    var tcs = new TaskCompletionSource<string>();
                                    var recognitionTimeout = TimeSpan.FromSeconds(30); // Timeout for recognition
                                    
                                    // Set up event handlers
                                    void OnRecognized(object sender, System.Speech.Recognition.SpeechRecognizedEventArgs e)
                                    {
                                        if (e.Result.Confidence >= _confidenceThreshold)
                                        {
                                            _logger.LogDebug("Audio speech recognized: '{Text}' (Confidence: {Confidence:F3})",
                                                e.Result.Text, e.Result.Confidence);
                                            tcs.TrySetResult(e.Result.Text);
                                        }
                                        else
                                        {
                                            _logger.LogDebug("Audio speech confidence too low: {Confidence:F3} < {Threshold:F3} - Text: '{Text}'",
                                                e.Result.Confidence, _confidenceThreshold, e.Result.Text);
                                            tcs.TrySetResult($"[Low confidence] {e.Result.Text}");
                                        }
                                    }

                                    void OnRejected(object sender, System.Speech.Recognition.SpeechRecognitionRejectedEventArgs e)
                                    {
                                        _logger.LogDebug("Audio speech recognition rejected");
                                        tcs.TrySetResult(null); // No speech recognized
                                    }

                                    void OnCompleted(object sender, System.Speech.Recognition.RecognizeCompletedEventArgs e)
                                    {
                                        if (e.Error != null)
                                        {
                                            _logger.LogError("Audio speech recognition error: {Error}", e.Error.Message);
                                            tcs.TrySetException(e.Error);
                                        }
                                        else if (e.Cancelled)
                                        {
                                            _logger.LogDebug("Audio speech recognition cancelled");
                                            tcs.TrySetResult(null);
                                        }
                                        else if (!tcs.Task.IsCompleted)
                                        {
                                            // Recognition completed but no result was set
                                            _logger.LogDebug("Audio speech recognition completed with no result");
                                            tcs.TrySetResult(null);
                                        }
                                    }

                                    // Subscribe to events
                                    tempEngine.SpeechRecognized += OnRecognized;
                                    tempEngine.SpeechRecognitionRejected += OnRejected;
                                    tempEngine.RecognizeCompleted += OnCompleted;

                                    try
                                    {
                                        // Start recognition (single mode for audio file)
                                        _logger.LogDebug("Starting speech recognition on audio data");
                                        tempEngine.RecognizeAsync(RecognizeMode.Single);

                                        // Wait for result with timeout
                                        if (tcs.Task.Wait(recognitionTimeout))
                                        {
                                            var result = tcs.Task.Result;
                                            if (!string.IsNullOrEmpty(result))
                                            {
                                                _logger.LogInformation("Successfully recognized speech from audio: '{Text}'", result);
                                                return result;
                                            }
                                            else
                                            {
                                                _logger.LogInformation("No speech recognized from audio data");
                                                return null;
                                            }
                                        }
                                        else
                                        {
                                            _logger.LogWarning("Audio speech recognition timed out after {Timeout} seconds", recognitionTimeout.TotalSeconds);
                                            tempEngine.RecognizeAsyncCancel();
                                            return null;
                                        }
                                    }
                                    finally
                                    {
                                        // Unsubscribe from events
                                        tempEngine.SpeechRecognized -= OnRecognized;
                                        tempEngine.SpeechRecognitionRejected -= OnRejected;
                                        tempEngine.RecognizeCompleted -= OnCompleted;
                                    }
                                }
                            }
                            finally
                            {
                                // Clean up temporary engine
                                tempEngine?.Dispose();
                            }
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(ex, "Error during audio speech recognition");
                            throw;
                        }
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to recognize speech from audio data");
                throw;
            }
        }

        public List<RecognizerInfo> GetAvailableRecognizers()
        {
            try
            {
                return SpeechRecognitionEngine.InstalledRecognizers().ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get available recognizers");
                return new List<RecognizerInfo>();
            }
        }

        public bool IsLanguageSupported(string language)
        {
            try
            {
                var culture = new CultureInfo(language);
                return SpeechRecognitionEngine.InstalledRecognizers()
                    .Any(r => r.Culture.Equals(culture));
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Error checking language support for: {Language}", language);
                return false;
            }
        }

        private void InitializeRecognitionEngine(string language)
        {
            try
            {
                // Dispose existing engine if any
                _recognitionEngine?.Dispose();

                // Create recognition engine for specified language
                var culture = new CultureInfo(language);

                if (_useSharedRecognizer)
                {
                    // Use shared recognizer (same as Windows Speech Recognition)
                    _logger.LogInformation("Creating shared recognition engine for language: {Language}", language);
                    _recognitionEngine = new SpeechRecognitionEngine();
                }
                else
                {
                    // Use in-process recognizer for specific language
                    var recognizer = SpeechRecognitionEngine.InstalledRecognizers()
                        .FirstOrDefault(r => r.Culture.Equals(culture));

                    if (recognizer == null)
                    {
                        _logger.LogWarning("No recognizer found for language {Language}, using shared recognizer", language);
                        _recognitionEngine = new SpeechRecognitionEngine();
                    }
                    else
                    {
                        _logger.LogInformation("Using in-process recognizer: {Name} for language {Language}",
                            recognizer.Name, language);
                        _recognitionEngine = new SpeechRecognitionEngine(recognizer);
                    }
                }

                // Configure recognition settings
                _recognitionEngine.SetInputToDefaultAudioDevice();

                // Set recognition parameters for better detection
                try
                {
                    var engineType = _recognitionEngine.GetType();

                    // Set timeouts with more generous values
                    var maxSpeechProperty = engineType.GetProperty("MaxSpeechTimeout");
                    if (maxSpeechProperty != null)
                    {
                        maxSpeechProperty.SetValue(_recognitionEngine, TimeSpan.FromMilliseconds(_maxSpeechTimeoutMs));
                        _logger.LogDebug("Set MaxSpeechTimeout to {Timeout}ms", _maxSpeechTimeoutMs);
                    }

                    var initialSilenceProperty = engineType.GetProperty("InitialSilenceTimeout");
                    if (initialSilenceProperty != null)
                    {
                        initialSilenceProperty.SetValue(_recognitionEngine, TimeSpan.FromMilliseconds(_initialSilenceTimeoutMs));
                        _logger.LogDebug("Set InitialSilenceTimeout to {Timeout}ms", _initialSilenceTimeoutMs);
                    }

                    // Set shorter end silence for better responsiveness
                    var endSilenceProperty = engineType.GetProperty("EndSilenceTimeout");
                    if (endSilenceProperty != null)
                    {
                        endSilenceProperty.SetValue(_recognitionEngine, TimeSpan.FromMilliseconds(300));
                        _logger.LogDebug("Set EndSilenceTimeout to 300ms");
                    }

                    // Set babble timeout
                    var babbleTimeoutProperty = engineType.GetProperty("BabbleTimeout");
                    if (babbleTimeoutProperty != null)
                    {
                        babbleTimeoutProperty.SetValue(_recognitionEngine, TimeSpan.FromMilliseconds(1500));
                        _logger.LogDebug("Set BabbleTimeout to 1500ms");
                    }

                    // Set recognition confidence
                    var endSilenceTimeoutAmbigiousProperty = engineType.GetProperty("EndSilenceTimeoutAmbiguous");
                    if (endSilenceTimeoutAmbigiousProperty != null)
                    {
                        endSilenceTimeoutAmbigiousProperty.SetValue(_recognitionEngine, TimeSpan.FromMilliseconds(1000));
                        _logger.LogDebug("Set EndSilenceTimeoutAmbiguous to 1000ms");
                    }

                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not set all speech recognition parameters - using defaults");
                }

                // Create grammar for better recognition
                CreateImprovedGrammar(language);

                // Set up event handlers
                _recognitionEngine.SpeechRecognized += OnSpeechRecognized;
                _recognitionEngine.SpeechRecognitionRejected += OnSpeechRecognitionRejected;
                _recognitionEngine.AudioLevelUpdated += OnAudioLevelUpdated;
                _recognitionEngine.SpeechDetected += OnSpeechDetected;
                _recognitionEngine.RecognizeCompleted += OnRecognizeCompleted;
                _recognitionEngine.SpeechHypothesized += OnSpeechHypothesized;

                _currentLanguage = language;

                _logger.LogInformation("Recognition engine configured for language: {Language} with improved settings", language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize recognition engine");
                throw;
            }
        }

        private void CreateImprovedGrammar(string language)
        {
            try
            {
                // Load dictation grammar with high weight (most important)
                var dictationGrammar = new DictationGrammar();
                dictationGrammar.Name = "Dictation";
                dictationGrammar.Enabled = true;
                dictationGrammar.Weight = 1.0f;
                _recognitionEngine.LoadGrammar(dictationGrammar);

                // Create a simple word grammar with common words
                var commonWords = new Choices(new string[]
                {
                    // Basic words
                    "hello", "hi", "hey", "yes", "no", "ok", "okay", "thanks", "thank you",
                    "help", "please", "excuse me", "sorry", "good", "bad", "fine",
                    
                    // Numbers
                    "one", "two", "three", "four", "five", "six", "seven", "eight", "nine", "ten",
                    "first", "second", "third", "last",
                    
                    // Airport specific
                    "gate", "flight", "plane", "airport", "ticket", "baggage", "luggage",
                    "check in", "security", "passport", "boarding", "departure", "arrival",
                    "terminal", "bathroom", "restroom", "food", "restaurant", "shop", "store",
                    "taxi", "bus", "train", "metro", "subway", "city", "hotel",
                    
                    // Questions
                    "where", "what", "when", "how", "why", "which", "who",
                    "where is", "what time", "how much", "how many", "how do I",
                    
                    // Directions
                    "left", "right", "straight", "up", "down", "here", "there", "near", "far"
                });

                var commonWordsBuilder = new GrammarBuilder(commonWords);
                var commonWordsGrammar = new Grammar(commonWordsBuilder);
                commonWordsGrammar.Name = "CommonWords";
                commonWordsGrammar.Enabled = true;
                commonWordsGrammar.Weight = 0.9f;
                _recognitionEngine.LoadGrammar(commonWordsGrammar);

                // Add airport phrases with high priority
                var airportPhrases = GetAirportPhrases(language);
                if (airportPhrases.Any())
                {
                    var choicesBuilder = new Choices(airportPhrases.ToArray());
                    var grammarBuilder = new GrammarBuilder(choicesBuilder);
                    var airportGrammar = new Grammar(grammarBuilder);
                    airportGrammar.Name = "AirportPhrases";
                    airportGrammar.Enabled = true;
                    airportGrammar.Weight = 0.8f;
                    _recognitionEngine.LoadGrammar(airportGrammar);

                    _logger.LogDebug("Loaded {Count} airport phrases for language {Language}",
                        airportPhrases.Count, language);
                }

                _logger.LogInformation("Improved grammar loaded - Dictation + Common words + Airport phrases");
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to create improved grammar, using basic dictation only");

                // Fallback - just use dictation
                try
                {
                    var dictationGrammar = new DictationGrammar();
                    dictationGrammar.Name = "Dictation";
                    dictationGrammar.Enabled = true;
                    _recognitionEngine.LoadGrammar(dictationGrammar);
                }
                catch (Exception ex2)
                {
                    _logger.LogError(ex2, "Failed to load even basic dictation grammar");
                }
            }
        }

        private List<string> GetAirportPhrases(string language)
        {
            return language switch
            {
                "en-US" => new List<string>
                {
                    "Where is gate",
                    "How much does this cost",
                    "Where is the bathroom",
                    "What time does my flight depart",
                    "Is there free WiFi",
                    "Where can I buy food",
                    "How do I get to the city center",
                    "I need help with my luggage",
                    "Where is check-in",
                    "Where is security",
                    "Where is baggage claim",
                    "Thank you",
                    "Excuse me",
                    "Help me please"
                },
                "ja-JP" => new List<string>
                {
                    "ゲートはどこですか",
                    "これはいくらですか",
                    "トイレはどこですか",
                    "飛行機は何時に出発しますか",
                    "無料のWiFiはありますか",
                    "食べ物はどこで買えますか",
                    "市内中心部にはどうやって行けばいいですか",
                    "荷物を手伝ってください",
                    "チェックインはどこですか",
                    "セキュリティはどこですか",
                    "手荷物受取所はどこですか",
                    "ありがとうございます",
                    "すみません",
                    "助けてください"
                },
                "it-IT" => new List<string>
                {
                    "Dov'è il gate",
                    "Quanto costa questo",
                    "Dov'è il bagno",
                    "A che ora parte il mio volo",
                    "C'è il WiFi gratuito",
                    "Dove posso comprare del cibo",
                    "Come arrivo al centro città",
                    "Ho bisogno di aiuto con i bagagli",
                    "Dov'è il check-in",
                    "Dov'è la sicurezza",
                    "Dov'è il ritiro bagagli",
                    "Grazie",
                    "Mi scusi",
                    "Aiutami per favore"
                },
                "ko-KR" => new List<string>
                {
                    "게이트가 어디에 있나요",
                    "이것은 얼마입니까",
                    "화장실이 어디에 있나요",
                    "제 비행기는 몇 시에 출발하나요",
                    "무료 와이파이가 있나요",
                    "음식을 어디서 살 수 있나요",
                    "시내 중심가로 어떻게 가나요",
                    "짐을 도와주세요",
                    "체크인이 어디에 있나요",
                    "보안검색대가 어디에 있나요",
                    "수하물 찾는 곳이 어디에 있나요",
                    "감사합니다",
                    "실례합니다",
                    "도와주세요"
                },
                _ => new List<string>()
            };
        }

        private void SwitchLanguage(string language)
        {
            try
            {
                _logger.LogInformation("Switching speech recognition language from {From} to {To}",
                    _currentLanguage, language);

                if (_isListening)
                {
                    _recognitionEngine.RecognizeAsyncStop();
                    _isListening = false;
                }

                InitializeRecognitionEngine(language);

                _logger.LogInformation("Language switched successfully to {Language}", language);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to switch language to {Language}", language);
                RecognitionError?.Invoke(this, $"Failed to switch language: {ex.Message}");
            }
        }

        #region Event Handlers

        private void OnSpeechHypothesized(object sender, System.Speech.Recognition.SpeechHypothesizedEventArgs e)
        {
            if (_enableDebugging)
            {
                _logger.LogDebug("Speech hypothesis: '{Text}' (Confidence: {Confidence:F3})",
                    e.Result.Text, e.Result.Confidence);
            }
        }

        private void OnSpeechRecognized(object sender, System.Speech.Recognition.SpeechRecognizedEventArgs e)
        {
            try
            {
                if (_enableDebugging)
                {
                    _logger.LogInformation("Speech recognized: '{Text}' (Confidence: {Confidence:F3}, Threshold: {Threshold:F3})",
                        e.Result.Text, e.Result.Confidence, _confidenceThreshold);

                    // Log grammar used
                    if (e.Result.Grammar != null)
                    {
                        _logger.LogDebug("Grammar used: {Grammar}", e.Result.Grammar.Name);
                    }

                    // Log alternates for debugging
                    if (e.Result.Alternates.Count > 1)
                    {
                        var alternates = string.Join(", ", e.Result.Alternates.Take(3).Select(a => $"'{a.Text}' ({a.Confidence:F2})"));
                        _logger.LogDebug("Alternates: {Alternates}", alternates);
                    }
                }

                // Accept ANY recognition with confidence above threshold
                if (e.Result.Confidence >= _confidenceThreshold)
                {
                    var args = new SpeechRecognizedEventArgs
                    {
                        Text = e.Result.Text,
                        Confidence = e.Result.Confidence,
                        Language = _currentLanguage,
                        RecognitionTime = e.Result.Audio.Duration
                    };

                    SpeechRecognized?.Invoke(this, args);

                    if (_enableDebugging)
                    {
                        _logger.LogInformation("Speech ACCEPTED and forwarded to UI");
                    }
                }
                else
                {
                    _logger.LogDebug("Speech confidence too low: {Confidence:F3} < {Threshold:F3} - Text: '{Text}'",
                        e.Result.Confidence, _confidenceThreshold, e.Result.Text);

                    var rejectedArgs = new SpeechRecognitionRejectedEventArgs
                    {
                        Reason = $"Low confidence: {e.Result.Confidence:F2} (need {_confidenceThreshold:F2}) - but heard: {e.Result.Text}"
                    };

                    SpeechRejected?.Invoke(this, rejectedArgs);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling speech recognition result");
            }
        }

        private void OnSpeechRecognitionRejected(object sender, System.Speech.Recognition.SpeechRecognitionRejectedEventArgs e)
        {
            try
            {
                _logger.LogDebug("Speech recognition rejected");

                var args = new SpeechRecognitionRejectedEventArgs
                {
                    Reason = "Speech not recognized"
                };

                SpeechRejected?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling speech recognition rejection");
            }
        }

        private void OnAudioLevelUpdated(object sender, System.Speech.Recognition.AudioLevelUpdatedEventArgs e)
        {
            try
            {
                var args = new AudioLevelUpdatedEventArgs
                {
                    AudioLevel = e.AudioLevel
                };

                AudioLevelChanged?.Invoke(this, args);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error handling audio level update");
            }
        }

        private void OnSpeechDetected(object sender, System.Speech.Recognition.SpeechDetectedEventArgs e)
        {
            _logger.LogDebug("Speech detected at audio position: {Position}", e.AudioPosition);
        }

        private void OnRecognizeCompleted(object sender, System.Speech.Recognition.RecognizeCompletedEventArgs e)
        {
            try
            {
                _isListening = false; // Mark as no longer listening

                if (e.Error != null)
                {
                    _logger.LogError("Speech recognition completed with error: {Error}", e.Error.Message);
                    RecognitionError?.Invoke(this, e.Error.Message);
                }
                else if (e.Cancelled)
                {
                    _logger.LogDebug("Speech recognition was cancelled");
                }
                else
                {
                    _logger.LogDebug("Speech recognition completed successfully - recognition stopped automatically");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in OnRecognizeCompleted");
            }
        }

        #endregion

        public void Dispose()
        {
            try
            {
                lock (_lockObject)
                {
                    if (_isListening)
                    {
                        _recognitionEngine?.RecognizeAsyncStop();
                        _isListening = false;
                    }

                    _recognitionEngine?.Dispose();
                    _recognitionEngine = null;
                    _isInitialized = false;
                }

                _logger.LogInformation("Speech recognition service disposed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error disposing speech recognition service");
            }
        }
    }
}