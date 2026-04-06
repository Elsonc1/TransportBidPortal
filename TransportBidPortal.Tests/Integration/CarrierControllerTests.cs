using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using TransportBidPortal.Contracts;
using TransportBidPortal.Domain;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Integration;

public class CarrierControllerTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly HttpClient _client;
    private readonly TestWebApplicationFactory _factory;
    private readonly Guid _carrierId;
    private readonly Guid _shipperId;

    public CarrierControllerTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
        (_shipperId, _carrierId) = SeedUsers();
        _client.SetAuth(_carrierId, "Test Carrier", UserRole.Carrier);
    }

    public void Dispose() => _client.Dispose();

    [Fact]
    public async Task InvitedBids_NoInvitations_ReturnsEmptyList()
    {
        var response = await _client.GetAsync("/api/carrier/invited-bids");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("[]");
    }

    [Fact]
    public async Task InvitedBids_WithInvitation_ReturnsBidDetails()
    {
        var bidId = SeedBidWithInvitation();

        var response = await _client.GetAsync("/api/carrier/invited-bids");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Test BID");
    }

    [Fact]
    public async Task BidDetails_ValidBid_ReturnsLanes()
    {
        var bidId = SeedBidWithInvitation();

        var response = await _client.GetAsync($"/api/carrier/bids/{bidId}");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Sao Paulo");
    }

    [Fact]
    public async Task SaveDraft_ValidProposal_Returns200()
    {
        var (bidId, laneId) = SeedBidWithLane();

        var request = new SaveProposalRequest(
            bidId,
            new List<ProposalLanePriceInput> { new(laneId, 50000m) },
            100m, true);

        var response = await _client.PostAsJsonAsync("/api/carrier/proposals/save-draft", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":");
    }

    [Fact]
    public async Task Submit_ValidProposal_Returns200WithSubmitted()
    {
        var (bidId, laneId) = SeedBidWithLane();

        var request = new SaveProposalRequest(
            bidId,
            new List<ProposalLanePriceInput> { new(laneId, 75000m) },
            200m, true);

        var response = await _client.PostAsJsonAsync("/api/carrier/proposals/submit", request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"status\":");
    }

    [Fact]
    public async Task ProposalVersions_MultipleVersions_ReturnsOrdered()
    {
        var (bidId, laneId) = SeedBidWithLane();

        await _client.PostAsJsonAsync("/api/carrier/proposals/save-draft",
            new SaveProposalRequest(bidId, new List<ProposalLanePriceInput> { new(laneId, 50000m) }, 100m, true));
        await _client.PostAsJsonAsync("/api/carrier/proposals/submit",
            new SaveProposalRequest(bidId, new List<ProposalLanePriceInput> { new(laneId, 45000m) }, 100m, true));

        var response = await _client.GetAsync($"/api/carrier/bids/{bidId}/proposal-versions");
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"version\":2");
    }

    [Fact]
    public async Task CarrierEndpoints_ShipperRole_Returns403()
    {
        var shipperClient = _factory.CreateClient();
        shipperClient.SetAuth(Guid.NewGuid(), "Shipper", UserRole.Shipper);

        var response = await shipperClient.GetAsync("/api/carrier/invited-bids");
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    private (Guid shipperId, Guid carrierId) SeedUsers()
    {
        var sId = Guid.NewGuid();
        var cId = Guid.NewGuid();
        using var db = _factory.CreateDbContext();
        db.Users.AddRange(
            new AppUser { Id = sId, Name = "S", Email = $"s_{sId:N}@t.com", Role = UserRole.Shipper, Company = "S", PasswordHash = "x" },
            new AppUser { Id = cId, Name = "C", Email = $"c_{cId:N}@t.com", Role = UserRole.Carrier, Company = "C", PasswordHash = "x" }
        );
        db.SaveChanges();
        return (sId, cId);
    }

    private Guid SeedBidWithInvitation()
    {
        using var db = _factory.CreateDbContext();
        var bid = new BidEvent
        {
            CreatedByShipperId = _shipperId, Title = "Test BID",
            DeadlineUtc = DateTime.UtcNow.AddDays(7), Status = BidStatus.Open,
            Lanes = new List<BidLane> { new() { Origin = "Sao Paulo", Destination = "Curitiba" } }
        };
        db.BidEvents.Add(bid);
        db.BidInvitations.Add(new BidInvitation { BidEventId = bid.Id, CarrierId = _carrierId });
        db.SaveChanges();
        return bid.Id;
    }

    private (Guid bidId, Guid laneId) SeedBidWithLane()
    {
        using var db = _factory.CreateDbContext();
        var lane = new BidLane { Origin = "SP", Destination = "RJ" };
        var bid = new BidEvent
        {
            CreatedByShipperId = _shipperId, Title = "BID for Proposal",
            DeadlineUtc = DateTime.UtcNow.AddDays(7), Status = BidStatus.Open,
            Lanes = new List<BidLane> { lane }
        };
        db.BidEvents.Add(bid);
        db.BidInvitations.Add(new BidInvitation { BidEventId = bid.Id, CarrierId = _carrierId });
        db.SaveChanges();
        return (bid.Id, lane.Id);
    }
}
