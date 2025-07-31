using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportKiosk.Core.Models;
public class TranslationResponse
{
    public string TranslatedText { get; set; }
    public string DetectedSourceLanguage { get; set; }
    public string SourceLanguage { get; set; }
    public string TargetLanguage { get; set; }
    public float Confidence { get; set; }
    public string Provider { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public bool Success { get; set; }
    public string ErrorMessage { get; set; }
    public DateTime ResponseTime { get; set; } = DateTime.UtcNow;
    public string SessionId { get; set; }
}