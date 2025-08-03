using AirportKiosk.Maui.Models;

namespace AirportKiosk.Maui.Services;

public interface IFlightService
{
    Task<List<FlightSchedule>> GetDeparturesAsync(string airportCode, DateTime date);
    Task<List<FlightSchedule>> GetArrivalsAsync(string airportCode, DateTime date);
    Task<List<MockFlightData>> GetMockFlightDataAsync(FlightDirection direction);
    Task<bool> IsServiceAvailableAsync();
}

public class FlightServiceException : Exception
{
    public FlightServiceException(string message) : base(message) { }
    public FlightServiceException(string message, Exception innerException) : base(message, innerException) { }
}