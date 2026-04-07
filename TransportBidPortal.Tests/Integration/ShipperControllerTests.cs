using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TransportBidPortal.Contracts;
using TransportBidPortal.Domain;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Integration;

public class ShipperControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;
    private readonly Guid _shipperId;

    public ShipperControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        _shipperId = SeedShipper();
        _client.SetAuth(_shipperId, "Test Shipper", UserRole.Shipper);
    }

    public void Dispose() => _client.Dispose();

    // --- Templates ---

    [Fact]
    public async Task CreateTemplate_ValidRequest_Returns200()
    {
        var request = new CreateBidTemplateRequest(
            $"Template_{Guid.NewGuid():N}",
            false,
            new List<TemplateColumnInput>
            {
                new("origin", "Origem", "Origin", true, "text", 1),
                new("destination", "Destino", "Destination", true, "text", 2)
            });

        var response = await _client.PostAsJsonAsync("/api/shipper/templates", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task GetTemplates_ReturnsTemplateList()
    {
        var response = await _client.GetAsync("/api/shipper/templates");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- Facilities ---

    [Fact]
    public async Task CreateFacility_Matriz_Returns200()
    {
        var request = new SaveFacilityRequest(
            "CD Principal", "Matriz", "11222333000101", "Rua A, 100",
            "Sao Paulo", "SP", "01001000", "Brasil", null, null, true);

        var response = await _client.PostAsJsonAsync("/api/shipper/facilities", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateFacility_FilialWithoutMatriz_Returns400()
    {
        var request = new SaveFacilityRequest(
            "Filial", "Filial", "99887766000200", "Rua B, 200",
            "Curitiba", "PR", "80010000", "Brasil", null, null, true);

        var response = await _client.PostAsJsonAsync("/api/shipper/facilities", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CreateFacility_InvalidCnpj_Returns400()
    {
        var request = new SaveFacilityRequest(
            "Bad", "Matriz", "123", "Rua C",
            "SP", "SP", "01001000", "Brasil", null, null, true);

        var response = await _client.PostAsJsonAsync("/api/shipper/facilities", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetFacilities_ReturnsListForShipper()
    {
        var response = await _client.GetAsync("/api/shipper/facilities");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- BIDs ---

    [Fact]
    public async Task CreateBid_ValidRequest_Returns200()
    {
        var request = new CreateBidRequest(
            "Test BID", AuctionType.Open, DateTime.UtcNow.AddDays(7),
            "ANTT", 100000m, null, null,
            new List<BidLaneInput>
            {
                new("Sao Paulo", "Curitiba", TestSeedIds.DeliveryPointCuritibaId, "CIF", 500, "48h", "Truck", null, null, "Sul")
            });

        var response = await _client.PostAsJsonAsync("/api/shipper/bids", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task CreateBid_NoLanes_Returns400()
    {
        var request = new CreateBidRequest(
            "Empty BID", AuctionType.Sealed, null, null, 0, null, null,
            new List<BidLaneInput>());

        var response = await _client.PostAsJsonAsync("/api/shipper/bids", request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task GetBids_ReturnsListForShipper()
    {
        var response = await _client.GetAsync("/api/shipper/bids");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    // --- RBAC ---

    [Fact]
    public async Task ShipperEndpoints_CarrierRole_Returns403()
    {
        var carrierClient = _factory.CreateClient();
        carrierClient.SetAuth(Guid.NewGuid(), "Carrier", UserRole.Carrier);

        var response = await carrierClient.GetAsync("/api/shipper/templates");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private Guid SeedShipper()
    {
        using (var db = _factory.CreateDbContext())
        {
            return TestSeedIds.DemoShipperId;
        }
    }
}
