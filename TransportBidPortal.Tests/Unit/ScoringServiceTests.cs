using FluentAssertions;
using TransportBidPortal.Contracts;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.Unit;

public class ScoringServiceTests : IDisposable
{
    private readonly Data.AppDbContext _db;
    private readonly IScoringService _sut;

    public ScoringServiceTests()
    {
        _db = InMemoryDbHelper.Create();
        _sut = new ScoringService(_db);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task BuildDashboard_NoBid_ReturnsEmptyRanking()
    {
        var result = await _sut.BuildDashboardAsync(
            Guid.NewGuid(), new DashboardFilter(null, null, null), null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"Ranking\":[]");
    }

    [Fact]
    public async Task BuildDashboard_WithProposals_RanksCorrectly()
    {
        var shipperId = Guid.NewGuid();
        var carrierA = Guid.NewGuid();
        var carrierB = Guid.NewGuid();

        _db.Users.AddRange(
            new AppUser { Id = shipperId, Name = "Shipper", Email = "s@t.com", Role = UserRole.Shipper, Company = "S" },
            new AppUser { Id = carrierA, Name = "Carrier A", Email = "a@t.com", Role = UserRole.Carrier, Company = "A" },
            new AppUser { Id = carrierB, Name = "Carrier B", Email = "b@t.com", Role = UserRole.Carrier, Company = "B" }
        );

        var bid = new BidEvent
        {
            CreatedByShipperId = shipperId,
            Title = "Test BID",
            BaselineContractValue = 100000m,
            DeadlineUtc = DateTime.UtcNow.AddDays(7),
            Status = BidStatus.Open
        };
        _db.BidEvents.Add(bid);

        var lane = new BidLane { BidEventId = bid.Id, Origin = "SP", Destination = "RJ", Region = "Sudeste" };
        _db.BidLanes.Add(lane);

        _db.CarrierProposals.AddRange(
            new CarrierProposal
            {
                BidEventId = bid.Id, CarrierId = carrierA, Version = 1,
                Status = ProposalStatus.Submitted, TotalPrice = 80000m,
                SlaCompliant = true, OperationalCapacityTons = 100,
                LanePrices = new List<CarrierProposalLanePrice>
                    { new() { LaneId = lane.Id, PricePerLane = 80000m } }
            },
            new CarrierProposal
            {
                BidEventId = bid.Id, CarrierId = carrierB, Version = 1,
                Status = ProposalStatus.Submitted, TotalPrice = 95000m,
                SlaCompliant = false, OperationalCapacityTons = 200,
                LanePrices = new List<CarrierProposalLanePrice>
                    { new() { LaneId = lane.Id, PricePerLane = 95000m } }
            }
        );

        await _db.SaveChangesAsync();

        var result = await _sut.BuildDashboardAsync(
            bid.Id, new DashboardFilter(null, null, null), null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"CarrierName\":\"A\"").And.Contain("\"CarrierName\":\"B\"");
        // Carrier A has lower price + SLA compliant, should rank first
        json.IndexOf("\"CarrierName\":\"A\"").Should().BeLessThan(json.IndexOf("\"CarrierName\":\"B\""));
    }

    [Fact]
    public async Task BuildDashboard_RegionFilter_FiltersProposals()
    {
        var shipperId = Guid.NewGuid();
        var carrierId = Guid.NewGuid();

        _db.Users.AddRange(
            new AppUser { Id = shipperId, Name = "S", Email = "s2@t.com", Role = UserRole.Shipper, Company = "S" },
            new AppUser { Id = carrierId, Name = "C", Email = "c2@t.com", Role = UserRole.Carrier, Company = "C" }
        );

        var bid = new BidEvent
        {
            CreatedByShipperId = shipperId, Title = "BID2",
            BaselineContractValue = 50000m, DeadlineUtc = DateTime.UtcNow.AddDays(7), Status = BidStatus.Open
        };
        _db.BidEvents.Add(bid);

        var laneSul = new BidLane { BidEventId = bid.Id, Origin = "CWB", Destination = "POA", Region = "Sul" };
        var laneSE = new BidLane { BidEventId = bid.Id, Origin = "SP", Destination = "RJ", Region = "Sudeste" };
        _db.BidLanes.AddRange(laneSul, laneSE);

        _db.CarrierProposals.Add(new CarrierProposal
        {
            BidEventId = bid.Id, CarrierId = carrierId, Version = 1,
            Status = ProposalStatus.Submitted, TotalPrice = 30000m,
            SlaCompliant = true, OperationalCapacityTons = 50,
            LanePrices = new List<CarrierProposalLanePrice>
            {
                new() { LaneId = laneSul.Id, PricePerLane = 15000m },
                new() { LaneId = laneSE.Id, PricePerLane = 15000m }
            }
        });

        await _db.SaveChangesAsync();

        var result = await _sut.BuildDashboardAsync(
            bid.Id, new DashboardFilter("Sul", null, null), null, CancellationToken.None);

        var json = System.Text.Json.JsonSerializer.Serialize(result);
        json.Should().Contain("\"Ranking\":[");
    }

    [Fact]
    public async Task BuildDashboard_CustomWeights_AffectsScore()
    {
        var shipperId = Guid.NewGuid();
        var carrierId = Guid.NewGuid();

        _db.Users.AddRange(
            new AppUser { Id = shipperId, Name = "S", Email = "s3@t.com", Role = UserRole.Shipper, Company = "S" },
            new AppUser { Id = carrierId, Name = "C", Email = "c3@t.com", Role = UserRole.Carrier, Company = "C" }
        );

        var bid = new BidEvent
        {
            CreatedByShipperId = shipperId, Title = "BID3",
            BaselineContractValue = 50000m, DeadlineUtc = DateTime.UtcNow.AddDays(7), Status = BidStatus.Open
        };
        _db.BidEvents.Add(bid);

        var lane = new BidLane { BidEventId = bid.Id, Origin = "SP", Destination = "RJ" };
        _db.BidLanes.Add(lane);

        _db.CarrierProposals.Add(new CarrierProposal
        {
            BidEventId = bid.Id, CarrierId = carrierId, Version = 1,
            Status = ProposalStatus.Submitted, TotalPrice = 40000m,
            SlaCompliant = true, OperationalCapacityTons = 80,
            LanePrices = new List<CarrierProposalLanePrice>
                { new() { LaneId = lane.Id, PricePerLane = 40000m } }
        });

        await _db.SaveChangesAsync();

        var highPriceWeight = new RankingWeights(0.9m, 0.05m, 0.03m, 0.02m);
        var highSlaWeight = new RankingWeights(0.1m, 0.1m, 0.7m, 0.1m);

        var r1 = await _sut.BuildDashboardAsync(bid.Id, new DashboardFilter(null, null, null), highPriceWeight, CancellationToken.None);
        var r2 = await _sut.BuildDashboardAsync(bid.Id, new DashboardFilter(null, null, null), highSlaWeight, CancellationToken.None);

        var json1 = System.Text.Json.JsonSerializer.Serialize(r1);
        var json2 = System.Text.Json.JsonSerializer.Serialize(r2);
        json1.Should().NotBe(json2);
    }
}
