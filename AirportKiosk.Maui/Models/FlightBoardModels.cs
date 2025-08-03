using System.Text.Json.Serialization;

namespace AirportKiosk.Maui.Models;

public class FlightBoardRequest
{
    public string AirportCode { get; set; } = "BOS"; // Boston Logan
    public DateTime Date { get; set; } = DateTime.Today;
    public FlightDirection Direction { get; set; } = FlightDirection.Departure;
    public int MaxResults { get; set; } = 50;
}

public enum FlightDirection
{
    Departure,
    Arrival
}

public class FlightBoardResponse
{
    [JsonPropertyName("data")]
    public List<FlightSchedule> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public FlightBoardMeta Meta { get; set; } = new();
}

public class FlightSchedule
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("scheduledDepartureDate")]
    public string ScheduledDepartureDate { get; set; } = string.Empty;

    [JsonPropertyName("flightDesignator")]
    public FlightDesignator FlightDesignator { get; set; } = new();

    [JsonPropertyName("flightPoints")]
    public List<FlightPoint> FlightPoints { get; set; } = new();

    [JsonPropertyName("segments")]
    public List<FlightSegment> Segments { get; set; } = new();

    // Computed properties for easy display
    public string FlightNumber => $"{FlightDesignator.CarrierCode}{FlightDesignator.FlightNumber}";

    public FlightPoint? DeparturePoint => FlightPoints?.FirstOrDefault(fp => fp.IataCode != "BOS");
    public FlightPoint? ArrivalPoint => FlightPoints?.LastOrDefault(fp => fp.IataCode != "BOS");

    public string Destination { get; set; } = string.Empty;
    public string Origin { get; set; } = string.Empty;

    public DateTime? DepartureTime { get; set; }
    public DateTime? ArrivalTime { get; set; }

    public string Status { get; set; } = "Scheduled";
    public string Gate { get; set; } = "TBD";
    public string Terminal { get; set; } = "";
    public string Airline { get; set; } = "";
}

public class FlightDesignator
{
    [JsonPropertyName("carrierCode")]
    public string CarrierCode { get; set; } = string.Empty;

    [JsonPropertyName("flightNumber")]
    public int FlightNumber { get; set; }
}

public class FlightSegment
{
    [JsonPropertyName("boardPointIataCode")]
    public string BoardPointIataCode { get; set; } = string.Empty;

    [JsonPropertyName("offPointIataCode")]
    public string OffPointIataCode { get; set; } = string.Empty;

    [JsonPropertyName("scheduledSegmentDuration")]
    public string ScheduledSegmentDuration { get; set; } = string.Empty;

    [JsonPropertyName("partnership")]
    public Partnership Partnership { get; set; } = new();
}

public class Partnership
{
    [JsonPropertyName("operatingFlight")]
    public OperatingFlight OperatingFlight { get; set; } = new();
}

public class OperatingFlight
{
    [JsonPropertyName("carrierCode")]
    public string CarrierCode { get; set; } = string.Empty;

    [JsonPropertyName("flightNumber")]
    public string FlightNumber { get; set; } = string.Empty;
}

public class FlightBoardMeta
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("links")]
    public FlightBoardLinks Links { get; set; } = new();
}

public class FlightBoardLinks
{
    [JsonPropertyName("self")]
    public string Self { get; set; } = string.Empty;
}

// For mock data when API is not available
public class MockFlightData
{
    public string FlightNumber { get; set; } = string.Empty;
    public string Airline { get; set; } = string.Empty;
    public string Destination { get; set; } = string.Empty;
    public string DestinationCity { get; set; } = string.Empty;
    public DateTime Time { get; set; }
    public string Status { get; set; } = "On Time";
    public string Gate { get; set; } = string.Empty;
    public string Terminal { get; set; } = string.Empty;
}