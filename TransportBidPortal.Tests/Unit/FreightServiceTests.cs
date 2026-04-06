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

public class FreightServiceTests : IDisposable
{
    private readonly Data.AppDbContext _db;
    private readonly IConfiguration _config;

    public FreightServiceTests()
    {
        _db = InMemoryDbHelper.Create();
        _config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["OpenRouteService:ApiKey"] = "test-key"
            })
            .Build();

        _db.FreightRates.Add(new FreightRate
        {
            VehicleType = "Truck",
            RatePerKm = 3.50m,
            IsActive = true
        });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task CalculateAsync_ValidCeps_ReturnsEstimate()
    {
        var geocodeJson = """
        {"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}
        """;
        var routeJson = """
        {"features":[{"properties":{"segments":[{"summary":{"distance":500000,"duration":21600}}]}}]}
        """;

        var sut = CreateService(geocodeJson, routeJson);
        var result = await sut.CalculateAsync("01001000", "80010000", "Truck", CancellationToken.None);

        result.Should().NotBeNull();
        result!.DistanceKm.Should().Be(500.0);
        result.DurationHours.Should().Be(6.0);
        result.RatePerKm.Should().Be(3.50m);
        result.EstimatedCost.Should().Be(1750.00m);
        result.VehicleType.Should().Be("Truck");
    }

    [Fact]
    public async Task CalculateAsync_NoVehicleType_DefaultsToTruck()
    {
        var geocodeJson = """{"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}""";
        var routeJson = """{"features":[{"properties":{"segments":[{"summary":{"distance":100000,"duration":3600}}]}}]}""";

        var sut = CreateService(geocodeJson, routeJson);
        var result = await sut.CalculateAsync("01001000", "80010000", null, CancellationToken.None);

        result.Should().NotBeNull();
        result!.VehicleType.Should().Be("Truck");
    }

    [Fact]
    public async Task CalculateAsync_GeocodeFailsForOrigin_ReturnsNull()
    {
        var emptyGeocode = """{"features":[]}""";
        var sut = CreateService(emptyGeocode, "{}");
        var result = await sut.CalculateAsync("99999999", "80010000", "Truck", CancellationToken.None);
        result.Should().BeNull();
    }

    [Fact]
    public async Task CalculateAsync_NoRateForVehicle_CostIsZero()
    {
        var geocodeJson = """{"features":[{"geometry":{"coordinates":[-46.633,-23.550]}}]}""";
        var routeJson = """{"features":[{"properties":{"segments":[{"summary":{"distance":100000,"duration":3600}}]}}]}""";

        var sut = CreateService(geocodeJson, routeJson);
        var result = await sut.CalculateAsync("01001000", "80010000", "Helicopter", CancellationToken.None);

        result.Should().NotBeNull();
        result!.RatePerKm.Should().Be(0m);
        result.EstimatedCost.Should().Be(0m);
    }

    [Fact]
    public async Task CalculateAsync_MissingApiKey_ReturnsNull()
    {
        var emptyConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>())
            .Build();
        var sut = CreateService("{}", "{}", emptyConfig);
        var result = await sut.CalculateAsync("01001000", "80010000", "Truck", CancellationToken.None);
        result.Should().BeNull();
    }

    private FreightService CreateService(string geocodeResponse, string routeResponse, IConfiguration? config = null)
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
                var url = req.RequestUri?.ToString() ?? "";
                var body = url.Contains("directions") ? routeResponse : geocodeResponse;
                var response = new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(body)
                };
                return Task.FromResult(response);
            });

        var factoryMock = new Mock<IHttpClientFactory>();
        factoryMock.Setup(f => f.CreateClient("OpenRouteService"))
            .Returns(() => new HttpClient(handlerMock.Object));

        return new FreightService(factoryMock.Object, _db, config ?? _config, Mock.Of<ILogger<FreightService>>());
    }
}
