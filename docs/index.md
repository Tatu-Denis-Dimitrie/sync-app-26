# SyncApp26 Documentation

Version: 1.31.0
Last updated: 2026-08-26

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
- Role-based access control (Admin, Line Manager, Basic User, plus independently-grantable SSM/SU Officer roles)
- SSM/SU document generation and multi-stage signatures
- Initial and periodic training data management
- Data change requests with admin review
- Admin "view as" impersonation with audit logging
- Real-time progress updates via SignalR

## Roles and access model
Five roles exist. A user's Admin/Line Manager/Basic User role and their SSM/SU Officer standing are granted independently — a person can hold any combination.
- Admin: full system access, generates documents, resolves data-change requests, and manages roles.
- Line Manager: access to direct reports; generates documents for assigned users and countersigns as the manager stage.
- Basic User: access to own data and signature actions.
- SSM Officer: signs the final ("Instructor") stage of SSM documents, for any employee's document, regardless of reporting line.
- SU Officer: signs the final ("Instructor") stage of SU documents, for any employee's document, regardless of reporting line.

Admin has no signing role in the document chain itself — the final signature is always the type-specific officer's. Access is enforced via JWT role claims and server-side authorization checks.

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
