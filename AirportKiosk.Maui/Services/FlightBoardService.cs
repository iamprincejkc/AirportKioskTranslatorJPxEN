using AirportKiosk.Maui.Models;
using Microsoft.Extensions.Configuration;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AirportKiosk.Maui.Services;

public class FlightBoardService : IFlightService
{
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;
    private string? _accessToken;
    private DateTime _tokenExpiry = DateTime.MinValue;

    private const string BaseUrl = "https://test.api.amadeus.com";
    private const string AuthUrl = $"{BaseUrl}/v1/security/oauth2/token";

    public FlightBoardService(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;

        _clientId = configuration["Amadeus:ClientId"] ?? "";
        _clientSecret = configuration["Amadeus:ClientSecret"] ?? "";

        System.Diagnostics.Debug.WriteLine($"Flight Board Service - Client ID: {(!string.IsNullOrEmpty(_clientId) ? "SET" : "NOT SET")}");
    }

    public async Task<bool> IsServiceAvailableAsync()
    {
        try
        {
            if (string.IsNullOrEmpty(_clientId) || string.IsNullOrEmpty(_clientSecret))
            {
                return false;
            }

            await EnsureAuthenticatedAsync();
            return !string.IsNullOrEmpty(_accessToken);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Service availability check failed: {ex.Message}");
            return false;
        }
    }

    public async Task<List<FlightSchedule>> GetDeparturesAsync(string airportCode, DateTime date)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            // Use Airport Direct Destinations API to get real destinations from Boston
            var destinationsUrl = $"{BaseUrl}/v1/airport/direct-destinations?departureAirportCode={airportCode}&max=50";

            System.Diagnostics.Debug.WriteLine($"Direct destinations URL: {destinationsUrl}");

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.GetAsync(destinationsUrl);
            var content = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"Direct destinations API response: {response.StatusCode}");
            System.Diagnostics.Debug.WriteLine($"Direct destinations content: {content}");

            if (response.IsSuccessStatusCode)
            {
                var destinationsResponse = JsonSerializer.Deserialize<DirectDestinationsResponse>(content);
                return await CreateRealisticDeparturesFromDestinations(destinationsResponse?.Data ?? new List<DirectDestination>());
            }
            else
            {
                System.Diagnostics.Debug.WriteLine($"API failed, using fallback destinations");
                return await CreateRealisticBostonDepartures();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Departures error: {ex.Message}");
            return await CreateRealisticBostonDepartures();
        }
    }

    public async Task<List<FlightSchedule>> GetArrivalsAsync(string airportCode, DateTime date)
    {
        try
        {
            await EnsureAuthenticatedAsync();

            // Use the same destinations but create arrival flights FROM those destinations TO Boston
            var destinationsUrl = $"{BaseUrl}/v1/airport/direct-destinations?departureAirportCode={airportCode}&max=50";

            _httpClient.DefaultRequestHeaders.Clear();
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.GetAsync(destinationsUrl);
            var content = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                var destinationsResponse = JsonSerializer.Deserialize<DirectDestinationsResponse>(content);
                return await CreateRealisticArrivalsFromDestinations(destinationsResponse?.Data ?? new List<DirectDestination>());
            }
            else
            {
                return await CreateRealisticBostonArrivals();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Arrivals error: {ex.Message}");
            return await CreateRealisticBostonArrivals();
        }
    }

    public async Task<List<MockFlightData>> GetMockFlightDataAsync(FlightDirection direction)
    {
        return new List<MockFlightData>();
    }

    private async Task<List<FlightSchedule>> CreateRealisticDeparturesFromDestinations(List<DirectDestination> destinations)
    {
        var schedules = new List<FlightSchedule>();
        var now = DateTime.Now;
        var airlines = new[] { "AA", "DL", "UA", "B6", "WN", "AS" };
        var random = new Random();

        foreach (var destination in destinations.Take(20))
        {
            // Create multiple flights to each destination throughout the day
            var flightsToDestination = random.Next(1, 4); // 1-3 flights per destination

            for (int i = 0; i < flightsToDestination; i++)
            {
                var airline = airlines[random.Next(airlines.Length)];
                var flightNum = random.Next(100, 9999);
                var departureTime = now.AddHours(random.NextDouble() * 12); // Next 12 hours

                var schedule = new FlightSchedule
                {
                    FlightDesignator = new FlightDesignator
                    {
                        CarrierCode = airline,
                        FlightNumber = flightNum
                    },
                    Destination = destination.IataCode,
                    DepartureTime = departureTime,
                    Status = GetRandomStatus(),
                    Gate = $"{(char)('A' + random.Next(0, 3))}{random.Next(1, 30)}",
                    Terminal = ((char)('A' + random.Next(0, 3))).ToString(),
                    Airline = GetAirlineName(airline),
                    ScheduledDepartureDate = departureTime.ToString("yyyy-MM-dd")
                };

                schedules.Add(schedule);
            }
        }

        return schedules.OrderBy(s => s.DepartureTime).ToList();
    }

    private async Task<List<FlightSchedule>> CreateRealisticArrivalsFromDestinations(List<DirectDestination> destinations)
    {
        var schedules = new List<FlightSchedule>();
        var now = DateTime.Now;
        var airlines = new[] { "AA", "DL", "UA", "B6", "WN", "AS" };
        var random = new Random();

        foreach (var destination in destinations.Take(20))
        {
            // Create multiple flights from each destination throughout the day
            var flightsFromDestination = random.Next(1, 4); // 1-3 flights per destination

            for (int i = 0; i < flightsFromDestination; i++)
            {
                var airline = airlines[random.Next(airlines.Length)];
                var flightNum = random.Next(100, 9999);
                var arrivalTime = now.AddHours(random.NextDouble() * 12); // Next 12 hours

                var schedule = new FlightSchedule
                {
                    FlightDesignator = new FlightDesignator
                    {
                        CarrierCode = airline,
                        FlightNumber = flightNum
                    },
                    Origin = destination.IataCode,
                    ArrivalTime = arrivalTime,
                    Status = GetRandomStatus(),
                    Gate = $"{(char)('A' + random.Next(0, 3))}{random.Next(1, 30)}",
                    Terminal = ((char)('A' + random.Next(0, 3))).ToString(),
                    Airline = GetAirlineName(airline),
                    ScheduledDepartureDate = arrivalTime.ToString("yyyy-MM-dd")
                };

                schedules.Add(schedule);
            }
        }

        return schedules.OrderBy(s => s.ArrivalTime).ToList();
    }

    private async Task<List<FlightSchedule>> CreateRealisticBostonDepartures()
    {
        // Based on actual popular routes from Boston Logan
        var realDestinations = new[] { "LAX", "SFO", "ORD", "DEN", "SEA", "MIA", "ATL", "DFW", "LAS", "PHX", "MSP", "DTW", "CLT", "PHL", "BWI", "DCA", "LGA", "JFK", "MCO", "FLL" };
        var airlines = new[] { "AA", "DL", "UA", "B6", "WN", "AS" };
        var random = new Random();
        var now = DateTime.Now;
        var schedules = new List<FlightSchedule>();

        foreach (var destination in realDestinations)
        {
            var airline = airlines[random.Next(airlines.Length)];
            var flightNum = random.Next(100, 9999);
            var departureTime = now.AddHours(random.NextDouble() * 12);

            var schedule = new FlightSchedule
            {
                FlightDesignator = new FlightDesignator
                {
                    CarrierCode = airline,
                    FlightNumber = flightNum
                },
                Destination = destination,
                DepartureTime = departureTime,
                Status = GetRandomStatus(),
                Gate = $"{(char)('A' + random.Next(0, 3))}{random.Next(1, 30)}",
                Terminal = ((char)('A' + random.Next(0, 3))).ToString(),
                Airline = GetAirlineName(airline),
                ScheduledDepartureDate = departureTime.ToString("yyyy-MM-dd")
            };

            schedules.Add(schedule);
        }

        return schedules.OrderBy(s => s.DepartureTime).ToList();
    }

    private async Task<List<FlightSchedule>> CreateRealisticBostonArrivals()
    {
        var realOrigins = new[] { "LAX", "SFO", "ORD", "DEN", "SEA", "MIA", "ATL", "DFW", "LAS", "PHX", "MSP", "DTW", "CLT", "PHL", "BWI", "DCA", "LGA", "JFK", "MCO", "FLL" };
        var airlines = new[] { "AA", "DL", "UA", "B6", "WN", "AS" };
        var random = new Random();
        var now = DateTime.Now;
        var schedules = new List<FlightSchedule>();

        foreach (var origin in realOrigins)
        {
            var airline = airlines[random.Next(airlines.Length)];
            var flightNum = random.Next(100, 9999);
            var arrivalTime = now.AddHours(random.NextDouble() * 12);

            var schedule = new FlightSchedule
            {
                FlightDesignator = new FlightDesignator
                {
                    CarrierCode = airline,
                    FlightNumber = flightNum
                },
                Origin = origin,
                ArrivalTime = arrivalTime,
                Status = GetRandomStatus(),
                Gate = $"{(char)('A' + random.Next(0, 3))}{random.Next(1, 30)}",
                Terminal = ((char)('A' + random.Next(0, 3))).ToString(),
                Airline = GetAirlineName(airline),
                ScheduledDepartureDate = arrivalTime.ToString("yyyy-MM-dd")
            };

            schedules.Add(schedule);
        }

        return schedules.OrderBy(s => s.ArrivalTime).ToList();
    }

    private string GetRandomStatus()
    {
        var statuses = new[] { "On Time", "On Time", "On Time", "Boarding", "Delayed", "Departed", "Arrived" };
        var random = new Random();
        return statuses[random.Next(statuses.Length)];
    }

    private string GetAirlineName(string carrierCode)
    {
        return carrierCode switch
        {
            "AA" => "American Airlines",
            "DL" => "Delta Air Lines",
            "UA" => "United Airlines",
            "B6" => "JetBlue Airways",
            "WN" => "Southwest Airlines",
            "AS" => "Alaska Airlines",
            "NK" => "Spirit Airlines",
            "F9" => "Frontier Airlines",
            _ => carrierCode
        };
    }

    private async Task EnsureAuthenticatedAsync()
    {
        if (!string.IsNullOrEmpty(_accessToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return;
        }

        await AuthenticateAsync();
    }

    private async Task AuthenticateAsync()
    {
        try
        {
            System.Diagnostics.Debug.WriteLine($"Authenticating with Amadeus API...");

            var requestBody = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("grant_type", "client_credentials"),
                new KeyValuePair<string, string>("client_id", _clientId),
                new KeyValuePair<string, string>("client_secret", _clientSecret)
            });

            var response = await _httpClient.PostAsync(AuthUrl, requestBody);
            var responseContent = await response.Content.ReadAsStringAsync();

            System.Diagnostics.Debug.WriteLine($"Auth Response Status: {response.StatusCode}");

            if (response.IsSuccessStatusCode)
            {
                var authResponse = JsonSerializer.Deserialize<AmadeusAuthResponse>(responseContent);

                if (authResponse != null && !string.IsNullOrEmpty(authResponse.AccessToken))
                {
                    _accessToken = authResponse.AccessToken;
                    _tokenExpiry = DateTime.UtcNow.AddSeconds(authResponse.ExpiresIn - 60);
                    System.Diagnostics.Debug.WriteLine("Amadeus authentication successful");
                    return;
                }
            }

            throw new FlightServiceException($"Authentication failed: {response.StatusCode}");
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Authentication error: {ex.Message}");
            throw new FlightServiceException("Failed to authenticate with Amadeus API.", ex);
        }
    }
}

// Model for Airport Direct Destinations API
public class DirectDestinationsResponse
{
    [JsonPropertyName("data")]
    public List<DirectDestination> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public DirectDestinationsMeta Meta { get; set; } = new();
}

public class DirectDestination
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("subtype")]
    public string Subtype { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("iataCode")]
    public string IataCode { get; set; } = string.Empty;
}

public class DirectDestinationsMeta
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("links")]
    public DirectDestinationsLinks Links { get; set; } = new();
}

public class DirectDestinationsLinks
{
    [JsonPropertyName("self")]
    public string Self { get; set; } = string.Empty;
}