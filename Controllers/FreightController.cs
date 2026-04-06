using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TransportBidPortal.Data;
using TransportBidPortal.Domain;
using TransportBidPortal.Services;
using Microsoft.EntityFrameworkCore;

namespace TransportBidPortal.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class FreightController(AppDbContext db, IFreightService freightService) : ControllerBase
{
    [HttpGet("estimate")]
    public async Task<ActionResult<FreightEstimate>> Estimate(
        [FromQuery] string originCep,
        [FromQuery] string destinationCep,
        [FromQuery] string? vehicleType,
        CancellationToken ct)
    {
        var result = await freightService.CalculateAsync(originCep, destinationCep, vehicleType, ct);
        if (result is null) return BadRequest("Não foi possível calcular o frete. Verifique os CEPs.");
        return Ok(result);
    }

    [HttpGet("rates")]
    public async Task<ActionResult<object>> GetRates(CancellationToken ct)
    {
        var rates = await db.FreightRates
            .Where(x => x.IsActive)
            .OrderBy(x => x.VehicleType)
            .Select(x => new { x.Id, x.VehicleType, x.RatePerKm, x.Description })
            .ToListAsync(ct);
        return Ok(rates);
    }

    [HttpPost("rates")]
    [Authorize(Roles = "Admin,Shipper")]
    public async Task<ActionResult<object>> SaveRate([FromBody] SaveFreightRateRequest request, CancellationToken ct)
    {
        var rate = new FreightRate
        {
            VehicleType = request.VehicleType.Trim(),
            RatePerKm = request.RatePerKm,
            Description = request.Description?.Trim() ?? string.Empty,
            IsActive = true
        };
        db.FreightRates.Add(rate);
        await db.SaveChangesAsync(ct);
        return Ok(new { rate.Id, rate.VehicleType });
    }
}

public record SaveFreightRateRequest(string VehicleType, decimal RatePerKm, string? Description);
