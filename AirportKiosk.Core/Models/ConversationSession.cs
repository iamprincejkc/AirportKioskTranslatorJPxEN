namespace AirportKiosk.Core.Models;
public class ConversationSession
{
    public string SessionId { get; set; } = Guid.NewGuid().ToString();
    public DateTime StartTime { get; set; } = DateTime.UtcNow;
    public DateTime LastActivity { get; set; } = DateTime.UtcNow;
    public List<ConversationEntry> Entries { get; set; } = new List<ConversationEntry>();
    public int TotalTranslations { get; set; }
    public string KioskId { get; set; }
}
