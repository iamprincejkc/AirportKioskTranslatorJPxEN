namespace AirportKiosk.Maui.Services;

public interface ITranslationService
{
    Task<string> TranslateTextAsync(string text, string fromLanguage, string toLanguage);
    Task<List<LanguageInfo>> GetSupportedLanguagesAsync();
}

public class LanguageInfo
{
    public string Code { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string NativeName { get; set; } = string.Empty;
}