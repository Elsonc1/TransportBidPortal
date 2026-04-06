# API Endpoints Reference

## Auth — `AuthController`
| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| POST | /api/auth/login | No | Login, returns JWT |
| POST | /api/auth/register | No | Register new user |

## Shipper — `ShipperController` (Role: Shipper)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/shipper/templates | List templates with columns |
| POST | /api/shipper/templates | Create template (checks duplicate name) |
| PUT | /api/shipper/templates/{id} | Update template |
| DELETE | /api/shipper/templates/{id} | Delete template |
| GET | /api/shipper/templates/{id}/export | Export template as Excel |
| POST | /api/shipper/bids | Create BID with lanes (manual entry) |
| GET | /api/shipper/bids | List shipper's BIDs |
| POST | /api/shipper/bids/{id}/invite | Invite carriers to a BID |
| GET | /api/shipper/facilities | List shipper's facilities |
| POST | /api/shipper/facilities | Create facility (auto-geocodes) |
| PUT | /api/shipper/facilities/{id} | Update facility (auto-geocodes) |
| DELETE | /api/shipper/facilities/{id} | Delete facility |
| GET | /api/shipper/route-engine/suggestions?originFacilityId= | Generate routes from origin CD |
| GET | /api/shipper/dashboard?bidId= | Dashboard data for a BID |
| GET | /api/shipper/dashboard/export-excel?bidId= | Export dashboard as Excel |
| GET | /api/shipper/dashboard/export-pdf?bidId= | Export dashboard as PDF |

## Carrier — `CarrierController` (Role: Carrier)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/carrier/bids | List invited BIDs |
| POST | /api/carrier/proposals | Save/submit proposal |
| GET | /api/carrier/proposals | List carrier's proposals |

## Admin — `AdminController` (Role: Admin)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/admin/mapping-profiles | List mapping profiles |
| POST | /api/admin/mapping-profiles | Create mapping profile |
| PUT | /api/admin/mapping-profiles/{id} | Update mapping profile |
| POST | /api/admin/mapping-profiles/{id}/analyze-excel | Analyze Excel against profile |

## System — `SystemController` (Authorized, any role)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/system/carriers | List all carrier users |

## CEP — `CepController` (Authorized)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/cep/{cep} | Lookup CEP (cached) |

## Freight — `FreightController` (Authorized)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/freight/estimate?originCep=&destinationCep=&vehicleType= | Calculate freight estimate |
| GET | /api/freight/rates | List active freight rates |
| POST | /api/freight/rates | Create new freight rate (Admin/Shipper) |

## Log — `LogController` (Role: Admin)
| Method | Route | Description |
|--------|-------|-------------|
| GET | /api/log/search?page=&pageSize=&level=&... | Search logs with filters |
| GET | /api/log/stats?from=&to= | Log statistics by level |
