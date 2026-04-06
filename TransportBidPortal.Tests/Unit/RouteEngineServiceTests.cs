using System.Net;
using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using Moq.Protected;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Unit;

public class RouteEngineServiceTests : IDisposable
{
    private readonly Data.AppDbContext _db;
    private readonly IConfiguration _config;
    private readonly Guid _shipperId = Guid.NewGuid();

    public RouteEngineServiceTests()
    {
        _db = InMemoryDbHelper.Create();
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouteService:ApiKey"] = "test-key"
            })
            .Build();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task GenerateSuggestions_NoApiKey_ReturnsEmpty()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var sut = CreateService("{}", "{}", emptyConfig);
        var result = await sut.GenerateSuggestionsAsync(_shipperId, Guid.NewGuid(), CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSuggestions_OriginNotFound_ReturnsEmpty()
    {
        var sut = CreateService("{}", "{}");
        var result = await sut.GenerateSuggestionsAsync(_shipperId, Guid.NewGuid(), CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSuggestions_NoDestinations_ReturnsEmpty()
    {
        var origin = AddFacility("CD SP", "01001000", -23.55m, -46.63m);
        await _db.SaveChangesAsync();

        var sut = CreateService("{}", "{}");
        var result = await sut.GenerateSuggestionsAsync(_shipperId, origin.Id, CancellationToken.None);
        result.Should().BeEmpty();
    }

    [Fact]
    public async Task GenerateSuggestions_WithDestinations_ReturnsSortedByTime()
    {
        var origin = AddFacility("CD SP", "01001000", -23.55m, -46.63m);
        var dest1 = AddFacility("CD RJ", "20040020", -22.90m, -43.17m);
        var dest2 = AddFacility("CD CWB", "80010000", -25.43m, -49.27m);

        _db.FreightRates.Add(new FreightRate { VehicleType = "Truck", RatePerKm = 2.0m, IsActive = true });
        await _db.SaveChangesAsync();

        var geocodeJson = """{"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}""";
        // Matrix: dest1 (RJ) closer/faster, dest2 (CWB) farther/slower
        var matrixJson = """
        {
            "durations": [[7200, 14400]],
            "distances": [[400000, 800000]]
        }
        """;

        var sut = CreateService(geocodeJson, matrixJson);
        var result = await sut.GenerateSuggestionsAsync(_shipperId, origin.Id, CancellationToken.None);

        result.Should().HaveCount(2);
        result[0].Rank.Should().Be(1);
        result[0].DurationHours.Should().BeLessThan(result[1].DurationHours);
        result[0].DestName.Should().Be("CD RJ");
        result[1].Rank.Should().Be(2);
    }

    [Fact]
    public async Task GenerateSuggestions_EstimatedCostCalculated()
    {
        var origin = AddFacility("CD SP", "01001000", -23.55m, -46.63m);
        AddFacility("CD RJ", "20040020", -22.90m, -43.17m);

        // Clear any seeded rates and add our own
        _db.FreightRates.RemoveRange(_db.FreightRates);
        _db.FreightRates.Add(new FreightRate { VehicleType = "TestVehicle", RatePerKm = 5.0m, IsActive = true });
        await _db.SaveChangesAsync();

        var geocodeJson = """{"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}""";
        var matrixJson = """{"durations": [[3600]], "distances": [[100000]]}""";

        var sut = CreateService(geocodeJson, matrixJson);
        var result = await sut.GenerateSuggestionsAsync(_shipperId, origin.Id, CancellationToken.None);

        result.Should().HaveCount(1);
        result[0].DistanceKm.Should().Be(100.0);
        result[0].EstimatedCost.Should().Be(500.0m); // 100km * 5.0/km
    }

    [Fact]
    public async Task GeocodeAndCacheFacility_AlreadyGeocoded_SkipsApiCall()
    {
        var facility = new ShipperFacility
        {
            ShipperId = _shipperId, Name = "CD", ZipCode = "01001000",
            City = "SP", State = "SP", Latitude = -23.55m, Longitude = -46.63m
        };

        var sut = CreateService("{}", "{}");
        await sut.GeocodeAndCacheFacilityAsync(facility, CancellationToken.None);

        facility.Latitude.Should().Be(-23.55m);
        facility.Longitude.Should().Be(-46.63m);
    }

    [Fact]
    public async Task GeocodeAndCacheFacility_NoCoords_CallsApiAndSets()
    {
        var facility = new ShipperFacility
        {
            ShipperId = _shipperId, Name = "CD", ZipCode = "01001000",
            City = "SP", State = "SP", Latitude = null, Longitude = null
        };

        var geocodeJson = """{"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}""";
        var sut = CreateService(geocodeJson, "{}");
        await sut.GeocodeAndCacheFacilityAsync(facility, CancellationToken.None);

        facility.Latitude.Should().NotBeNull();
        facility.Longitude.Should().NotBeNull();
    }

    private ShipperFacility AddFacility(string name, string zip, decimal? lat, decimal? lon)
    {
        var f = new ShipperFacility
        {
            ShipperId = _shipperId, Name = name, ZipCode = zip,
            City = name, State = "XX", Latitude = lat, Longitude = lon, IsActive = true
        };
        _db.ShipperFacilities.Add(f);
        return f;
    }

    private RouteEngineService CreateService(string geocodeResponse, string matrixResponse, IConfiguration? config = null)
    {
        var handlerMock = new Mock<HttpMessageHandler>();
        handlerMock
            .Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .Returns((HttpRequestMessage req, CancellationToken _) =>
            {
                var isMatrix = req.Method == HttpMethod.Post;
                var body = isMatrix ? matrixResponse : geocodeResponse;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                });
            });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("OpenRouteService"))
            .Returns(() => new HttpClient(handlerMock.Object));

        return new RouteEngineService(factoryMock.Object, _db, config ?? _config, Mock.Of<ILogger<RouteEngineService>>());
    }
}
