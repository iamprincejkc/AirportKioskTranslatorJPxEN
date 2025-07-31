using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AirportKiosk.Core.Models;
public class HealthCheckResponse
{
    public bool IsHealthy { get; set; }
    public string Status { get; set; }
    public Dictionary<string, object> Details { get; set; } = new Dictionary<string, object>();
    public DateTime CheckTime { get; set; } = DateTime.UtcNow;
}
