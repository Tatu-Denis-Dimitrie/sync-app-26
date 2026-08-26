# Configuration

## Server configuration (API)
Primary settings are in SyncApp26/SyncApp26.API/appsettings.json. Development overrides live in appsettings.Development.json. The active environment is controlled by ASPNETCORE_ENVIRONMENT.

Both appsettings.json and appsettings.Development.json are git-ignored, so a fresh clone has no config. A tracked template is provided at SyncApp26/SyncApp26.API/appsettings.example.json — copy it to appsettings.json and fill in your own values (JwtSettings:SecretKey and Smtp credentials at minimum):

```bash
cp SyncApp26/SyncApp26.API/appsettings.example.json SyncApp26/SyncApp26.API/appsettings.json
```

Key settings:
- ConnectionStrings:DefaultConnection
  - SQLite path. Relative paths are resolved against the API content root.
  - Example: Data Source=../SyncApp26.Infrastructure/SyncApp26.db;Mode=ReadWrite
- JwtSettings:SecretKey, Issuer, Audience, ExpirationMinutes
  - JWT signing and validation settings. The token itself now travels in an httpOnly cookie, not the response body.
- Auth:Cookie:Secure (bool, optional)
  - Overrides the auto-detected `Secure` flag on auth cookies (defaults to `!IsDevelopment()`). Set explicitly behind a reverse proxy, since `Request.IsHttps` is unreliable there.
- Frontend:LoginUrl, Frontend:BaseUrl, Frontend:ResetPasswordUrl
  - Used in email links and redirects.
- Smtp:Host, Port, Username, Password, FromEmail, FromName, EnableSsl
  - Used by SmtpEmailService for verification, password reset, and signature emails.
- Logging:LogLevel
  - Controls log verbosity (Information and Warning by default).
- SignatureVerificationSweep:Enabled (bool, default false), SignatureVerificationSweep:IntervalHours (int, default 24), SignatureVerificationSweep:IntervalMinutes (int, optional)
  - Opt-in background safety-net that periodically re-verifies every SignatureRecord and logs any that no longer verify (Invalid / ChainBroken). Disabled by default; the sweep recomputes an HMAC per signature, so enable it only after validating the cost at your data volume. Read-only — it never mutates data. IntervalMinutes overrides IntervalHours when set — useful for quickly observing a sweep run during testing; leave it unset in normal use.
- Authentication:Google:ClientId
  - OAuth client ID used to validate Google Sign-In ID tokens server-side. Required only if Google sign-in is used; must match the googleClientId configured in the Angular client (see below).
- Authentication:Microsoft:ClientId
  - Application (client) ID used to validate Microsoft Sign-In ID tokens server-side. Required only if Microsoft sign-in is used; must match the microsoftClientId configured in the Angular client (see below).
- SignatureHmac:DevKey
  - Dev-only HMAC key used to chain-sign `SignatureRecord` rows (see docs/08_signature-safety.md). Must be a long random value; do not reuse the example placeholder outside local dev.
- Serilog
  - Standard Serilog configuration section (MinimumLevel, Enrich, WriteTo). Ships with Console plus two rolling file sinks (`logs/syncapp-.log` for all levels, `logs/errors/error-.log` for Error and above), each capped at 10 MB/file with day-based rolling.
- LogRetention:SweepIntervalMinutes, LogRetention:RetentionDays, LogRetention:Directories
  - Drives the `LogFileRetentionService` background sweep that prunes old log files beyond what Serilog's own rolling limits remove. `Directories` lists each log folder with a `MaxFilesPerDay` cap.
- AllowedHosts
  - Standard ASP.NET Core host-header allowlist (`*` by default in the example template).

Note: `Frontend:BaseUrl` and `Frontend:ResetPasswordUrl` are read by the code and documented above, but `appsettings.example.json` currently only ships `Frontend:LoginUrl` — add the other two yourself if you need signature-link or password-reset emails to point somewhere other than the code's hardcoded `localhost:4200` fallback.

Operational guidance:
- Do not commit real SMTP credentials or production JWT secrets.
- Prefer environment variables or a secret store for production.
- Update CORS origins in SyncApp26/SyncApp26.API/Program.cs to match deployed SPA URLs. `AllowCredentials()` is set alongside them, which is required for the session cookie to be sent cross-origin but also means origins cannot be wildcarded (`AllowAnyOrigin` is incompatible with `AllowCredentials`).

## Rate limiting
Configured in Program.cs via `AddRateLimiter`, all partitioned per client IP:
- A global fixed-window limiter caps every request at 300/minute/IP.
- Named policies layer tighter limits on top for specific endpoints: `login` (5/min), `auth-sensitive` (5/min), `signing-token` (10/min). See docs/05_api-reference.md for exactly which endpoints use each.
- A rejected request gets HTTP 429 with a fixed JSON body, and the rejection is logged with the client IP and path.

## Client configuration (Angular)
Environment files under SyncApp26/SyncApp26.Client/src/environments/:
- environment.ts (local)
- environment.staging.ts
- environment.prod.ts

Key settings:
- apiUrl: API base URL. Relative (`/api`) in dev, routed to the API through proxy.conf.json (see angular.json's serve target) so the SPA and API are same-origin — required for the session cookie to work. Never point this at an absolute URL.
- endpoints: relative paths used by services
- googleClientId: OAuth client ID for Google Sign-In. Must match Authentication:Google:ClientId in the API config.
- microsoftClientId: Application (client) ID for Microsoft Sign-In. Must match Authentication:Microsoft:ClientId in the API config.

Note: angular.json has no fileReplacements configured, so environment.ts is used for every build configuration, including production. environment.prod.ts and environment.staging.ts are currently not wired up.

## CORS
Allowed origins are configured in SyncApp26/SyncApp26.API/Program.cs. Ensure the SPA base URL is included for local and deployed environments.

## SignalR
The SignalR hub is exposed at /hubs/sync. CSV sync progress uses the X-Connection-Id header or connectionId query string to route streaming updates to the correct client.
