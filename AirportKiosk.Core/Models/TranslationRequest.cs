using System.ComponentModel.DataAnnotations;

namespace AirportKiosk.Core.Models;
public class TranslationRequest
{
    [Required]
    [MaxLength(5000)]
    public string Text { get; set; }

    [Required]
    [RegularExpression("^(en|ja|ko|it|auto)$", ErrorMessage = "Source language must be 'en', 'ja', 'ko', 'it', or 'auto'")]
    public string SourceLanguage { get; set; }

    [Required]
    [RegularExpression("^(en|ja|ko|it)$", ErrorMessage = "Target language must be 'en', 'ja', 'ko', or 'it'")]
    public string TargetLanguage { get; set; }

    public string SessionId { get; set; }
    public DateTime RequestTime { get; set; } = DateTime.UtcNow;
}
