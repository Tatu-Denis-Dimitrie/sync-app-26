# SyncApp26

SyncApp26 is a data synchronization and compliance SSM/SU document platform. It provides CSV-based user and department sync, role-based management, and multi-stage document signing (SSM/SU) backed by a .NET 9 API and an Angular 21 SPA.

Version: 1.2.0

## Documentation
Start here: [docs/index.md](docs/index.md)

Key documents:
- [docs/index.md](docs/index.md)
- [docs/01_getting-started.md](docs/01_getting-started.md)
- [docs/02_architecture.md](docs/02_architecture.md)
- [docs/03_data-model.md](docs/03_data-model.md)
- [docs/04_business-workflows.md](docs/04_business-workflows.md)
- [docs/05_api-reference.md](docs/05_api-reference.md)
- [docs/06_client-app.md](docs/06_client-app.md)
- [docs/configuration.md](docs/configuration.md)

## Quick start

Backend (API):

```bash
cd SyncApp26
dotnet restore
dotnet run --project SyncApp26.API
```

Frontend (SPA):

```bash
cd SyncApp26/SyncApp26.Client
npm install
npm start
```

Default dev URLs:
- API: http://localhost:5022
- SPA: http://localhost:4200

## Repository layout
- SyncApp26/SyncApp26.API: REST API and SignalR hub
- SyncApp26/SyncApp26.Application: Services
- SyncApp26/SyncApp26.Domain: Entities and repository contracts
- SyncApp26/SyncApp26.Infrastructure: EF Core persistence
- SyncApp26/SyncApp26.Shared: DTOs and shared contracts
- SyncApp26/SyncApp26.Client: Angular SPA
- docs/: Documentation files

## Configuration
Configuration is primarily in appsettings.json. See [docs/configuration.md](docs/configuration.md) for API and client settings, **JWT** and **SMTP** requirements, and **SignalR** notes.

## Security notes
- Replace the default JWT secret and SMTP credentials before production.
- Do not commit real credentials to source control.

## Performance and capacity observations
We performed controlled import tests to establish baseline behavior and upper limits in the current SQLite-backed configuration. On the same test environment, a **1,000-user CSV import completed in approximately 1.9 seconds, and a 100,000-user import completed in approximately 19 seconds**. These results reflect end-to-end processing, including validation, comparison, and persistence, and should be treated as indicative rather than absolute.

When scaling further, we observed the **database becoming unstable beyond roughly 250,000 users**, indicating that the current SQLite configuration is not suitable for higher volumes. If growth beyond this threshold is expected, plan for a production-grade relational database and re-validate the import and reconciliation workflows under realistic load.