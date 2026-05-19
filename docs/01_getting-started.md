# Getting Started

## Scope
This guide covers local setup, basic validation, and common troubleshooting steps.

## Prerequisites
- .NET 9 SDK
- Node.js and npm (compatible with Angular 21)
- Optional: SQLite viewer for local data inspection

## Backend (API)
From the repository root:

```bash
cd SyncApp26
dotnet restore
dotnet run --project SyncApp26.API
```

Default dev URLs (see launchSettings.json):
- http://localhost:5022
- https://localhost:7154

Notes:
- Swagger is enabled in Development at /swagger.
- The SQLite database is created automatically at SyncApp26/SyncApp26.Infrastructure/SyncApp26.db.
- Database seeding runs on startup.

## Frontend (Angular SPA)
From the repository root:

```bash
cd SyncApp26/SyncApp26.Client
npm install
npm start
```

The SPA runs at http://localhost:4200 and uses the API base URL from src/environments/environment.ts.

## Local verification
- Open http://localhost:5022/swagger (Development)
- GET http://localhost:5022/api/version
- Open http://localhost:4200 and confirm the login page loads

## Authentication flow (local)
1. Register a user using the SPA or POST /api/authentication/register.
2. Verify the email address using the link sent by SMTP (required for login).
3. Log in and confirm the token is stored in localStorage as authToken.

If SMTP is not configured, registration still creates the user but email delivery fails. You will receive a warning response and will not be able to verify the account without configuring SMTP.

## CSV test data
Sample files are available under sample-csvs/. CSV validation rules for user sync:
- Required headers: PersonalId, FirstName, LastName, Email, DepartmentName
- Optional headers: AssignedToPersonalId, Function
- Files must be UTF-8 encoded

## Common issues
- CORS errors: update allowed origins in SyncApp26/SyncApp26.API/Program.cs.
- DB path errors: verify ConnectionStrings:DefaultConnection in appsettings.json.
- Auth issues: ensure authToken and currentUser exist in localStorage.
- Port conflicts: ensure 5022 (API) and 4200 (SPA) are free.
