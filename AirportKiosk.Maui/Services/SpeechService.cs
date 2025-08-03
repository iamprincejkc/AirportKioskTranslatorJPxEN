using Microsoft.Maui.Authentication;

#if WINDOWS
using WinRT;
using Microsoft.UI.Xaml;
using WinSpeech = Windows.Media.SpeechRecognition;
using WinMedia = Windows.Media.Capture;
using WinGlob = Windows.Globalization;
#endif

namespace AirportKiosk.Maui.Services;

public class SpeechService : ISpeechService
{
    private bool _isListening;
    private CancellationTokenSource? _cancellationTokenSource;

#if WINDOWS
    private WinSpeech.SpeechRecognizer? _windowsSpeechRecognizer;
#endif

    public bool IsSupported =>
#if ANDROID || IOS || WINDOWS
        true;
#else
        false;
#endif

    public async Task<bool> RequestPermissionsAsync()
    {
        try
        {
#if WINDOWS
            // Check speech privacy policy first
            var speechPrivacySettings = WinSpeech.SpeechRecognizer.SystemSpeechLanguage;
            if (speechPrivacySettings == null)
            {
                return false;
            }

            // Windows requires special handling for microphone permission
            try
            {
                var mediaCapture = new WinMedia.MediaCapture();
                var settings = new WinMedia.MediaCaptureInitializationSettings();
                settings.StreamingCaptureMode = WinMedia.StreamingCaptureMode.Audio;

                await mediaCapture.InitializeAsync(settings);
                mediaCapture.Dispose();
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (Exception)
            {
                return false;
            }
#else
            var status = await Permissions.RequestAsync<Permissions.Microphone>();
            return status == PermissionStatus.Granted;
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Permission request failed: {ex.Message}");
            return false;
        }
    }

    public async Task<SpeechRecognitionResult> ListenAsync(string languageCode, CancellationToken cancellationToken = default)
    {
        if (!await RequestPermissionsAsync())
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = "Microphone permission denied"
            };
        }

        try
        {
            _isListening = true;
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

#if ANDROID
            return await ListenAndroidAsync(languageCode, _cancellationTokenSource.Token);
#elif IOS
            return await ListenIOSAsync(languageCode, _cancellationTokenSource.Token);
#elif WINDOWS
            return await ListenWindowsAsync(languageCode, _cancellationTokenSource.Token);
#else
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = "Speech recognition not supported on this platform"
            };
#endif
        }
        catch (OperationCanceledException)
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = "Speech recognition was cancelled"
            };
        }
        catch (Exception ex)
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
        finally
        {
            _isListening = false;
        }
    }

    public Task<bool> IsListeningAsync()
    {
        return Task.FromResult(_isListening);
    }

    public async Task StopListeningAsync()
    {
        try
        {
            _cancellationTokenSource?.Cancel();
            _isListening = false;

#if WINDOWS
            if (_windowsSpeechRecognizer != null)
            {
                await _windowsSpeechRecognizer.StopRecognitionAsync();
            }
#endif
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Error stopping speech recognition: {ex.Message}");
        }
    }

#if ANDROID
    private async Task<SpeechRecognitionResult> ListenAndroidAsync(string languageCode, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<SpeechRecognitionResult>();
        
        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var activity = Platform.CurrentActivity ?? Android.App.Application.Context;
                var intent = new Android.Content.Intent(Android.Speech.RecognizerIntent.ActionRecognizeSpeech);
                intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguageModel, Android.Speech.RecognizerIntent.LanguageModelFreeForm);
                intent.PutExtra(Android.Speech.RecognizerIntent.ExtraLanguage, GetAndroidLanguageCode(languageCode));
                intent.PutExtra(Android.Speech.RecognizerIntent.ExtraPrompt, "Speak now...");

                if (activity is AndroidX.Activity.ComponentActivity componentActivity)
                {
                    var activityResultLauncher = componentActivity.RegisterForActivityResult(
                        new AndroidX.Activity.Result.Contract.ActivityResultContracts.StartActivityForResult(),
                        new ActivityResultCallback(tcs));
                    
                    activityResultLauncher.Launch(intent);
                }
            });

            using (cancellationToken.Register(() => tcs.TrySetCanceled()))
            {
                return await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private class ActivityResultCallback : Java.Lang.Object, AndroidX.Activity.Result.IActivityResultCallback
    {
        private readonly TaskCompletionSource<SpeechRecognitionResult> _tcs;

        public ActivityResultCallback(TaskCompletionSource<SpeechRecognitionResult> tcs)
        {
            _tcs = tcs;
        }

        public void OnActivityResult(Java.Lang.Object result)
        {
            if (result is AndroidX.Activity.Result.ActivityResult activityResult)
            {
                if (activityResult.ResultCode == Android.App.Result.Ok && activityResult.Data != null)
                {
                    var matches = activityResult.Data.GetStringArrayListExtra(Android.Speech.RecognizerIntent.ExtraResults);
                    if (matches != null && matches.Count > 0)
                    {
                        _tcs.SetResult(new SpeechRecognitionResult
                        {
                            IsSuccessful = true,
                            Text = matches[0] ?? string.Empty,
                            Confidence = 1.0
                        });
                        return;
                    }
                }
            }

            _tcs.SetResult(new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = "No speech recognized"
            });
        }
    }

    private string GetAndroidLanguageCode(string languageCode)
    {
        return languageCode switch
        {
            "en" => "en-US",
            "es" => "es-ES",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "ru" => "ru-RU",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "zh-CN",
            "ar" => "ar-SA",
            "hi" => "hi-IN",
            "th" => "th-TH",
            "vi" => "vi-VN",
            "nl" => "nl-NL",
            "sv" => "sv-SE",
            "da" => "da-DK",
            "no" => "nb-NO",
            "fi" => "fi-FI",
            "pl" => "pl-PL",
            _ => "en-US"
        };
    }
#endif

#if IOS
    private async Task<SpeechRecognitionResult> ListenIOSAsync(string languageCode, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<SpeechRecognitionResult>();

        try
        {
            await MainThread.InvokeOnMainThreadAsync(async () =>
            {
                var speechRecognizer = new Speech.SFSpeechRecognizer(new Foundation.NSLocale(GetIOSLanguageCode(languageCode)));
                
                if (speechRecognizer == null || !speechRecognizer.Available)
                {
                    tcs.SetResult(new SpeechRecognitionResult
                    {
                        IsSuccessful = false,
                        ErrorMessage = "Speech recognition not available for this language"
                    });
                    return;
                }

                var audioEngine = new AVFoundation.AVAudioEngine();
                var request = new Speech.SFSpeechAudioBufferRecognitionRequest();
                var inputNode = audioEngine.InputNode;

                var recognitionTask = speechRecognizer.GetRecognitionTask(request, (result, error) =>
                {
                    if (error != null)
                    {
                        tcs.TrySetResult(new SpeechRecognitionResult
                        {
                            IsSuccessful = false,
                            ErrorMessage = error.LocalizedDescription
                        });
                        return;
                    }

                    if (result != null && result.Final)
                    {
                        tcs.TrySetResult(new SpeechRecognitionResult
                        {
                            IsSuccessful = true,
                            Text = result.BestTranscription.FormattedString,
                            Confidence = result.BestTranscription.Segments.LastOrDefault()?.Confidence ?? 0.0
                        });
                    }
                });

                var recordingFormat = inputNode.GetBusOutputFormat(0);
                inputNode.InstallTapOnBus(0, 1024, recordingFormat, (buffer, time) =>
                {
                    request.Append(buffer);
                });

                audioEngine.Prepare();
                await audioEngine.StartAndReturnError();

                // Stop after 5 seconds of listening
                _ = Task.Delay(5000, cancellationToken).ContinueWith(t =>
                {
                    if (!t.IsCanceled)
                    {
                        MainThread.BeginInvokeOnMainThread(() =>
                        {
                            audioEngine.Stop();
                            inputNode.RemoveTapOnBus(0);
                            request.EndAudio();
                        });
                    }
                });
            });

            using (cancellationToken.Register(() =>
            {
                tcs.TrySetResult(new SpeechRecognitionResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Speech recognition was cancelled"
                });
            }))
            {
                return await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GetIOSLanguageCode(string languageCode)
    {
        return languageCode switch
        {
            "en" => "en-US",
            "es" => "es-ES",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "ru" => "ru-RU",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "zh-CN",
            "ar" => "ar-SA",
            "hi" => "hi-IN",
            "th" => "th-TH",
            "vi" => "vi-VN",
            "nl" => "nl-NL",
            "sv" => "sv-SE",
            "da" => "da-DK",
            "no" => "nb-NO",
            "fi" => "fi-FI",
            "pl" => "pl-PL",
            _ => "en-US"
        };
    }
#endif

#if WINDOWS
    private async Task<SpeechRecognitionResult> ListenWindowsAsync(string languageCode, CancellationToken cancellationToken)
    {
        try
        {
            _windowsSpeechRecognizer?.Dispose();
            _windowsSpeechRecognizer = new WinSpeech.SpeechRecognizer();

            // Set language
            var language = GetWindowsLanguageCode(languageCode);
            try
            {
                var windowsLanguage = new WinGlob.Language(language);
                if (WinSpeech.SpeechRecognizer.SupportedTopicLanguages.Contains(windowsLanguage))
                {
                    _windowsSpeechRecognizer.Dispose();
                    _windowsSpeechRecognizer = new WinSpeech.SpeechRecognizer(windowsLanguage);
                }
            }
            catch
            {
                // Use default language if specified language is not supported
            }

            // Compile the built-in dictation grammar
            var compilationResult = await _windowsSpeechRecognizer.CompileConstraintsAsync();
            if (compilationResult.Status != WinSpeech.SpeechRecognitionResultStatus.Success)
            {
                return new SpeechRecognitionResult
                {
                    IsSuccessful = false,
                    ErrorMessage = "Failed to compile speech recognition constraints"
                };
            }

            // Set timeouts
            _windowsSpeechRecognizer.Timeouts.InitialSilenceTimeout = TimeSpan.FromSeconds(5);
            _windowsSpeechRecognizer.Timeouts.EndSilenceTimeout = TimeSpan.FromSeconds(2);

            // Register cancellation
            using (cancellationToken.Register(async () =>
            {
                try
                {
                    await _windowsSpeechRecognizer.StopRecognitionAsync();
                }
                catch { }
            }))
            {
                try
                {
                    var result = await _windowsSpeechRecognizer.RecognizeAsync();

                    if (result.Status == WinSpeech.SpeechRecognitionResultStatus.Success)
                    {
                        return new SpeechRecognitionResult
                        {
                            IsSuccessful = true,
                            Text = result.Text,
                            Confidence = result.Confidence == WinSpeech.SpeechRecognitionConfidence.High ? 0.9 :
                                        result.Confidence == WinSpeech.SpeechRecognitionConfidence.Medium ? 0.6 : 0.3
                        };
                    }
                    else
                    {
                        return new SpeechRecognitionResult
                        {
                            IsSuccessful = false,
                            ErrorMessage = GetWindowsErrorMessage(result.Status)
                        };
                    }
                }
                catch (Exception ex)
                {
                    return new SpeechRecognitionResult
                    {
                        IsSuccessful = false,
                        ErrorMessage = ex.Message
                    };
                }
            }
        }
        catch (Exception ex)
        {
            return new SpeechRecognitionResult
            {
                IsSuccessful = false,
                ErrorMessage = ex.Message
            };
        }
    }

    private string GetWindowsErrorMessage(WinSpeech.SpeechRecognitionResultStatus status)
    {
        return status switch
        {
            WinSpeech.SpeechRecognitionResultStatus.AudioQualityFailure => "Audio quality too poor for recognition",
            WinSpeech.SpeechRecognitionResultStatus.UserCanceled => "Speech recognition was cancelled",
            WinSpeech.SpeechRecognitionResultStatus.NetworkFailure => "Network connection required",
            WinSpeech.SpeechRecognitionResultStatus.TopicLanguageNotSupported => "Language not supported",
            WinSpeech.SpeechRecognitionResultStatus.GrammarLanguageMismatch => "Grammar language mismatch",
            WinSpeech.SpeechRecognitionResultStatus.GrammarCompilationFailure => "Grammar compilation failed",
            WinSpeech.SpeechRecognitionResultStatus.MicrophoneUnavailable => "Microphone unavailable",
            WinSpeech.SpeechRecognitionResultStatus.TimeoutExceeded => "No speech detected - try speaking louder",
            WinSpeech.SpeechRecognitionResultStatus.Unknown => "Please enable speech recognition in Windows Settings > Privacy & Security > Speech",
            _ => "Please enable speech recognition in Windows Settings > Privacy & Security > Speech"
        };
    }

    private string GetWindowsLanguageCode(string languageCode)
    {
        return languageCode switch
        {
            "en" => "en-US",
            "es" => "es-ES",
            "fr" => "fr-FR",
            "de" => "de-DE",
            "it" => "it-IT",
            "pt" => "pt-BR",
            "ru" => "ru-RU",
            "ja" => "ja-JP",
            "ko" => "ko-KR",
            "zh" => "zh-CN",
            "ar" => "ar-SA",
            "hi" => "hi-IN",
            "nl" => "nl-NL",
            "sv" => "sv-SE",
            "da" => "da-DK",
            "no" => "nb-NO",
            "fi" => "fi-FI",
            "pl" => "pl-PL",
            _ => "en-US"
        };
    }
#endif

    public void Dispose()
    {
#if WINDOWS
        _windowsSpeechRecognizer?.Dispose();
#endif
        _cancellationTokenSource?.Dispose();
    }
}