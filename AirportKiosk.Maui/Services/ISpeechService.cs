namespace AirportKiosk.Maui.Services;

public interface ISpeechService
{
    Task<bool> RequestPermissionsAsync();
    Task<SpeechRecognitionResult> ListenAsync(string languageCode, CancellationToken cancellationToken = default);
    Task<bool> IsListeningAsync();
    Task StopListeningAsync();
    bool IsSupported { get; }
}

public class SpeechRecognitionResult
{
    public string Text { get; set; } = string.Empty;
    public bool IsSuccessful { get; set; }
    public string? ErrorMessage { get; set; }
    public double Confidence { get; set; }
}