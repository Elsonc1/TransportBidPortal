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
[Authorize(Roles = nameof(UserRole.Admin))]
public class AdminController(AppDbContext db, IExcelImportService excelImportService) : ControllerBase
{
    [HttpGet("shippers")]
    public async Task<ActionResult<object>> Shippers(CancellationToken ct)
    {
        var shippers = await db.Users
            .Where(x => x.Role == UserRole.Shipper)
            .Select(x => new { x.Id, x.Name, x.Company, x.Email })
            .OrderBy(x => x.Name)
            .ToListAsync(ct);
        return Ok(shippers);
    }

    [HttpGet("mapping-profiles")]
    public async Task<ActionResult<object>> MappingProfiles([FromQuery] Guid shipperId, CancellationToken ct)
    {
        var profiles = await db.ExcelMappingProfiles
            .Include(x => x.Rules)
            .Where(x => x.ShipperId == shipperId)
            .OrderByDescending(x => x.IsActive)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.ShipperId,
                x.Name,
                x.IsActive,
                Rules = x.Rules.OrderBy(r => r.SortOrder).Select(r => new
                {
                    r.CanonicalField,
                    r.DisplayName,
                    r.Aliases,
                    r.IsRequired,
                    r.DataType,
                    r.SortOrder
                })
            })
            .ToListAsync(ct);
        return Ok(profiles);
    }

    [HttpPost("mapping-profiles")]
    public async Task<ActionResult<object>> CreateMappingProfile(SaveMappingProfileRequest request, CancellationToken ct)
    {
        if (!request.Rules.Any())
        {
            return BadRequest("At least one mapping rule is required.");
        }

        var profile = new ExcelMappingProfile
        {
            ShipperId = request.ShipperId,
            Name = request.Name.Trim(),
            IsActive = request.IsActive,
            Rules = request.Rules
                .OrderBy(r => r.SortOrder)
                .Select(r => new ExcelMappingRule
                {
                    CanonicalField = r.CanonicalField.Trim(),
                    DisplayName = r.DisplayName.Trim(),
                    Aliases = r.Aliases.Trim(),
                    IsRequired = r.IsRequired,
                    DataType = r.DataType.Trim(),
                    SortOrder = r.SortOrder
                }).ToList()
        };

        db.ExcelMappingProfiles.Add(profile);
        await db.SaveChangesAsync(ct);
        return Ok(new { profile.Id, profile.Name });
    }

    [HttpPut("mapping-profiles/{profileId:guid}")]
    public async Task<ActionResult> UpdateMappingProfile(Guid profileId, SaveMappingProfileRequest request, CancellationToken ct)
    {
        var profile = await db.ExcelMappingProfiles
            .Include(x => x.Rules)
            .FirstOrDefaultAsync(x => x.Id == profileId, ct);
        if (profile is null) return NotFound("Mapping profile not found.");

        profile.Name = request.Name.Trim();
        profile.IsActive = request.IsActive;
        profile.UpdatedAtUtc = DateTime.UtcNow;

        db.ExcelMappingRules.RemoveRange(profile.Rules);
        profile.Rules = request.Rules
            .OrderBy(r => r.SortOrder)
            .Select(r => new ExcelMappingRule
            {
                ExcelMappingProfileId = profileId,
                CanonicalField = r.CanonicalField.Trim(),
                DisplayName = r.DisplayName.Trim(),
                Aliases = r.Aliases.Trim(),
                IsRequired = r.IsRequired,
                DataType = r.DataType.Trim(),
                SortOrder = r.SortOrder
            }).ToList();

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("mapping-profiles/{profileId:guid}/analyze-excel")]
    public async Task<ActionResult<object>> AnalyzeWithProfile(Guid profileId, [FromForm] IFormFile excelFile, CancellationToken ct)
    {
        var profile = await db.ExcelMappingProfiles
            .Include(x => x.Rules)
            .FirstOrDefaultAsync(x => x.Id == profileId, ct);
        if (profile is null) return NotFound("Mapping profile not found.");

        var fields = profile.Rules
            .OrderBy(x => x.SortOrder)
            .Select(x => new TemplateFieldDefinition(x.CanonicalField, x.DisplayName, x.Aliases, x.IsRequired, x.DataType, x.SortOrder))
            .ToList();

        var matches = excelImportService.MatchTemplate(excelFile, fields);
        var missingRequired = matches.Where(x => x.IsRequired && string.IsNullOrWhiteSpace(x.MatchedHeader)).Select(x => x.DisplayName).ToList();
        return Ok(new { Profile = profile.Name, Matches = matches, MissingRequired = missingRequired });
    }
}
