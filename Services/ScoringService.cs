using Microsoft.EntityFrameworkCore;
using TransportBidPortal.Contracts;
using TransportBidPortal.Data;

namespace TransportBidPortal.Services;

public interface IScoringService
{
    Task<object> BuildDashboardAsync(Guid bidId, DashboardFilter filter, RankingWeights? weights, CancellationToken ct);
}

public class ScoringService(AppDbContext db) : IScoringService
{
    public async Task<object> BuildDashboardAsync(Guid bidId, DashboardFilter filter, RankingWeights? weights, CancellationToken ct)
    {
        var cfg = weights ?? new RankingWeights(0.45m, 0.2m, 0.25m, 0.1m);
        var bid = await db.BidEvents.FirstOrDefaultAsync(x => x.Id == bidId, ct);
        if (bid is null)
        {
            return new { Ranking = Array.Empty<object>(), Heatmap = Array.Empty<object>(), Kpis = new { TotalProjectedSavings = 0m, CostVsPreviousContract = 0m, CarrierPerformanceIndex = 0m } };
        }

        var lanes = await db.BidLanes.Where(x => x.BidEventId == bidId).ToListAsync(ct);
        var proposals = await db.CarrierProposals
            .Include(x => x.Carrier)
            .Include(x => x.LanePrices)
            .Where(x => x.BidEventId == bidId && x.Status == Domain.ProposalStatus.Submitted)
            .OrderByDescending(x => x.Version)
            .ToListAsync(ct);

        if (!string.IsNullOrWhiteSpace(filter.Region))
        {
            var laneIds = lanes.Where(x => x.Region == filter.Region).Select(x => x.Id).ToHashSet();
            proposals = proposals.Where(p => p.LanePrices.Any(lp => laneIds.Contains(lp.LaneId))).ToList();
        }

        if (filter.CarrierId.HasValue)
        {
            proposals = proposals.Where(x => x.CarrierId == filter.CarrierId).ToList();
        }

        var minTotal = proposals.Any() ? proposals.Min(x => x.TotalPrice) : 0m;
        var avgTotal = proposals.Any() ? proposals.Average(x => x.TotalPrice) : 0m;

        var qualificationMap = await db.CarrierQualifications
            .ToDictionaryAsync(x => x.CarrierId, x => x.QualificationScore, ct);

        var ranking = proposals.Select(p =>
        {
            var priceScore = minTotal > 0 ? (minTotal / Math.Max(p.TotalPrice, 1m)) * 100m : 0m;
            var benefitScore = p.SlaCompliant ? 100m : 40m;
            var slaScore = p.SlaCompliant ? 100m : 0m;
            var qScore = qualificationMap.TryGetValue(p.CarrierId, out var q) ? q : 50m;
            var weighted = (priceScore * cfg.PriceWeight) +
                           (benefitScore * cfg.CostBenefitWeight) +
                           (slaScore * cfg.SlaWeight) +
                           (qScore * cfg.QualificationWeight);

            return new
            {
                p.CarrierId,
                CarrierName = p.Carrier!.Company,
                p.TotalPrice,
                PriceDeviation = avgTotal == 0 ? 0 : ((p.TotalPrice - avgTotal) / avgTotal) * 100m,
                Sla = p.SlaCompliant,
                Score = Math.Round(weighted, 2),
                SavingsVsBaseline = bid.BaselineContractValue - p.TotalPrice
            };
        })
        .OrderByDescending(x => x.Score)
        .ToList();

        var heatmap = lanes
            .GroupBy(x => string.IsNullOrWhiteSpace(x.Region) ? "N/A" : x.Region)
            .Select(g => new
            {
                Region = g.Key,
                LaneCount = g.Count(),
                AvgVolume = g.Average(x => x.VolumeForecast)
            })
            .ToList();

        var top = ranking.FirstOrDefault();
        var kpis = new
        {
            TotalProjectedSavings = ranking.Sum(x => Math.Max(x.SavingsVsBaseline, 0m)),
            CostVsPreviousContract = top is null ? 0m : top.TotalPrice,
            CarrierPerformanceIndex = top?.Score ?? 0m
        };

        return new
        {
            Ranking = ranking,
            Heatmap = heatmap,
            Kpis = kpis
        };
    }
}
