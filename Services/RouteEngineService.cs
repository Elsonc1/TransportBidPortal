using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Contracts;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;

namespace TransportBidPortal.Services;

public interface IRouteEngineService
{
    Task<List<RouteSuggestion>> GenerateSuggestionsAsync(Guid shipperId, Guid originFacilityId, CancellationToken ct);
    Task GeocodeAndCacheFacilityAsync(ShipperFacility facility, CancellationToken ct);
    Task GeocodeAndCacheDeliveryPointAsync(ShipperDeliveryPoint point, CancellationToken ct);
}

public class RouteEngineService(
    IHttpClientFactory httpClientFactory,
    AppDbContext db,
    IConfiguration configuration,
    ILogger<RouteEngineService> logger) : IRouteEngineService
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    public async Task<List<RouteSuggestion>> GenerateSuggestionsAsync(
        Guid shipperId, Guid originFacilityId, CancellationToken ct)
    {
        var apiKey = configuration["OpenRouteService:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            logger.LogWarning("OpenRouteService API key not configured. Route engine disabled.");
            return [];
        }

        var facilities = await db.ShipperFacilities
            .Where(f => f.ShipperId == shipperId && f.IsActive)
            .ToListAsync(ct);

        var origin = facilities.FirstOrDefault(f => f.Id == originFacilityId);
        if (origin is null) return [];

        var destinations = await db.ShipperDeliveryPoints
            .Where(p => p.ShipperId == shipperId && p.IsActive)
            .ToListAsync(ct);
        if (destinations.Count == 0) return [];

        // Geocode missing coordinates
        await EnsureGeocodedAsync(origin, apiKey, ct);
        foreach (var dest in destinations)
            await EnsureGeocodedDeliveryPointAsync(dest, apiKey, ct);

        await db.SaveChangesAsync(ct);

        var validDests = destinations.Where(d => d.Latitude is not null && d.Longitude is not null).ToList();
        if (validDests.Count == 0 || origin.Latitude is null || origin.Longitude is null) return [];

        // Build coordinates list: index 0 = origin, 1..N = destinations
        var locations = new List<double[]> { new[] { (double)origin.Longitude!, (double)origin.Latitude! } };
        locations.AddRange(validDests.Select(d => new[] { (double)d.Longitude!, (double)d.Latitude! }));

        var destIndices = Enumerable.Range(1, validDests.Count).ToArray();
        var matrix = await CallMatrixApiAsync(locations, new[] { 0 }, destIndices, apiKey, ct);
        if (matrix is null) return [];

        // Resolve best freight rate per vehicle type
        var rates = await db.FreightRates.AsNoTracking().Where(r => r.IsActive).ToListAsync(ct);
        var defaultRate = rates.OrderBy(r => r.RatePerKm).FirstOrDefault()
                          ?? new FreightRate { VehicleType = "Truck", RatePerKm = 0m };

        var suggestions = new List<RouteSuggestion>();
        for (int i = 0; i < validDests.Count; i++)
        {
            // ORS returns null for unreachable routes
            if (matrix.Durations[0][i] < 0 || matrix.Distances[0][i] < 0) continue;

            var durationSec  = matrix.Durations[0][i];
            var distanceM    = matrix.Distances[0][i];
            if (durationSec <= 0 || distanceM <= 0) continue;

            var dest      = validDests[i];
            var distKm    = Math.Round(distanceM  / 1000.0, 2);
            var durHours  = Math.Round(durationSec / 3600.0, 2);
            var cost      = Math.Round((decimal)distKm * defaultRate.RatePerKm, 2);

            // Pedágio: placeholder até integração (ITollEstimator); mantido no contrato para o front/dashboard futuro.
            const decimal tollStub = 0m;
            var destRegion = BrazilMacroRegion.EffectiveRegion(dest.Region, dest.State);
            suggestions.Add(new RouteSuggestion(
                origin.Id, origin.Name, origin.City, origin.State, origin.ZipCode,
                dest.Id, dest.Name, dest.City, dest.State, dest.ZipCode, destRegion,
                distKm, durHours, cost, defaultRate.VehicleType, tollStub, 0));
        }

        return [.. suggestions
            .OrderBy(s => s.DurationHours)
            .Select((s, idx) => s with { Rank = idx + 1 })];
    }

    public async Task GeocodeAndCacheFacilityAsync(ShipperFacility facility, CancellationToken ct)
    {
        if (facility.Latitude is not null && facility.Longitude is not null) return;

        var apiKey = configuration["OpenRouteService:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        await EnsureGeocodedAsync(facility, apiKey, ct);
    }

    public async Task GeocodeAndCacheDeliveryPointAsync(ShipperDeliveryPoint point, CancellationToken ct)
    {
        if (point.Latitude is not null && point.Longitude is not null) return;

        var apiKey = configuration["OpenRouteService:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey)) return;

        await EnsureGeocodedDeliveryPointAsync(point, apiKey, ct);
    }

    // ----- private helpers -----

    private async Task EnsureGeocodedAsync(ShipperFacility facility, string apiKey, CancellationToken ct)
    {
        if (facility.Latitude is not null && facility.Longitude is not null) return;

        var coords = await GeocodeAsync(facility.ZipCode, facility.City, facility.State, apiKey, ct);
        if (coords is null) return;

        facility.Latitude  = (decimal)coords.Value.Lat;
        facility.Longitude = (decimal)coords.Value.Lon;
        logger.LogInformation("Geocoded facility {Name}: {Lat},{Lon}", facility.Name, facility.Latitude, facility.Longitude);
    }

    private async Task EnsureGeocodedDeliveryPointAsync(ShipperDeliveryPoint point, string apiKey, CancellationToken ct)
    {
        if (point.Latitude is not null && point.Longitude is not null) return;

        var coords = await GeocodeAsync(point.ZipCode, point.City, point.State, apiKey, ct);
        if (coords is null) return;

        point.Latitude  = (decimal)coords.Value.Lat;
        point.Longitude = (decimal)coords.Value.Lon;
        logger.LogInformation("Geocoded delivery point {Name}: {Lat},{Lon}", point.Name, point.Latitude, point.Longitude);
    }

    private async Task<(double Lon, double Lat)?> GeocodeAsync(
        string zipCode, string city, string state, string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("OpenRouteService");

            var searchText = !string.IsNullOrWhiteSpace(zipCode)
                ? $"{zipCode},Brasil"
                : $"{city},{state},Brasil";

            var url = $"https://api.openrouteservice.org/geocode/search"
                    + $"?api_key={apiKey}"
                    + $"&text={Uri.EscapeDataString(searchText)}"
                    + $"&boundary.country=BR&size=1";

            using var response = await client.GetAsync(url, ct);
            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Geocode failed for '{Text}' — HTTP {Status}", searchText, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            using var doc = JsonDocument.Parse(json);
            var features = doc.RootElement.GetProperty("features");
            if (features.GetArrayLength() == 0) return null;

            var coords = features[0].GetProperty("geometry").GetProperty("coordinates");
            return (coords[0].GetDouble(), coords[1].GetDouble());
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Geocode exception for ZipCode={ZipCode} City={City}", zipCode, city);
            return null;
        }
    }

    private async Task<OrsMatrixResult?> CallMatrixApiAsync(
        List<double[]> locations, int[] sources, int[] destinations, string apiKey, CancellationToken ct)
    {
        try
        {
            using var client = httpClientFactory.CreateClient("OpenRouteService");

            var payload = JsonSerializer.Serialize(new
            {
                locations,
                sources,
                destinations,
                metrics = new[] { "duration", "distance" }
            });

            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                "https://api.openrouteservice.org/v2/matrix/driving-car");
            request.Headers.Add("Authorization", apiKey);
            request.Content = new StringContent(payload, Encoding.UTF8, "application/json");

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
            {
                var err = await response.Content.ReadAsStringAsync(ct);
                logger.LogWarning("ORS Matrix returned {Status}: {Error}", response.StatusCode, err);
                return null;
            }

            var resultJson = await response.Content.ReadAsStringAsync(ct);
            return JsonSerializer.Deserialize<OrsMatrixResult>(resultJson, JsonOptions);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "ORS Matrix API call failed");
            return null;
        }
    }

    private record OrsMatrixResult(double[][] Durations, double[][] Distances);
}
