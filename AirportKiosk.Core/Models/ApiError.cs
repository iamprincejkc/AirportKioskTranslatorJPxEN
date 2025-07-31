namespace AirportKiosk.Core.Models;
public class ApiError
{
    public string Message { get; set; }
    public string Code { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public string RequestId { get; set; }
    public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
}