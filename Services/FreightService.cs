using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Services;

public record FreightEstimate(
    string OriginCep, string DestinationCep,
    double DistanceKm, double DurationHours,
    string VehicleType, decimal RatePerKm, decimal EstimatedCost);

public interface IFreightService
{
    Task<FreightEstimate?> CalculateAsync(string originCep, string destinationCep, string? vehicleType, CancellationToken ct);
}

public class FreightService(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    IConfiguration configuration,
    ILogger<FreightService> logger) : IFreightService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<FreightEstimate?> CalculateAsync(
        string originCep, string destinationCep, string? vehicleType, CancellationToken ct)
    {
        try
        {
            var apiKey = configuration["OpenRouteService:ApiKey"]
                ?? throw new InvalidOperationException("OpenRouteService API key not configured.");

            var originCoords = await GeocodeAsync(originCep, apiKey, ct);
            if (originCoords is null) return null;

            var destinationCoords = await GeocodeAsync(destinationCep, apiKey, ct);
            if (destinationCoords is null) return null;

            var (distanceKm, durationHours) = await GetRouteAsync(
                originCoords.Value, destinationCoords.Value, apiKey, ct);

            var vehicle = string.IsNullOrWhiteSpace(vehicleType) ? "Truck" : vehicleType.Trim();

            var rate = await db.FreightRates
                .AsNoTracking()
                .Where(r => r.IsActive && r.VehicleType == vehicle)
                .FirstOrDefaultAsync(ct);

            var ratePerKm = rate?.RatePerKm ?? 0m;
            var estimatedCost = (decimal)distanceKm * ratePerKm;

            return new FreightEstimate(
                originCep, destinationCep,
                Math.Round(distanceKm, 2),
                Math.Round(durationHours, 2),
                vehicle, ratePerKm,
                Math.Round(estimatedCost, 2));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to calculate freight from {Origin} to {Destination}", originCep, destinationCep);
            return null;
        }
    }

    private async Task<(double Lon, double Lat)?> GeocodeAsync(string cep, string apiKey, CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("OpenRouteService");
        var url = $"https://api.openrouteservice.org/geocode/search?api_key={apiKey}&text={cep},Brasil&boundary.country=BR&size=1";

        using var response = await client.GetAsync(url, ct);
        if (!response.IsSuccessStatusCode)
        {
            logger.LogWarning("Geocode request failed for CEP {Cep} with status {Status}", cep, response.StatusCode);
            return null;
        }

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var features = doc.RootElement.GetProperty("features");
        if (features.GetArrayLength() == 0)
        {
            logger.LogWarning("No geocode results for CEP {Cep}", cep);
            return null;
        }

        var coords = features[0].GetProperty("geometry").GetProperty("coordinates");
        var lon = coords[0].GetDouble();
        var lat = coords[1].GetDouble();
        return (lon, lat);
    }

    private async Task<(double DistanceKm, double DurationHours)> GetRouteAsync(
        (double Lon, double Lat) origin,
        (double Lon, double Lat) destination,
        string apiKey,
        CancellationToken ct)
    {
        using var client = httpClientFactory.CreateClient("OpenRouteService");
        var url = $"https://api.openrouteservice.org/v2/directions/driving-car" +
                  $"?api_key={apiKey}&start={origin.Lon},{origin.Lat}&end={destination.Lon},{destination.Lat}";

        using var response = await client.GetAsync(url, ct);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(ct);
        using var doc = JsonDocument.Parse(json);

        var segment = doc.RootElement
            .GetProperty("features")[0]
            .GetProperty("properties")
            .GetProperty("segments")[0]
            .GetProperty("summary");

        var distanceMeters = segment.GetProperty("distance").GetDouble();
        var durationSeconds = segment.GetProperty("duration").GetDouble();

        return (distanceMeters / 1000.0, durationSeconds / 3600.0);
    }
}
