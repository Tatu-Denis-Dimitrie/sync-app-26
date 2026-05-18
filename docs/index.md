# SyncApp26 Documentation

Version: 1.2.0
Last updated: 2026-05-11

## Scope
This documentation covers architecture, configuration, data model, workflows, API reference, client behavior, and performance characteristics for SyncApp26. It is intended to support implementation, QA validation, and operational readiness.

## Audience
- Engineering (backend and frontend)
- QA and automation
- Operations and support
- Security and compliance reviewers

## System summary
SyncApp26 is an enterprise HR data synchronization and compliance document platform built with:
- ASP.NET Core (.NET 9) API and SignalR hub
- Angular 21 SPA
- EF Core with SQLite storage
- SMTP email for verification, password reset, and signature workflows

## Key capabilities
- CSV user and department synchronization with conflict resolution
- Role-based access control (Admin, Line Manager, Basic User)
- SSM/SU document generation and multi-stage signatures
- Initial and periodic training data management
- Data change requests with admin review
- Real-time progress updates via SignalR

## Roles and access model
- Admin: full system access, generates documents, signs SSM documents as final step, and manages approvals.
- Line Manager: access to direct reports, can generate and countersign documents for assigned users.
- Basic User: access to own data and signature actions.

Access is enforced via JWT role claims and server-side authorization checks.

## Document map
- [01 Getting started](01_getting-started.md)
- [02 Architecture](02_architecture.md)
- [03 Data model](03_data-model.md)
- [04 Business workflows](04_business-workflows.md)
- [05 API reference](05_api-reference.md)
- [06 Client application](06_client-app.md)
- [07 Performance and capacity](07_performance-and-capacity.md)
- [08 Signature safety](08_signature-safety.md)
- [Configuration](configuration.md)

## Conventions
- Identifiers are GUIDs.
- Timestamps are UTC.
- REST base path: /api
- SignalR hub: /hubs/sync
