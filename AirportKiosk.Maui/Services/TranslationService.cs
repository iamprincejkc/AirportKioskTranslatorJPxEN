using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirportKiosk.Maui.Services;

public class TranslationService : ITranslationService
{
    private readonly HttpClient _httpClient;
    private const string BaseUrl = "https://api.mymemory.translated.net";

    private readonly List<LanguageInfo> _supportedLanguages = new()
    {
        new LanguageInfo { Code = "en", Name = "English", NativeName = "English" },
        new LanguageInfo { Code = "es", Name = "Spanish", NativeName = "Español" },
        new LanguageInfo { Code = "fr", Name = "French", NativeName = "Français" },
        new LanguageInfo { Code = "de", Name = "German", NativeName = "Deutsch" },
        new LanguageInfo { Code = "it", Name = "Italian", NativeName = "Italiano" },
        new LanguageInfo { Code = "pt", Name = "Portuguese", NativeName = "Português" },
        new LanguageInfo { Code = "ru", Name = "Russian", NativeName = "Русский" },
        new LanguageInfo { Code = "ja", Name = "Japanese", NativeName = "日本語" },
        new LanguageInfo { Code = "ko", Name = "Korean", NativeName = "한국어" },
        new LanguageInfo { Code = "zh", Name = "Chinese", NativeName = "中文" },
        new LanguageInfo { Code = "ar", Name = "Arabic", NativeName = "العربية" },
        new LanguageInfo { Code = "hi", Name = "Hindi", NativeName = "हिन्दी" },
        new LanguageInfo { Code = "th", Name = "Thai", NativeName = "ไทย" },
        new LanguageInfo { Code = "vi", Name = "Vietnamese", NativeName = "Tiếng Việt" },
        new LanguageInfo { Code = "nl", Name = "Dutch", NativeName = "Nederlands" },
        new LanguageInfo { Code = "sv", Name = "Swedish", NativeName = "Svenska" },
        new LanguageInfo { Code = "da", Name = "Danish", NativeName = "Dansk" },
        new LanguageInfo { Code = "no", Name = "Norwegian", NativeName = "Norsk" },
        new LanguageInfo { Code = "fi", Name = "Finnish", NativeName = "Suomi" },
        new LanguageInfo { Code = "pl", Name = "Polish", NativeName = "Polski" }
    };

    public TranslationService(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            var encodedText = Uri.EscapeDataString(text);
            var langPair = $"{fromLanguage}|{toLanguage}";
            var url = $"{BaseUrl}/get?q={encodedText}&langpair={langPair}";

            var response = await _httpClient.GetStringAsync(url);
            var translationResponse = JsonSerializer.Deserialize<TranslationResponse>(response);

            // 1. Try responseData.translatedText
            var translatedText = translationResponse?.ResponseData?.TranslatedText;
            if (!string.IsNullOrWhiteSpace(translatedText))
                return translatedText;

            // 2. Fallback: Get best translation from matches
            var matches = translationResponse?.Matches;
            if (matches != null && matches.Count > 0)
            {
                // Find match with non-empty translation and highest quality
                var best = matches
                    .Where(m => !string.IsNullOrWhiteSpace(m.Translation))
                    .OrderByDescending(m => GetQualityAsDouble(m.Quality))
                    .FirstOrDefault();

                double GetQualityAsDouble(object? value)
                {
                    if (value is double d) return d;
                    if (value is int i) return i;
                    if (value is string s && double.TryParse(s, out var v)) return v;
                    return 0.0;
                }

                if (best != null)
                    return best.Translation!;
            }

            return text; // As fallback
        }
        catch (Exception ex)
        {
            // Log error in production
            System.Diagnostics.Debug.WriteLine($"Translation error: {ex.Message}");
            return text;
        }
    }

    public Task<List<LanguageInfo>> GetSupportedLanguagesAsync()
    {
        return Task.FromResult(_supportedLanguages);
    }
}
public class TranslationResponse
{
    [JsonPropertyName("responseData")]
    public ResponseData? ResponseData { get; set; }

    [JsonPropertyName("quotaFinished")]
    public bool QuotaFinished { get; set; }

    [JsonPropertyName("responseDetails")]
    public string? ResponseDetails { get; set; }

    [JsonPropertyName("responseStatus")]
    public int ResponseStatus { get; set; }

    [JsonPropertyName("matches")]
    public List<TranslationMatch>? Matches { get; set; }
}

public class ResponseData
{
    [JsonPropertyName("translatedText")]
    public string? TranslatedText { get; set; }

    [JsonPropertyName("match")]
    public double Match { get; set; }
}

public class TranslationMatch
{
    [JsonPropertyName("id")]
    public object? Id { get; set; } 

    [JsonPropertyName("segment")]
    public string? Segment { get; set; }

    [JsonPropertyName("translation")]
    public string? Translation { get; set; }

    [JsonPropertyName("source")]
    public string? Source { get; set; }

    [JsonPropertyName("target")]
    public string? Target { get; set; }

    [JsonPropertyName("quality")]
    public object? Quality { get; set; } 
    [JsonPropertyName("reference")]
    public string? Reference { get; set; }

    [JsonPropertyName("usage-count")]
    public int UsageCount { get; set; }

    [JsonPropertyName("subject")]
    public object? Subject { get; set; } 

    [JsonPropertyName("created-by")]
    public string? CreatedBy { get; set; }

    [JsonPropertyName("last-updated-by")]
    public string? LastUpdatedBy { get; set; }

    [JsonPropertyName("create-date")]
    public string? CreateDate { get; set; }

    [JsonPropertyName("last-update-date")]
    public string? LastUpdateDate { get; set; }

    [JsonPropertyName("match")]
    public double Match { get; set; }

    [JsonPropertyName("penalty")]
    public double? Penalty { get; set; } 

    [JsonPropertyName("model")]
    public string? Model { get; set; }
}