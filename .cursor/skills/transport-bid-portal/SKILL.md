---
name: transport-bid-portal
description: >-
  Comprehensive project context for the Transport Bidding Portal (BID Portal).
  Use when working on any feature, bug fix, or enhancement in this codebase.
  Covers architecture, domain model, services, APIs, frontend patterns, and
  known pitfalls. Applies to tasks involving bidding, carriers, shippers,
  templates, facilities, freight, CEP, routing, dashboards, or logging.
---

# Transport Bidding Portal — Project Skill

## Purpose

Enterprise logistics platform where **Shippers** (embarcadores) create transport
BIDs, invite **Carriers** (transportadoras) to submit proposals, and analyze
results via dashboards. **Admins** manage mapping profiles and view structured logs.

## Stack

| Layer | Tech |
|-------|------|
| Backend | ASP.NET Core (.NET 10), C# 13, SQL Server LocalDB, EF Core |
| Frontend | Vanilla JS + HTML5 + CSS3 (single `index.html` SPA) |
| Auth | JWT Bearer + RBAC (Shipper / Carrier / Admin) |
| Excel | ClosedXML (template export, admin mapping profiles) |
| PDF | QuestPDF |
| Charts | Chart.js |
| Geocoding / Routing | OpenRouteService API (Geocode + Matrix + Directions) |
| CEP Lookup | ViaCEP API with in-memory + SQL Server cache |

## Project Structure

```
TransportBidPortal/
├── Controllers/
│   ├── AuthController.cs        # Login, Register
│   ├── ShipperController.cs     # Templates, BIDs, Facilities, Route Engine
│   ├── CarrierController.cs     # View BIDs, Submit proposals
│   ├── AdminController.cs       # Mapping profiles, admin ops
│   ├── SystemController.cs      # System-wide queries (carriers list)
│   ├── CepController.cs         # GET /api/cep/{cep}
│   ├── FreightController.cs     # Freight estimate + rate CRUD
│   └── LogController.cs         # Structured log viewer
├── Contracts/
│   └── Dtos.cs                  # All request/response records
├── Domain/
│   ├── Entities.cs              # Core EF entities
│   ├── Enums.cs                 # UserRole, AuctionType, BidStatus, etc.
│   ├── AppLog.cs                # Structured log entity
│   ├── CepCache.cs              # CEP cache entity
│   └── FreightRate.cs           # Freight rate per vehicle type
├── Data/
│   └── AppDbContext.cs          # EF context + seed data
├── Services/
│   ├── CepService.cs            # ICepService — memory + DB + ViaCEP
│   ├── FreightService.cs        # IFreightService — ORS geocode + directions
│   ├── RouteEngineService.cs    # IRouteEngineService — ORS Matrix bulk routing
│   ├── ExcelImportService.cs    # ClosedXML template builder + column matching
│   ├── ScoringService.cs        # Weighted ranking engine
│   ├── JwtTokenService.cs       # JWT generation
│   └── PlatformServices.cs      # Audit, Notification, FileStorage, Export
├── Middleware/
│   ├── CorrelationIdMiddleware.cs
│   └── RequestLoggingMiddleware.cs
├── wwwroot/
│   ├── index.html               # Full SPA markup
│   ├── app.js                   # All frontend logic (~1300 lines)
│   └── styles.css               # NDD-inspired enterprise styling
├── Program.cs                   # DI registration + middleware pipeline
└── appsettings.json             # Connection strings, JWT config, ORS key
```

## Domain Model (key entities)

- **AppUser** — `Id`, `Name`, `Email`, `PasswordHash`, `Role`, `Company`
- **BidEvent** — created by Shipper; has `Lanes`, `Invitations`, `Status` (Draft/Open/Closed), `AuctionType`
- **BidLane** — one route: Origin, Destination, FreightType, VolumeForecast, VehicleType, SLA, Region
- **BidInvitation** — links BidEvent to a Carrier with a unique invite token
- **CarrierProposal** — carrier's bid; has `LanePrices`, `Documents`, `Version`, `Status`
- **BidTemplate** / **BidTemplateColumn** — reusable BID structure definitions
- **ShipperFacility** — Matriz or Filial with CNPJ, address, Lat/Lon (auto-geocoded)
- **FreightRate** — rate per km by vehicle type
- **CepCache** — persisted ViaCEP results
- **AppLog** — structured JSON log entries
- **AuditLog** — user action audit trail

## Key Business Rules

1. **CNPJ Validation**: branch digits `0001` = Matriz; anything else = Filial.
   Filial requires its Matriz (same 8-digit root) to exist first.
2. **Template uniqueness**: `IX_BidTemplates_ShipperId_Name` — same shipper cannot
   have two templates with the same name. Frontend detects existing selection and
   sends PUT (update) instead of POST (create).
3. **Route Engine**: selecting a CD origin and clicking "Gerar Rotas" calls the
   ORS Matrix API to calculate distance/duration from that CD to all other active
   facilities, returning results sorted by shortest time.
4. **Auto-geocoding**: when a facility is created or updated, the system
   automatically geocodes its ZipCode via ORS and persists Lat/Lon.

## External API Integration

### ViaCEP (CEP Lookup)
- Endpoint: `GET https://viacep.com.br/ws/{cep}/json/`
- Named HttpClient: `"ViaCEP"`
- Caching: `ConcurrentDictionary` in-memory → `CepCaches` SQL table → API fallback
- Service: `ICepService` / `CepService`

### OpenRouteService (Geocoding + Routing)
- Named HttpClient: `"OpenRouteService"`
- API key in `appsettings.json` → `OpenRouteService:ApiKey`
- Geocode: `GET /geocode/search?api_key=...&text=...&boundary.country=BR&size=1`
- Directions: `GET /v2/directions/driving-car?api_key=...&start=...&end=...`
- Matrix: `POST /v2/matrix/driving-car` with `Authorization: {apiKey}` header,
  body `{ locations, sources, destinations, metrics: ["duration","distance"] }`
- Services: `IFreightService`, `IRouteEngineService`

## Frontend Architecture

- Single-page app via `navigateTo(pageId)` toggling `<section class="page-section" data-page="xxx">`
- Sidebar built dynamically from `SIDEBAR_MENUS[role]`
- All API calls use `authHeaders()` (JWT Bearer + X-Correlation-Id)
- `apiUrl(path)` resolves `API_BASE` (handles `file://` vs `http://` origins)
- Data grids use `.studio-grid` + `.studio-grid__th` classes (NDD-inspired)
- Route Engine: `bidGenerateRoutes()` calls the suggestions endpoint and
  auto-populates the lanes grid with origin/dest/vehicle/time/distance

## Known Pitfalls & Resolutions

| Problem | Solution |
|---------|----------|
| `innerHTML +=` destroys listeners | Use `createElement` + `appendChild` |
| File lock during `dotnet build` | `taskkill /F /IM dotnet.exe` + `<UseAppHost>false</UseAppHost>` in csproj |
| Browser caching old `app.js` | Bump `?v=N` on script tag + `Cache-Control: no-cache` in static files |
| `.page-section` selector too generic | Use `.page-section[data-page="xxx"]` |
| Duplicate template name insert | Check existence before insert; frontend sends PUT for updates |
| Scoped DbContext in singleton | Inject `AppDbContext` directly (scoped), not `IDbContextFactory` |
| CORS for `file://` origin | `AllowAnyOrigin()` in dev CORS policy |
| PowerShell `curl` syntax | Use `Invoke-RestMethod` instead |

## Coding Conventions

- For more detail, see [rules/project-conventions.mdc](../../.cursor/rules/project-conventions.mdc),
  [rules/csharp-patterns.mdc](../../.cursor/rules/csharp-patterns.mdc), and
  [rules/frontend-patterns.mdc](../../.cursor/rules/frontend-patterns.mdc)

### C# Backend
- Primary constructors for DI
- `CancellationToken ct` on all async methods
- `DeleteBehavior.Restrict` for FKs that could cascade
- `HasPrecision(18, 2)` for money; `HasPrecision(18, 4)` for rates
- `AsNoTracking()` for read-only queries
- Named HttpClients via `IHttpClientFactory`
- Interface + implementation pattern: `IXxxService` → `XxxService`
- Register as `AddScoped` in `Program.cs`

### Frontend JS
- Never use `element.innerHTML +=`
- Use `authHeaders()` for all fetch calls
- Handle errors: check `res.ok`, show `alert(await res.text())`
- Portuguese user-facing messages
- Template columns are dynamic — grid headers render from template data

## Demo Users (seeded)

| Email | Password | Role |
|-------|----------|------|
| shipper@test.com | Shipper123! | Shipper |
| carrier@test.com | Carrier123! | Carrier |
| admin@test.com   | Admin123!   | Admin   |

## Running the Project

```bash
cd TransportBidPortal
dotnet build
dotnet run
# Opens at http://localhost:5157
```

Database is auto-created via `EnsureCreated()` (dev mode drops and recreates on startup).
