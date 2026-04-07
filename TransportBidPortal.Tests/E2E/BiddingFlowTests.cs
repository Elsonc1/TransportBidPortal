using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using TransportBidPortal.Contracts;
using TransportBidPortal.Domain;
using TransportBidPortal.Tests.Helpers;

namespace TransportBidPortal.Tests.E2E;

public class BiddingFlowTests : IClassFixture<TestWebApplicationFactory>, IDisposable
{
    private readonly TestWebApplicationFactory _factory;
    private readonly HttpClient _shipperClient;
    private readonly HttpClient _carrierClient;
    private readonly Guid _shipperId;
    private readonly Guid _carrierId;

    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNameCaseInsensitive = true };

    public BiddingFlowTests(TestWebApplicationFactory factory)
    {
        _factory = factory;
        (_shipperId, _carrierId) = SeedUsers();

        _shipperClient = factory.CreateClient();
        _shipperClient.SetAuth(_shipperId, "Shipper", UserRole.Shipper);

        _carrierClient = factory.CreateClient();
        _carrierClient.SetAuth(_carrierId, "Carrier", UserRole.Carrier);
    }

    public void Dispose()
    {
        _shipperClient.Dispose();
        _carrierClient.Dispose();
    }

    [Fact]
    public async Task FullFlow_CreateTemplate_CreateBid_InviteCarrier_SubmitProposal()
    {
        // 1. Shipper creates a template
        var templateReq = new CreateBidTemplateRequest(
            $"E2E_Template_{Guid.NewGuid():N}", false,
            new List<TemplateColumnInput>
            {
                new("origin", "Origem", "Origin", true, "text", 1),
                new("destination", "Destino", "Destination", true, "text", 2),
                new("volume", "Volume", "VolumeForecast", false, "number", 3)
            });

        var templateRes = await _shipperClient.PostAsJsonAsync("/api/shipper/templates", templateReq);
        templateRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var templateBody = await templateRes.Content.ReadFromJsonAsync<JsonElement>();
        var templateId = templateBody.GetProperty("id").GetGuid();
        templateId.Should().NotBeEmpty();

        // 2. Shipper creates a BID with lanes
        var bidReq = new CreateBidRequest(
            "E2E BID", AuctionType.Open, DateTime.UtcNow.AddDays(14),
            "ANTT, Seguro", 200000m, null, templateId,
            new List<BidLaneInput>
            {
                new("Sao Paulo", "Curitiba", TestSeedIds.DeliveryPointCuritibaId, "CIF", 500, "48h", "Truck", "Obrigatorio", "30 dias", "Sul"),
                new("Sao Paulo", "Joinville", TestSeedIds.DeliveryPointJoinvilleId, "FOB", 300, "72h", "Carreta", null, null, "Sul")
            });

        var bidRes = await _shipperClient.PostAsJsonAsync("/api/shipper/bids", bidReq);
        bidRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var bidBody = await bidRes.Content.ReadFromJsonAsync<JsonElement>();
        var bidId = bidBody.GetProperty("id").GetGuid();
        bidBody.GetProperty("laneCount").GetInt32().Should().Be(2);

        // 3. Verify BID appears in shipper's list
        var bidsRes = await _shipperClient.GetAsync("/api/shipper/bids");
        var bids = await bidsRes.Content.ReadAsStringAsync();
        bids.Should().Contain("E2E BID");

        // 4. Shipper invites the carrier
        var inviteReq = new InviteCarriersRequest(new List<Guid> { _carrierId });
        var inviteRes = await _shipperClient.PostAsJsonAsync($"/api/shipper/bids/{bidId}/invite", inviteReq);
        inviteRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 5. Carrier sees the invitation
        var invitedRes = await _carrierClient.GetAsync("/api/carrier/invited-bids");
        invitedRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var invitedBody = await invitedRes.Content.ReadAsStringAsync();
        invitedBody.Should().Contain("E2E BID");

        // 6. Carrier views BID lanes
        var lanesRes = await _carrierClient.GetAsync($"/api/carrier/bids/{bidId}");
        lanesRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var lanesBody = await lanesRes.Content.ReadFromJsonAsync<JsonElement[]>();
        lanesBody.Should().HaveCount(2);
        var laneIds = lanesBody!.Select(l => l.GetProperty("id").GetGuid()).ToList();

        // 7. Carrier saves a draft proposal
        var draftReq = new SaveProposalRequest(
            bidId,
            laneIds.Select(id => new ProposalLanePriceInput(id, 50000m)).ToList(),
            150m, true);

        var draftRes = await _carrierClient.PostAsJsonAsync("/api/carrier/proposals/save-draft", draftReq);
        draftRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var draftBody = await draftRes.Content.ReadFromJsonAsync<JsonElement>();
        draftBody.GetProperty("version").GetInt32().Should().Be(1);

        // 8. Carrier submits final proposal (v2)
        var submitReq = new SaveProposalRequest(
            bidId,
            laneIds.Select(id => new ProposalLanePriceInput(id, 45000m)).ToList(),
            150m, true);

        var submitRes = await _carrierClient.PostAsJsonAsync("/api/carrier/proposals/submit", submitReq);
        submitRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var submitBody = await submitRes.Content.ReadFromJsonAsync<JsonElement>();
        submitBody.GetProperty("version").GetInt32().Should().Be(2);

        // 9. Verify proposal versions
        var versionsRes = await _carrierClient.GetAsync($"/api/carrier/bids/{bidId}/proposal-versions");
        versionsRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var versions = await versionsRes.Content.ReadFromJsonAsync<JsonElement[]>();
        versions.Should().HaveCount(2);
        versions![0].GetProperty("version").GetInt32().Should().Be(2);

        // 10. Shipper views dashboard
        var dashRes = await _shipperClient.GetAsync($"/api/shipper/dashboard?bidId={bidId}");
        dashRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var dash = await dashRes.Content.ReadAsStringAsync();
        dash.Should().Contain("Ranking");
    }

    [Fact]
    public async Task FacilityFlow_CreateMatriz_ThenFilial()
    {
        // 1. Create Matriz
        var matrizReq = new SaveFacilityRequest(
            "CD Principal", "Matriz", "11222333000101", "Av Brasil, 1000",
            "Sao Paulo", "SP", "01001000", "Brasil", null, null, true);
        var matrizRes = await _shipperClient.PostAsJsonAsync("/api/shipper/facilities", matrizReq);
        matrizRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Create Filial (same CNPJ root)
        var filialReq = new SaveFacilityRequest(
            "Filial Curitiba", "Filial", "11222333000202", "Rua XV, 500",
            "Curitiba", "PR", "80010000", "Brasil", null, null, true);
        var filialRes = await _shipperClient.PostAsJsonAsync("/api/shipper/facilities", filialReq);
        filialRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Verify both appear
        var listRes = await _shipperClient.GetAsync("/api/shipper/facilities");
        var body = await listRes.Content.ReadAsStringAsync();
        body.Should().Contain("CD Principal").And.Contain("Filial Curitiba");
    }

    [Fact]
    public async Task FacilityFlow_FilialBeforeMatriz_Fails()
    {
        var filialReq = new SaveFacilityRequest(
            "Filial Orphan", "Filial", "55667788000200", "Rua X",
            "RJ", "RJ", "20040020", "Brasil", null, null, true);
        var res = await _shipperClient.PostAsJsonAsync("/api/shipper/facilities", filialReq);
        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private (Guid shipperId, Guid carrierId) SeedUsers()
    {
        using (var db = _factory.CreateDbContext())
        {
            return (TestSeedIds.DemoShipperId, TestSeedIds.DemoCarrierAId);
        }
    }
}
