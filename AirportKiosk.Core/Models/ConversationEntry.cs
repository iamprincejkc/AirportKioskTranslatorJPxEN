using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportKiosk.Core.Models;
public class ConversationEntry
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string OriginalText { get; set; }
    public string TranslatedText { get; set; }
    public string SourceLanguage { get; set; }
    public string TargetLanguage { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string SessionId { get; set; }
    public float Confidence { get; set; }
}