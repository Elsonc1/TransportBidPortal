# Project History & Evolution

Detailed changelog of design decisions and feature evolution.

## Phase 1 — Initial Scaffold

- Created full ASP.NET Core Web API project with EF Core + SQL Server LocalDB
- Designed domain model: AppUser, BidEvent, BidLane, BidInvitation, CarrierProposal,
  CarrierProposalLanePrice, CarrierProposalDocument, Notification, AuditLog
- JWT authentication with three roles: Shipper, Carrier, Admin
- Frontend SPA with vanilla JS, HTML5, CSS3
- Seed data with demo users (shipper/carrier/admin)

## Phase 2 — Excel Import (later partially removed)

- Built `ExcelImportService` using ClosedXML for parsing uploaded Excel files
- Implemented header detection with canonical aliases and fuzzy matching
- Created `Excel Mapper Wizard` with drag-and-drop upload and step-by-step mapping

**Decision**: User requested removal of the Excel *import* wizard for BID creation.
BID creation was redesigned as a manual spreadsheet-like grid in the portal.
The `BID Template Studio` (for defining and exporting blank templates) was retained.
`ExcelImportService` methods for template matching and export are still used.

## Phase 3 — BID Template Studio

- Inspired by `ndd-frontend` project for enterprise grid aesthetics
- Template CRUD: name, columns with key/displayName/aliases/dataType/isRequired/sortOrder
- Template export to Excel via ClosedXML
- Inline editing and drag-and-drop column reordering
- Unique index on (ShipperId, Name) to prevent duplicate template names

## Phase 4 — Shipper Facilities (Matriz / Filiais)

- ShipperFacility entity with CNPJ, address, Lat/Lon
- CNPJ business rules: branch 0001 = Matriz, others = Filial
- Filial requires its Matriz to exist (same 8-digit CNPJ root)
- CRUD endpoints in ShipperController

## Phase 5 — CEP Lookup + Caching

- ViaCEP API integration for Brazilian postal code lookup
- Three-layer cache: ConcurrentDictionary → CepCaches SQL table → API
- CepController exposes GET /api/cep/{cep}
- Frontend: "Buscar CEP" button auto-fills address/city/state in facility form

## Phase 6 — Freight Calculation

- OpenRouteService APIs for geocoding and route directions
- FreightRate entity with rate per km by vehicle type (seeded with defaults)
- FreightService calculates: geocode → route → cost = distance * ratePerKm
- FreightController exposes estimate endpoint and rate management

## Phase 7 — Structured Logging

- AppLog entity with Timestamp, Level, Service, CorrelationId, Message, StackTrace, User
- CorrelationIdMiddleware injects X-Correlation-Id header
- RequestLoggingMiddleware logs all requests to AppLog table
- Admin UI: filterable log viewer with color-coded severity (Info/Warn/Error/Debug)
- LogController with search, stats, and pagination

## Phase 8 — Manual BID Creation

- Removed Excel import wizard endpoints from ShipperController
- Redesigned bid-create page with template selector + manual lane grid
- CreateBidRequest DTO with BidLaneInput list
- POST /api/shipper/bids creates BidEvent with lanes
- Dynamic grid columns based on selected template
- Template selector syncs from Template Studio

## Phase 9 — Route Engine

- RouteEngineService using ORS Matrix API for bulk distance/duration calculation
- Auto-geocoding: facilities get Lat/Lon populated automatically on create/update
- GET /api/shipper/route-engine/suggestions returns routes sorted by shortest time
- Frontend "Gerar Rotas Automaticamente" button auto-populates lane grid
- Each lane row shows time estimate and distance in the UI

## Recurring Issues & Fixes

1. **File locks during build**: solved with `<UseAppHost>false</UseAppHost>` and `taskkill`
2. **CORS for file:// origin**: AllowAnyOrigin in dev
3. **Sidebar click not working**: `innerHTML +=` was destroying event listeners
4. **Page sections not showing**: selector was matching sidebar items instead of content sections
5. **Duplicate template save error**: frontend now sends PUT for updates, backend validates name uniqueness
6. **DI lifetime mismatch**: CepService changed from IDbContextFactory to direct AppDbContext injection
7. **Browser cache**: added ?v=N cache bust on script tag + no-cache headers on static files
