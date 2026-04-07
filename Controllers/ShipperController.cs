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
    IExportService exportService,
    IRouteEngineService routeEngineService,
    ITollEstimator tollEstimator) : ControllerBase
{
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

        var nameExists = await db.BidTemplates
            .AnyAsync(x => x.ShipperId == shipperId && x.Name == request.Name.Trim(), ct);
        if (nameExists)
        {
            return BadRequest($"Já existe um template com o nome '{request.Name.Trim()}'. Use outro nome ou edite o template existente.");
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

    [HttpPost("bids")]
    public async Task<ActionResult<object>> CreateBid(CreateBidRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        if (request.Lanes == null || !request.Lanes.Any())
            return BadRequest("Adicione pelo menos uma rota/lane ao BID.");

        ShipperFacility? originFacilityForToll = null;
        if (request.OriginFacilityId is { } ofid)
        {
            originFacilityForToll = await db.ShipperFacilities
                .AsNoTracking()
                .FirstOrDefaultAsync(f => f.Id == ofid && f.ShipperId == shipperId, ct);
        }

        var pointIds = request.Lanes
            .Select(l => l.DestinationDeliveryPointId)
            .Where(g => g is { } pid && pid != Guid.Empty)
            .Select(g => g!.Value)
            .Distinct()
            .ToList();

        var points = await db.ShipperDeliveryPoints
            .AsNoTracking()
            .Where(p => pointIds.Contains(p.Id) && p.ShipperId == shipperId && p.IsActive)
            .ToDictionaryAsync(p => p.Id, ct);

        var lanes = new List<BidLane>();
        foreach (var l in request.Lanes)
        {
            var origin = l.Origin?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(origin))
                continue;

            if (l.DestinationDeliveryPointId is not { } dpId || dpId == Guid.Empty)
                return BadRequest("Cada rota deve ter um ponto de entrega (destino) selecionado no cadastro.");

            if (!points.TryGetValue(dpId, out var point))
                return BadRequest("Ponto de entrega inválido, inativo ou de outro embarcador.");

            var destSnapshot = $"{point.Name} ({point.City}/{point.State})";
            // Região: valor cadastrado no ponto ou macro-região derivada da UF (BrazilMacroRegion).
            var region = BrazilMacroRegion.EffectiveRegion(point.Region, point.State);

            var toll = await tollEstimator.EstimateTollAsync(
                originFacilityForToll?.Latitude, originFacilityForToll?.Longitude,
                point.Latitude, point.Longitude, ct);

            lanes.Add(new BidLane
            {
                Origin = origin,
                Destination = destSnapshot,
                DestinationDeliveryPointId = dpId,
                FreightType = l.FreightType?.Trim() ?? string.Empty,
                VolumeForecast = l.VolumeForecast,
                SlaRequirements = l.SlaRequirements?.Trim() ?? string.Empty,
                VehicleType = l.VehicleType?.Trim() ?? string.Empty,
                InsuranceRequirements = l.InsuranceRequirements?.Trim() ?? string.Empty,
                PaymentTerms = l.PaymentTerms?.Trim() ?? string.Empty,
                Region = region,
                EstimatedToll = toll
            });
        }

        if (!lanes.Any())
            return BadRequest("Nenhuma rota válida (origem e ponto de entrega são obrigatórios).");

        var bid = new BidEvent
        {
            CreatedByShipperId = shipperId,
            Title = string.IsNullOrWhiteSpace(request.Title) ? $"BID {DateTime.UtcNow:yyyyMMdd_HHmm}" : request.Title.Trim(),
            AuctionType = request.AuctionType,
            DeadlineUtc = request.DeadlineUtc ?? DateTime.UtcNow.AddDays(7),
            RequiredDocumentation = request.RequiredDocumentation?.Trim() ?? string.Empty,
            BaselineContractValue = request.BaselineContractValue ?? 0m,
            Status = BidStatus.Open,
            Lanes = lanes
        };

        db.BidEvents.Add(bid);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(shipperId, "CREATE_BID", nameof(BidEvent), bid.Id.ToString(),
            $"BID '{bid.Title}' criado com {lanes.Count} rotas.", ct);
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

        await routeEngineService.GeocodeAndCacheFacilityAsync(facility, ct);
        if (facility.Latitude is not null)
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
        facility.IsActive = request.IsActive;

        // If lat/lon provided by user, use them directly; otherwise geocode from ZipCode
        if (request.Latitude is not null && request.Longitude is not null)
        {
            facility.Latitude  = request.Latitude;
            facility.Longitude = request.Longitude;
        }
        else
        {
            facility.Latitude  = null;
            facility.Longitude = null;
            await routeEngineService.GeocodeAndCacheFacilityAsync(facility, ct);
        }

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

    /* ---------- Pontos de entrega (clientes / destinos) ---------- */

    [HttpGet("delivery-points")]
    public async Task<ActionResult<object>> GetDeliveryPoints(CancellationToken ct)
    {
        var shipperId = GetUserId();
        var list = await db.ShipperDeliveryPoints
            .AsNoTracking()
            .Where(x => x.ShipperId == shipperId)
            .OrderBy(x => x.Name)
            .Select(x => new
            {
                x.Id,
                x.Name,
                x.Address,
                x.City,
                x.State,
                x.ZipCode,
                x.Region,
                x.Country,
                x.Latitude,
                x.Longitude,
                x.IsActive
            })
            .ToListAsync(ct);
        return Ok(list);
    }

    [HttpPost("delivery-points")]
    public async Task<ActionResult<object>> CreateDeliveryPoint(SaveDeliveryPointRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        if (string.IsNullOrWhiteSpace(request.Name))
            return BadRequest("Informe o nome do ponto de entrega.");
        if (string.IsNullOrWhiteSpace(request.City) || string.IsNullOrWhiteSpace(request.State))
            return BadRequest("Preencha cidade e UF.");

        var point = new ShipperDeliveryPoint
        {
            ShipperId = shipperId,
            Name = request.Name.Trim(),
            Address = (request.Address ?? string.Empty).Trim(),
            City = request.City.Trim(),
            State = request.State.Trim().ToUpperInvariant(),
            ZipCode = (request.ZipCode ?? string.Empty).Trim(),
            Region = request.Region?.Trim() ?? string.Empty,
            Country = request.Country?.Trim() ?? "Brasil",
            Latitude = request.Latitude,
            Longitude = request.Longitude,
            IsActive = request.IsActive
        };

        db.ShipperDeliveryPoints.Add(point);
        await db.SaveChangesAsync(ct);

        if (request.Latitude is null || request.Longitude is null)
        {
            await routeEngineService.GeocodeAndCacheDeliveryPointAsync(point, ct);
            if (point.Latitude is not null)
                await db.SaveChangesAsync(ct);
        }

        await auditService.LogAsync(shipperId, "CREATE_DELIVERY_POINT", nameof(ShipperDeliveryPoint), point.Id.ToString(),
            $"Ponto de entrega '{point.Name}' criado.", ct);
        return Ok(new { point.Id, point.Name });
    }

    [HttpPut("delivery-points/{pointId:guid}")]
    public async Task<ActionResult> UpdateDeliveryPoint(Guid pointId, SaveDeliveryPointRequest request, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var point = await db.ShipperDeliveryPoints
            .FirstOrDefaultAsync(x => x.Id == pointId && x.ShipperId == shipperId, ct);
        if (point is null) return NotFound("Ponto de entrega não encontrado.");

        point.Name = request.Name.Trim();
        point.Address = (request.Address ?? string.Empty).Trim();
        point.City = request.City.Trim();
        point.State = request.State.Trim().ToUpperInvariant();
        point.ZipCode = (request.ZipCode ?? string.Empty).Trim();
        point.Region = request.Region?.Trim() ?? string.Empty;
        point.Country = request.Country?.Trim() ?? "Brasil";
        point.IsActive = request.IsActive;

        if (request.Latitude is not null && request.Longitude is not null)
        {
            point.Latitude = request.Latitude;
            point.Longitude = request.Longitude;
        }
        else
        {
            point.Latitude = null;
            point.Longitude = null;
            await routeEngineService.GeocodeAndCacheDeliveryPointAsync(point, ct);
        }

        await db.SaveChangesAsync(ct);
        return Ok();
    }

    [HttpDelete("delivery-points/{pointId:guid}")]
    public async Task<ActionResult> DeleteDeliveryPoint(Guid pointId, CancellationToken ct)
    {
        var shipperId = GetUserId();
        var point = await db.ShipperDeliveryPoints
            .FirstOrDefaultAsync(x => x.Id == pointId && x.ShipperId == shipperId, ct);
        if (point is null) return NotFound("Ponto de entrega não encontrado.");

        var inUse = await db.BidLanes.AnyAsync(l => l.DestinationDeliveryPointId == pointId, ct);
        if (inUse)
            return BadRequest("Este ponto está vinculado a rotas de BIDs existentes e não pode ser removido.");

        db.ShipperDeliveryPoints.Remove(point);
        await db.SaveChangesAsync(ct);
        await auditService.LogAsync(shipperId, "DELETE_DELIVERY_POINT", nameof(ShipperDeliveryPoint), pointId.ToString(),
            $"Ponto '{point.Name}' removido.", ct);
        return Ok();
    }

    /* ---------- Route Engine ---------- */

    [HttpGet("route-engine/suggestions")]
    public async Task<ActionResult<List<RouteSuggestion>>> GetRouteSuggestions(
        [FromQuery] Guid originFacilityId,
        CancellationToken ct)
    {
        var shipperId = GetUserId();

        var facilityExists = await db.ShipperFacilities
            .AnyAsync(x => x.Id == originFacilityId && x.ShipperId == shipperId && x.IsActive, ct);

        if (!facilityExists)
            return NotFound("CD de origem não encontrado ou inativo.");

        var deliveryCount = await db.ShipperDeliveryPoints
            .CountAsync(x => x.ShipperId == shipperId && x.IsActive, ct);

        if (deliveryCount == 0)
            return BadRequest("Cadastre pelo menos um ponto de entrega ativo para gerar rotas a partir do CD de origem.");

        var suggestions = await routeEngineService.GenerateSuggestionsAsync(shipperId, originFacilityId, ct);
        return Ok(suggestions);
    }

    private Guid GetUserId()
    {
        var raw = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue("sub");
        return Guid.Parse(raw!);
    }
}
