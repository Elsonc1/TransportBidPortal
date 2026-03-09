using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Contracts;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = nameof(UserRole.Carrier))]
public class CarrierController(
    AppDbContext db,
    IFileStorageService fileStorageService,
    IAuditService auditService) : ControllerBase
{
    [HttpGet("invited-bids")]
    public async Task<ActionResult<object>> InvitedBids(CancellationToken ct)
    {
        var carrierId = GetUserId();
        var bids = await db.BidInvitations
            .Where(x => x.CarrierId == carrierId)
            .Select(x => new
            {
                x.BidEventId,
                x.BidEvent!.Title,
                x.BidEvent.DeadlineUtc,
                AuctionType = x.BidEvent.AuctionType.ToString(),
                x.BidEvent.RequiredDocumentation,
                LaneCount = x.BidEvent.Lanes.Count,
                x.InviteToken
            })
            .ToListAsync(ct);

        return Ok(bids);
    }

    [HttpGet("bids/{bidId:guid}")]
    public async Task<ActionResult<object>> BidDetails(Guid bidId, CancellationToken ct)
    {
        var lanes = await db.BidLanes.Where(x => x.BidEventId == bidId).Select(x => new
        {
            x.Id,
            x.Origin,
            x.Destination,
            x.FreightType,
            x.VolumeForecast,
            x.SlaRequirements,
            x.VehicleType,
            x.Region
        }).ToListAsync(ct);

        return Ok(lanes);
    }

    [HttpPost("proposals/save-draft")]
    public async Task<ActionResult<object>> SaveDraft([FromBody] SaveProposalRequest request, CancellationToken ct)
    {
        var proposal = await SaveProposalInternal(request, ProposalStatus.Draft, ct);
        return Ok(new { proposal.Id, proposal.Version, proposal.TotalPrice, proposal.Status });
    }

    [HttpPost("proposals/submit")]
    public async Task<ActionResult<object>> Submit([FromBody] SaveProposalRequest request, CancellationToken ct)
    {
        var proposal = await SaveProposalInternal(request, ProposalStatus.Submitted, ct);
        return Ok(new { proposal.Id, proposal.Version, proposal.TotalPrice, proposal.Status });
    }

    [HttpPost("proposals/{proposalId:guid}/documents")]
    public async Task<ActionResult> UploadProposalDocument(Guid proposalId, [FromForm] IFormFile document, CancellationToken ct)
    {
        var proposal = await db.CarrierProposals.FirstOrDefaultAsync(x => x.Id == proposalId, ct);
        if (proposal is null) return NotFound("Proposal not found.");

        var path = await fileStorageService.SaveAsync(document, ct);
        db.CarrierProposalDocuments.Add(new CarrierProposalDocument
        {
            ProposalId = proposalId,
            OriginalFileName = document.FileName,
            StoredPath = path
        });
        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpGet("bids/{bidId:guid}/proposal-versions")]
    public async Task<ActionResult<object>> ProposalVersions(Guid bidId, CancellationToken ct)
    {
        var carrierId = GetUserId();
        var versions = await db.CarrierProposals
            .Where(x => x.BidEventId == bidId && x.CarrierId == carrierId)
            .OrderByDescending(x => x.Version)
            .Select(x => new { x.Id, x.Version, x.Status, x.TotalPrice, x.SlaCompliant, x.SavedAtUtc, x.SubmittedAtUtc })
            .ToListAsync(ct);
        return Ok(versions);
    }

    private async Task<CarrierProposal> SaveProposalInternal(SaveProposalRequest request, ProposalStatus status, CancellationToken ct)
    {
        if (!request.LanePrices.Any())
        {
            throw new InvalidOperationException("Proposal must include lane prices.");
        }

        var carrierId = GetUserId();
        var lastVersion = await db.CarrierProposals
            .Where(x => x.BidEventId == request.BidId && x.CarrierId == carrierId)
            .OrderByDescending(x => x.Version)
            .Select(x => x.Version)
            .FirstOrDefaultAsync(ct);

        var proposal = new CarrierProposal
        {
            BidEventId = request.BidId,
            CarrierId = carrierId,
            Version = lastVersion + 1,
            Status = status,
            OperationalCapacityTons = request.OperationalCapacityTons,
            SlaCompliant = request.SlaCompliant,
            TotalPrice = request.LanePrices.Sum(x => x.PricePerLane),
            SavedAtUtc = DateTime.UtcNow,
            SubmittedAtUtc = status == ProposalStatus.Submitted ? DateTime.UtcNow : null,
            LanePrices = request.LanePrices.Select(x => new CarrierProposalLanePrice
            {
                LaneId = x.LaneId,
                PricePerLane = x.PricePerLane
            }).ToList()
        };

        db.CarrierProposals.Add(proposal);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(carrierId, "SAVE_PROPOSAL", nameof(CarrierProposal), proposal.Id.ToString(), $"Status: {status}, version: {proposal.Version}", ct);
        return proposal;
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        return Guid.Parse(raw!);
    }
}
