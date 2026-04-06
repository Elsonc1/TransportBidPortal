using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TransportBidPortal.Controllers;
using TransportBidPortal.Domain;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Integration;

public class FreightControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;

    public FreightControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        var userId = SeedUser();
        _client.SetAuth(userId, "Test User", UserRole.Shipper);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task GetRates_ReturnsActiveRates()
    {
        SeedRates();
        var response = await _client.GetAsync("/api/freight/rates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Truck");
    }

    [Fact]
    public async Task SaveRate_ValidRequest_Returns200()
    {
        var request = new SaveFreightRateRequest($"Van_{Guid.NewGuid():N}", 2.50m, "Small van");
        var response = await _client.PostAsJsonAsync("/api/freight/rates", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Estimate_Unauthenticated_Returns401()
    {
        var anonClient = _factory.CreateClient();
        var response = await anonClient.GetAsync("/api/freight/estimate?originCep=01001000&destinationCep=80010000");
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private Guid SeedUser()
    {
        var id = Guid.NewGuid();
        using var db = _factory.CreateDbContext();
        db.Users.Add(new AppUser
        {
            Id = id, Name = "U", Email = $"f_{id:N}@t.com",
            PasswordHash = "x", Role = UserRole.Shipper, Company = "X"
        });
        db.SaveChanges();
        return id;
    }

    private void SeedRates()
    {
        using var db = _factory.CreateDbContext();
        if (!db.FreightRates.Any())
        {
            db.FreightRates.Add(new FreightRate { VehicleType = "Truck", RatePerKm = 3.50m, IsActive = true });
            db.SaveChanges();
        }
    }
}
