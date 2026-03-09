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
[Authorize(Roles = nameof(UserRole.Shipper))]
public class ShipperController(
    AppDbContext db,
    IExcelImportService excelImportService,
    INotificationService notificationService,
    IAuditService auditService,
    IScoringService scoringService,
    IExportService exportService) : ControllerBase
{
    [HttpPost("excel/analyze")]
    public ActionResult<ExcelAnalysisResult> AnalyzeExcel([FromForm] IFormFile excelFile)
    {
        var result = excelImportService.AnalyzeColumns(excelFile);
        return Ok(result);
    }

    [HttpGet("templates")]
    public async Task<ActionResult<object>> GetTemplates(CancellationToken ct)
    {
        var shipperId = GetUserId();
        var templates = await db.BidTemplates
            .Include(x => x.Columns)
            .Where(x => x.ShipperId == shipperId)
            .OrderByDescending(x => x.IsDefault)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.IsDefault,
                Columns = x.Columns.OrderBy(c => c.SortOrder).Select(c => new
                {
                    c.Key,
                    c.DisplayName,
                    c.Aliases,
                    c.IsRequired,
                    c.DataType,
                    c.SortOrder
                })
            })
            .ToListAsync(ct);
        return Ok(templates);
    }

    [HttpGet("mapping-profiles")]
    public async Task<ActionResult<object>> MappingProfiles(CancellationToken ct)
    {
        var shipperId = GetUserId();
        var profiles = await db.ExcelMappingProfiles
            .Include(x => x.Rules)
            .Where(x => x.ShipperId == shipperId && x.IsActive)
            .OrderBy(x => x.Name)
            .Select(x => new { x.Id, x.Name })
            .ToListAsync(ct);
        return Ok(profiles);
    }

    [HttpPost("templates")]
    public async Task<ActionResult<object>> CreateTemplate(CreateBidTemplateRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        if (!request.Columns.Any())
        {
            return BadRequest("Template must have at least one column.");
        }

        var template = new BidTemplate
        {
            ShipperId = shipperId,
            Name = request.Name.Trim(),
            IsDefault = request.IsDefault,
            Columns = request.Columns
                .OrderBy(x => x.SortOrder)
                .Select(x => new BidTemplateColumn
                {
                    Key = x.Key.Trim(),
                    DisplayName = x.DisplayName.Trim(),
                    Aliases = x.Aliases.Trim(),
                    IsRequired = x.IsRequired,
                    DataType = x.DataType.Trim(),
                    SortOrder = x.SortOrder
                }).ToList()
        };

        if (template.IsDefault)
        {
            var existingDefaults = await db.BidTemplates.Where(x => x.ShipperId == shipperId && x.IsDefault).ToListAsync(ct);
            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
            }
        }

        db.BidTemplates.Add(template);
        await db.SaveChangesAsync(ct);
        return Ok(new { template.Id, template.Name });
    }

    [HttpPut("templates/{templateId:guid}")]
    public async Task<ActionResult> UpdateTemplate(Guid templateId, UpdateBidTemplateRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        template.Name = request.Name.Trim();
        template.IsDefault = request.IsDefault;
        template.UpdatedAtUtc = DateTime.UtcNow;

        db.BidTemplateColumns.RemoveRange(template.Columns);
        template.Columns = request.Columns
            .OrderBy(x => x.SortOrder)
            .Select(x => new BidTemplateColumn
            {
                BidTemplateId = template.Id,
                Key = x.Key.Trim(),
                DisplayName = x.DisplayName.Trim(),
                Aliases = x.Aliases.Trim(),
                IsRequired = x.IsRequired,
                DataType = x.DataType.Trim(),
                SortOrder = x.SortOrder
            }).ToList();

        if (template.IsDefault)
        {
            var existingDefaults = await db.BidTemplates.Where(x => x.ShipperId == shipperId && x.Id != templateId && x.IsDefault).ToListAsync(ct);
            foreach (var d in existingDefaults)
            {
                d.IsDefault = false;
            }
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpPost("templates/{templateId:guid}/map-excel")]
    public async Task<ActionResult<object>> MapExcelToTemplate(Guid templateId, [FromForm] IFormFile excelFile, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var fields = template.Columns
            .OrderBy(x => x.SortOrder)
            .Select(x => new TemplateFieldDefinition(x.Key, x.DisplayName, x.Aliases, x.IsRequired, x.DataType, x.SortOrder))
            .ToList();

        var matches = excelImportService.MatchTemplate(excelFile, fields);
        var missingRequired = matches.Where(x => x.IsRequired && string.IsNullOrWhiteSpace(x.MatchedHeader)).Select(x => x.DisplayName).ToList();
        return Ok(new { Template = template.Name, Matches = matches, MissingRequired = missingRequired });
    }

    [HttpGet("templates/{templateId:guid}/export-excel")]
    public async Task<IActionResult> ExportTemplateExcel(Guid templateId, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var fields = template.Columns
            .OrderBy(x => x.SortOrder)
            .Select(x => new TemplateFieldDefinition(x.Key, x.DisplayName, x.Aliases, x.IsRequired, x.DataType, x.SortOrder))
            .ToList();
        var bytes = excelImportService.BuildTemplateWorkbook(fields);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"{template.Name}_template.xlsx");
    }

    [HttpGet("excel/analyze-by-path")]
    [AllowAnonymous]
    public ActionResult<ExcelAnalysisResult> AnalyzeExcelByPath([FromQuery] string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return BadRequest("Path is required.");
        }

        if (!System.IO.File.Exists(path))
        {
            return NotFound("Excel file not found.");
        }

        var ext = Path.GetExtension(path);
        if (!ext.Equals(".xlsx", StringComparison.OrdinalIgnoreCase) && !ext.Equals(".xls", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest("Only .xlsx/.xls files are supported.");
        }

        var result = excelImportService.AnalyzeColumns(path);
        return Ok(result);
    }

    [HttpPost("bids/from-excel")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<object>> CreateBidFromExcel(
        [FromForm] IFormFile excelFile,
        [FromForm] string? title,
        [FromForm] AuctionType auctionType,
        [FromForm] DateTime? deadlineUtc,
        [FromForm] string? requiredDocumentation,
        [FromForm] decimal? baselineContractValue,
        [FromForm] Guid? mappingProfileId,
        CancellationToken ct)
    {
        var shipperId = GetUserId();
        List<BidLane> lanes;
        if (mappingProfileId.HasValue)
        {
            var profile = await db.ExcelMappingProfiles
                .Include(x => x.Rules)
                .FirstOrDefaultAsync(x => x.Id == mappingProfileId && x.ShipperId == shipperId, ct);
            if (profile is null)
            {
                return BadRequest("Mapping profile not found.");
            }

            var fields = profile.Rules
                .OrderBy(x => x.SortOrder)
                .Select(x => new TemplateFieldDefinition(x.CanonicalField, x.DisplayName, x.Aliases, x.IsRequired, x.DataType, x.SortOrder))
                .ToList();
            lanes = excelImportService.ParseLanes(excelFile, fields);
        }
        else
        {
            lanes = excelImportService.ParseLanes(excelFile);
        }

        if (!lanes.Any())
        {
            var analysis = excelImportService.AnalyzeColumns(excelFile);
            return BadRequest(new
            {
                Error = "No valid lanes found in spreadsheet. Could not infer Origin/Destination rows.",
                DetectedColumns = analysis.Matches,
                UnmappedHeaders = analysis.UnmappedHeaders
            });
        }

        var bid = new BidEvent
        {
            CreatedByShipperId = shipperId,
            Title = string.IsNullOrWhiteSpace(title) ? $"BID {DateTime.UtcNow:yyyyMMdd_HHmm}" : title,
            AuctionType = auctionType,
            DeadlineUtc = deadlineUtc ?? DateTime.UtcNow.AddDays(7),
            RequiredDocumentation = requiredDocumentation ?? string.Empty,
            BaselineContractValue = baselineContractValue ?? 0m,
            Status = BidStatus.Open,
            Lanes = lanes
        };

        db.BidEvents.Add(bid);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(shipperId, "CREATE_BID", nameof(BidEvent), bid.Id.ToString(), $"Bid with {lanes.Count} lanes created via Excel import.", ct);
        return Ok(new { bid.Id, bid.Title, LaneCount = lanes.Count });
    }

    [HttpPost("bids/{bidId:guid}/invite")]
    public async Task<ActionResult> InviteCarriers(Guid bidId, InviteCarriersRequest request, CancellationToken ct)
    {
        var bid = await db.BidEvents.FirstOrDefaultAsync(x => x.Id == bidId, ct);
        if (bid is null) return NotFound("BID not found.");

        foreach (var carrierId in request.CarrierIds.Distinct())
        {
            if (await db.BidInvitations.AnyAsync(x => x.BidEventId == bidId && x.CarrierId == carrierId, ct))
            {
                continue;
            }

            var invitation = new BidInvitation
            {
                BidEventId = bidId,
                CarrierId = carrierId,
                EmailSent = true
            };
            db.BidInvitations.Add(invitation);
            await notificationService.NotifyAsync(
                carrierId,
                $"New BID invitation: {bid.Title}",
                $"Access your portal link token: {invitation.InviteToken}",
                ct);
        }

        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(GetUserId(), "INVITE_CARRIERS", nameof(BidEvent), bidId.ToString(), $"Invited {request.CarrierIds.Count} carriers.", ct);
        return Ok();
    }

    [HttpGet("bids")]
    public async Task<ActionResult<object>> GetBids(CancellationToken ct)
    {
        var shipperId = GetUserId();
        var bids = await db.BidEvents
            .Where(x => x.CreatedByShipperId == shipperId)
            .Select(x => new
            {
                x.Id,
                x.Title,
                AuctionType = x.AuctionType.ToString(),
                x.DeadlineUtc,
                x.Status,
                Lanes = x.Lanes.Count,
                InvitedCarriers = x.Invitations.Count
            })
            .OrderByDescending(x => x.DeadlineUtc)
            .ToListAsync(ct);

        return Ok(bids);
    }

    [HttpGet("bids/{bidId:guid}/dashboard")]
    public async Task<ActionResult<object>> Dashboard(Guid bidId, [FromQuery] string? region, [FromQuery] Guid? carrierId, [FromQuery] string? vehicleType, CancellationToken ct)
    {
        var filter = new DashboardFilter(region, carrierId, vehicleType);
        var data = await scoringService.BuildDashboardAsync(bidId, filter, null, ct);
        return Ok(data);
    }

    [HttpGet("bids/{bidId:guid}/export/excel")]
    public async Task<IActionResult> ExportExcel(Guid bidId, CancellationToken ct)
    {
        var data = await scoringService.BuildDashboardAsync(bidId, new DashboardFilter(null, null, null), null, ct);
        var bytes = exportService.BuildExcelExport((dynamic)data);
        return File(bytes, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"bid_{bidId}_dashboard.xlsx");
    }

    [HttpGet("bids/{bidId:guid}/export/pdf")]
    public async Task<IActionResult> ExportPdf(Guid bidId, CancellationToken ct)
    {
        var data = await scoringService.BuildDashboardAsync(bidId, new DashboardFilter(null, null, null), null, ct);
        var bytes = exportService.BuildPdfExport((dynamic)data);
        return File(bytes, "application/pdf", $"bid_{bidId}_dashboard.pdf");
    }

    /* ---------- Excel Mapping Wizard (low-code) ---------- */

    [HttpPost("excel/extract-headers")]
    public ActionResult<ExtractHeadersResult> ExtractHeaders([FromForm] IFormFile excelFile)
    {
        var result = excelImportService.ExtractHeaders(excelFile);
        return Ok(result);
    }

    [HttpPost("excel/preview-raw")]
    [RequestSizeLimit(20_000_000)]
    public ActionResult<ExcelRawPreviewResult> PreviewRaw([FromForm] IFormFile excelFile, [FromForm] int maxRows = 30)
    {
        var result = excelImportService.PreviewRaw(excelFile, maxRows);
        return Ok(result);
    }

    [HttpPost("templates/{templateId:guid}/auto-map")]
    public async Task<ActionResult<object>> AutoMap(
        Guid templateId,
        [FromForm] IFormFile excelFile,
        CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var headersResult = excelImportService.ExtractHeaders(excelFile);
        var fields = template.Columns
            .OrderBy(x => x.SortOrder)
            .Select(x => new TemplateFieldDefinition(x.Key, x.DisplayName, x.Aliases, x.IsRequired, x.DataType, x.SortOrder))
            .ToList();

        var autoMappings = excelImportService.AutoMapHeaders(headersResult.Headers, fields);

        return Ok(new
        {
            headersResult.SheetName,
            headersResult.DetectedHeaderRow,
            headersResult.TotalRows,
            SourceHeaders = headersResult.Headers,
            TemplateFields = fields,
            SuggestedMappings = autoMappings
        });
    }

    [HttpPost("templates/{templateId:guid}/transform-preview")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<TransformPreviewResult>> TransformPreview(
        Guid templateId,
        [FromForm] IFormFile excelFile,
        [FromForm] string mappingsJson,
        [FromForm] int headerRow,
        CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMappingInput>>(
            mappingsJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? [];

        if (!mappings.Any())
            return BadRequest("No column mappings provided.");

        var result = excelImportService.TransformExcel(excelFile, headerRow, mappings, 50);
        return Ok(result);
    }

    [HttpPost("templates/{templateId:guid}/transform-all")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<TransformPreviewResult>> TransformAll(
        Guid templateId,
        [FromForm] IFormFile excelFile,
        [FromForm] string mappingsJson,
        [FromForm] int headerRow,
        CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMappingInput>>(
            mappingsJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? [];

        var result = excelImportService.TransformExcel(excelFile, headerRow, mappings);
        return Ok(result);
    }

    [HttpPost("templates/{templateId:guid}/export-transformed")]
    [RequestSizeLimit(20_000_000)]
    public async Task<IActionResult> ExportTransformed(
        Guid templateId,
        [FromForm] IFormFile excelFile,
        [FromForm] string mappingsJson,
        [FromForm] int headerRow,
        CancellationToken ct)
    {
        var shipperId = GetUserId();
        var template = await db.BidTemplates
            .Include(x => x.Columns)
            .FirstOrDefaultAsync(x => x.Id == templateId && x.ShipperId == shipperId, ct);
        if (template is null) return NotFound("Template not found.");

        var mappings = System.Text.Json.JsonSerializer.Deserialize<List<ColumnMappingInput>>(
            mappingsJson,
            new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? [];

        var result = excelImportService.TransformExcel(excelFile, headerRow, mappings);

        using var wb = new ClosedXML.Excel.XLWorkbook();
        var ws = wb.AddWorksheet("Dados_Transformados");
        for (var c = 0; c < result.Columns.Count; c++)
        {
            ws.Cell(1, c + 1).Value = result.Columns[c];
            ws.Cell(1, c + 1).Style.Font.Bold = true;
            ws.Cell(1, c + 1).Style.Fill.BackgroundColor = ClosedXML.Excel.XLColor.FromHtml("#0f2a56");
            ws.Cell(1, c + 1).Style.Font.FontColor = ClosedXML.Excel.XLColor.White;
        }

        for (var r = 0; r < result.Rows.Count; r++)
        {
            for (var c = 0; c < result.Columns.Count; c++)
            {
                var key = result.Columns[c];
                ws.Cell(r + 2, c + 1).Value = result.Rows[r].GetValueOrDefault(key, "");
            }
        }

        ws.Columns().AdjustToContents();
        using var ms = new MemoryStream();
        wb.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "dados_transformados.xlsx");
    }

    /* ---------- Facilities (Matriz / Filiais) ---------- */

    [HttpGet("facilities")]
    public async Task<ActionResult<object>> GetFacilities(CancellationToken ct)
    {
        var shipperId = GetUserId();
        var list = await db.ShipperFacilities
            .Where(x => x.ShipperId == shipperId)
            .OrderBy(x => x.Type)
            .ThenBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                Type = x.Type.ToString(),
                x.Name,
                x.Cnpj,
                x.Address,
                x.City,
                x.State,
                x.ZipCode,
                x.Country,
                x.Latitude,
                x.Longitude,
                x.IsActive
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("facilities")]
    public async Task<ActionResult<object>> CreateFacility(SaveFacilityRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var cnpjDigits = new string(request.Cnpj.Where(char.IsDigit).ToArray());
        if (cnpjDigits.Length < 14)
            return BadRequest("CNPJ deve ter 14 dígitos.");

        var cnpjBase = cnpjDigits[..8];
        var cnpjBranch = cnpjDigits[8..12];
        var facilityType = cnpjBranch == "0001" ? FacilityType.Matriz : FacilityType.Filial;

        var existing = await db.ShipperFacilities
            .Where(x => x.ShipperId == shipperId)
            .ToListAsync(ct);

        if (facilityType == FacilityType.Matriz)
        {
            var existingMatriz = existing.FirstOrDefault(f =>
                f.Type == FacilityType.Matriz &&
                new string(f.Cnpj.Where(char.IsDigit).ToArray()).StartsWith(cnpjBase));
            if (existingMatriz is not null)
                return BadRequest($"Já existe uma matriz com raiz {cnpjBase}: {existingMatriz.Name}.");
        }
        else
        {
            var hasMatriz = existing.Any(f =>
                f.Type == FacilityType.Matriz &&
                new string(f.Cnpj.Where(char.IsDigit).ToArray()).StartsWith(cnpjBase));
            if (!hasMatriz)
                return BadRequest($"Cadastre primeiro a matriz (sufixo /0001) com raiz {cnpjBase}.");
        }

        var facility = new ShipperFacility
        {
            ShipperId = shipperId,
            Type = facilityType,
            Name = request.Name.Trim(),
            Cnpj = request.Cnpj.Trim(),
            Address = request.Address.Trim(),
            City = request.City.Trim(),
            State = request.State.Trim().ToUpper(),
            ZipCode = request.ZipCode.Trim(),
            Country = request.Country?.Trim() ?? "Brasil",
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = request.IsActive
        };

        db.ShipperFacilities.Add(facility);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(shipperId, "CREATE_FACILITY", nameof(ShipperFacility), facility.Id.ToString(), $"Facility '{facility.Name}' ({facility.Type}) created.", ct);
        return Ok(new { facility.Id, facility.Name });
    }

    [HttpPut("facilities/{facilityId:guid}")]
    public async Task<ActionResult> UpdateFacility(Guid facilityId, SaveFacilityRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var facility = await db.ShipperFacilities
            .FirstOrDefaultAsync(x => x.Id == facilityId && x.ShipperId == shipperId, ct);
        if (facility is null) return NotFound("Unidade não encontrada.");

        if (!Enum.TryParse<FacilityType>(request.Type, true, out var facilityType))
            return BadRequest("Tipo inválido. Use Matriz ou Filial.");

        facility.Type = facilityType;
        facility.Name = request.Name.Trim();
        facility.Cnpj = request.Cnpj.Trim();
        facility.Address = request.Address.Trim();
        facility.City = request.City.Trim();
        facility.State = request.State.Trim().ToUpper();
        facility.ZipCode = request.ZipCode.Trim();
        facility.Country = request.Country?.Trim() ?? "Brasil";
        facility.Latitude = request.Latitude;
        facility.Longitude = request.Longitude;
        facility.IsActive = request.IsActive;

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("facilities/{facilityId:guid}")]
    public async Task<ActionResult> DeleteFacility(Guid facilityId, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var facility = await db.ShipperFacilities
            .FirstOrDefaultAsync(x => x.Id == facilityId && x.ShipperId == shipperId, ct);
        if (facility is null) return NotFound("Unidade não encontrada.");

        db.ShipperFacilities.Remove(facility);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(shipperId, "DELETE_FACILITY", nameof(ShipperFacility), facilityId.ToString(), $"Facility '{facility.Name}' removed.", ct);
        return Ok();
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("sub");
        return Guid.Parse(raw!);
    }
}
