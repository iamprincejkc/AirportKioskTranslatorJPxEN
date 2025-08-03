using System.Text.Json.Serialization;

namespace AirportKiosk.Maui.Models;

public class FlightSearchRequest
{
    public string OriginLocationCode { get; set; } = string.Empty;
    public string DestinationLocationCode { get; set; } = string.Empty;
    public DateTime DepartureDate { get; set; } = DateTime.Today;
    public DateTime? ReturnDate { get; set; }
    public int Adults { get; set; } = 1;
    public string CurrencyCode { get; set; } = "USD";
    public bool NonStop { get; set; } = false;
    public int MaxResults { get; set; } = 10;
}

public class FlightSearchResponse
{
    [JsonPropertyName("data")]
    public List<FlightOffer> Data { get; set; } = new();

    [JsonPropertyName("meta")]
    public Meta Meta { get; set; } = new();
}

public class FlightOffer
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("source")]
    public string Source { get; set; } = string.Empty;

    [JsonPropertyName("instantTicketingRequired")]
    public bool InstantTicketingRequired { get; set; }

    [JsonPropertyName("nonHomogeneous")]
    public bool NonHomogeneous { get; set; }

    [JsonPropertyName("oneWay")]
    public bool OneWay { get; set; }

    [JsonPropertyName("lastTicketingDate")]
    public string LastTicketingDate { get; set; } = string.Empty;

    [JsonPropertyName("numberOfBookableSeats")]
    public int NumberOfBookableSeats { get; set; }

    [JsonPropertyName("itineraries")]
    public List<Itinerary> Itineraries { get; set; } = new();

    [JsonPropertyName("price")]
    public Price Price { get; set; } = new();

    [JsonPropertyName("pricingOptions")]
    public PricingOptions PricingOptions { get; set; } = new();

    [JsonPropertyName("validatingAirlineCodes")]
    public List<string> ValidatingAirlineCodes { get; set; } = new();

    [JsonPropertyName("travelerPricings")]
    public List<TravelerPricing> TravelerPricings { get; set; } = new();
}

public class Itinerary
{
    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("segments")]
    public List<Segment> Segments { get; set; } = new();
}

public class Segment
{
    [JsonPropertyName("departure")]
    public FlightPoint Departure { get; set; } = new();

    [JsonPropertyName("arrival")]
    public FlightPoint Arrival { get; set; } = new();

    [JsonPropertyName("carrierCode")]
    public string CarrierCode { get; set; } = string.Empty;

    [JsonPropertyName("number")]
    public string Number { get; set; } = string.Empty;

    [JsonPropertyName("aircraft")]
    public Aircraft Aircraft { get; set; } = new();

    [JsonPropertyName("operating")]
    public Operating Operating { get; set; } = new();

    [JsonPropertyName("duration")]
    public string Duration { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("numberOfStops")]
    public int NumberOfStops { get; set; }

    [JsonPropertyName("blacklistedInEU")]
    public bool BlacklistedInEU { get; set; }
}

public class FlightPoint
{
    [JsonPropertyName("iataCode")]
    public string IataCode { get; set; } = string.Empty;

    [JsonPropertyName("terminal")]
    public string Terminal { get; set; } = string.Empty;

    [JsonPropertyName("at")]
    public DateTime At { get; set; }
}

public class Aircraft
{
    [JsonPropertyName("code")]
    public string Code { get; set; } = string.Empty;
}

public class Operating
{
    [JsonPropertyName("carrierCode")]
    public string CarrierCode { get; set; } = string.Empty;
}

public class Price
{
    [JsonPropertyName("currency")]
    public string Currency { get; set; } = string.Empty;

    [JsonPropertyName("total")]
    public string Total { get; set; } = string.Empty;

    [JsonPropertyName("base")]
    public string Base { get; set; } = string.Empty;

    [JsonPropertyName("fees")]
    public List<Fee> Fees { get; set; } = new();

    [JsonPropertyName("grandTotal")]
    public string GrandTotal { get; set; } = string.Empty;
}

public class Fee
{
    [JsonPropertyName("amount")]
    public string Amount { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;
}

public class PricingOptions
{
    [JsonPropertyName("fareType")]
    public List<string> FareType { get; set; } = new();

    [JsonPropertyName("includedCheckedBagsOnly")]
    public bool IncludedCheckedBagsOnly { get; set; }
}

public class TravelerPricing
{
    [JsonPropertyName("travelerId")]
    public string TravelerId { get; set; } = string.Empty;

    [JsonPropertyName("fareOption")]
    public string FareOption { get; set; } = string.Empty;

    [JsonPropertyName("travelerType")]
    public string TravelerType { get; set; } = string.Empty;

    [JsonPropertyName("price")]
    public Price Price { get; set; } = new();

    [JsonPropertyName("fareDetailsBySegment")]
    public List<FareDetailsBySegment> FareDetailsBySegment { get; set; } = new();
}

public class FareDetailsBySegment
{
    [JsonPropertyName("segmentId")]
    public string SegmentId { get; set; } = string.Empty;

    [JsonPropertyName("cabin")]
    public string Cabin { get; set; } = string.Empty;

    [JsonPropertyName("fareBasis")]
    public string FareBasis { get; set; } = string.Empty;

    [JsonPropertyName("class")]
    public string Class { get; set; } = string.Empty;

    [JsonPropertyName("includedCheckedBags")]
    public IncludedCheckedBags IncludedCheckedBags { get; set; } = new();
}

public class IncludedCheckedBags
{
    [JsonPropertyName("quantity")]
    public int Quantity { get; set; }
}

public class Meta
{
    [JsonPropertyName("count")]
    public int Count { get; set; }

    [JsonPropertyName("links")]
    public Links Links { get; set; } = new();
}

public class Links
{
    [JsonPropertyName("self")]
    public string Self { get; set; } = string.Empty;
}

// For Airport/City search
public class LocationSearchResponse
{
    [JsonPropertyName("data")]
    public List<Location> Data { get; set; } = new();
}

public class Location
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("subType")]
    public string SubType { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("detailedName")]
    public string DetailedName { get; set; } = string.Empty;

    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("self")]
    public Self Self { get; set; } = new();

    [JsonPropertyName("timeZoneOffset")]
    public string TimeZoneOffset { get; set; } = string.Empty;

    [JsonPropertyName("iataCode")]
    public string IataCode { get; set; } = string.Empty;

    [JsonPropertyName("geoCode")]
    public GeoCode GeoCode { get; set; } = new();

    [JsonPropertyName("address")]
    public Address Address { get; set; } = new();

    [JsonPropertyName("analytics")]
    public Analytics Analytics { get; set; } = new();
}

public class Self
{
    [JsonPropertyName("href")]
    public string Href { get; set; } = string.Empty;

    [JsonPropertyName("methods")]
    public List<string> Methods { get; set; } = new();
}

public class GeoCode
{
    [JsonPropertyName("latitude")]
    public double Latitude { get; set; }

    [JsonPropertyName("longitude")]
    public double Longitude { get; set; }
}

public class Address
{
    [JsonPropertyName("cityName")]
    public string CityName { get; set; } = string.Empty;

    [JsonPropertyName("cityCode")]
    public string CityCode { get; set; } = string.Empty;

    [JsonPropertyName("countryName")]
    public string CountryName { get; set; } = string.Empty;

    [JsonPropertyName("countryCode")]
    public string CountryCode { get; set; } = string.Empty;

    [JsonPropertyName("regionCode")]
    public string RegionCode { get; set; } = string.Empty;
}

public class Analytics
{
    [JsonPropertyName("travelers")]
    public Travelers Travelers { get; set; } = new();
}

public class Travelers
{
    [JsonPropertyName("score")]
    public int Score { get; set; }
}

// Authentication response
public class AmadeusAuthResponse
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = string.Empty;

    [JsonPropertyName("username")]
    public string Username { get; set; } = string.Empty;

    [JsonPropertyName("application_name")]
    public string ApplicationName { get; set; } = string.Empty;

    [JsonPropertyName("client_id")]
    public string ClientId { get; set; } = string.Empty;

    [JsonPropertyName("token_type")]
    public string TokenType { get; set; } = string.Empty;

    [JsonPropertyName("access_token")]
    public string AccessToken { get; set; } = string.Empty;

    [JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [JsonPropertyName("state")]
    public string State { get; set; } = string.Empty;

    [JsonPropertyName("scope")]
    public string Scope { get; set; } = string.Empty;
}