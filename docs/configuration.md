# Configuration

## Server configuration (API)
Primary settings are in SyncApp26/SyncApp26.API/appsettings.json. Development overrides live in appsettings.Development.json. The active environment is controlled by ASPNETCORE_ENVIRONMENT.

Key settings:
- ConnectionStrings:DefaultConnection
  - SQLite path. Relative paths are resolved against the API content root.
  - Example: Data Source=../SyncApp26.Infrastructure/SyncApp26.db;Mode=ReadWrite
- JwtSettings:SecretKey, Issuer, Audience, ExpirationMinutes
  - JWT signing and validation settings.
- Frontend:LoginUrl, Frontend:BaseUrl, Frontend:ResetPasswordUrl
  - Used in email links and redirects.
- Smtp:Host, Port, Username, Password, FromEmail, FromName, EnableSsl
  - Used by SmtpEmailService for verification, password reset, and signature emails.
- Logging:LogLevel
  - Controls log verbosity (Information and Warning by default).

Operational guidance:
- Do not commit real SMTP credentials or production JWT secrets.
- Prefer environment variables or a secret store for production.
- Update CORS origins in SyncApp26/SyncApp26.API/Program.cs to match deployed SPA URLs.

## Client configuration (Angular)
Environment files under SyncApp26/SyncApp26.Client/src/environments/:
- environment.ts (local)
- environment.staging.ts
- environment.prod.ts

Key settings:
- apiUrl: API base URL (for example http://localhost:5022/api)
- endpoints: relative paths used by services

## CORS
Allowed origins are configured in SyncApp26/SyncApp26.API/Program.cs. Ensure the SPA base URL is included for local and deployed environments.

## SignalR
The SignalR hub is exposed at /hubs/sync. CSV sync progress uses the X-Connection-Id header or connectionId query string to route streaming updates to the correct client.
