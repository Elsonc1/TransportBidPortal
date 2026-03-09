# Transport BID Portal

Enterprise-style Transport Bidding portal built with ASP.NET Core, SQL Server, HTML5/CSS3/JavaScript, JWT auth, and BI-style dashboards.

## Features Implemented

- Shipper portal with drag-and-drop Excel BID creation
- Carrier portal with draft/final proposal submission and versioning
- Lane-level pricing, SLA confirmation, and operational capacity inputs
- Ranking engine with weighted scoring, KPI cards, and charts
- Excel and PDF dashboard export
- Notification system (in-platform + email simulation logs)
- Multi-round-ready auction model fields (auction type + round state)
- Audit log and role-based access control (Shipper/Carrier)
- Document upload storage for carrier proposals

## Tech Stack

- Backend: ASP.NET Core Web API (`net10.0`)
- Database: SQL Server (LocalDB default)
- ORM: Entity Framework Core
- Auth: JWT Bearer
- Excel processing: ClosedXML
- PDF export: QuestPDF
- Frontend: static HTML/CSS/JS + Chart.js

## Demo Users

- `shipper@demo.com` / `Demo@123`
- `carrier1@demo.com` / `Demo@123`
- `carrier2@demo.com` / `Demo@123`

## Run

```bash
dotnet restore
dotnet run
```

Open:

- `http://localhost:5157/`

## Excel Template Columns

Use these headers in row 1 (English or Portuguese aliases supported):

- `Origin` / `Origem`
- `Destination` / `Destino`
- `FreightType` / `TipoFrete`
- `VolumeForecast` / `Volume` / `PrevisaoVolume`
- `SLA` / `SlaRequirements`
- `VehicleType` / `TipoVeiculo`
- `InsuranceRequirements` / `Seguro`
- `PaymentTerms` / `PrazoPagamento`
- `Region` / `Regiao`

## Notes

- Development startup recreates the database (`EnsureDeleted + EnsureCreated`) for a clean demo environment.
- For production, replace with migrations and remove auto-drop behavior in `Program.cs`.
