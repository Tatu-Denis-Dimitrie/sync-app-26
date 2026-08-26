# API Reference

Base path: /api

## Conventions
- Authentication uses a JWT access token in an httpOnly session cookie, set by the login/refresh endpoints. There is no client-visible token; the browser sends the cookie automatically.
- Unsafe methods (POST/PUT/PATCH/DELETE) require the CSRF header `X-XSRF-TOKEN`, echoed from the `XSRF-TOKEN` cookie (Angular's built-in XSRF interceptor does this automatically). Exempt: safe methods, requests carrying an `Authorization: Bearer` header, and the pre-session endpoints below.
- Most endpoints return JSON objects or arrays.
- Partial success for sync operations may return HTTP 207.
- Timestamps are UTC and identifiers are GUIDs.

## Common status codes
- 200: success
- 202: accepted with warnings (registration email failure)
- 204: no content (delete or mapping operations)
- 207: partial success (CSV sync)
- 400: validation error
- 401: unauthenticated
- 403: unauthorized
- 404: not found
- 429: rate limit exceeded
- 500: server error

## Rate limiting
Two layers, both keyed by client IP:
- **Global limiter**: 300 requests/minute/IP, applied to every request as a blanket ceiling.
- **Named policies** (layered under the global limit, on top of it):
	| Policy | Limit | Applied to |
	|---|---|---|
	| `login` | 5/min/IP | `POST /authentication/login` |
	| `auth-sensitive` | 5/min/IP | `POST /authentication/register`, `GET /authentication/verify-email`, `POST /authentication/google-login`, `POST /authentication/microsoft-login`, `POST /authentication/forgot-password`, `POST /authentication/reset-password` |
	| `signing-token` | 10/min/IP | `POST /documentsignature/request-signature`, `GET /documentsignature/validate-token/{token}`, `POST /documentsignature/consume-token` |

A rejected request returns 429 with `{ "message": "Too many requests. Try again later." }`.

## Authentication (public)

### POST /authentication/register
Creates a new user and sends a verification email.

Request body (RegisterUserRequestDTO):
- firstName (string, required)
- lastName (string, required)
- email (string, required)
- password (string, required)

Password rules:
- Minimum 8 characters
- At least one uppercase, one lowercase, one digit, and one special character

Responses:
- 200: { message }
- 202: { message, error } when email delivery fails
- 400: validation error

### GET /authentication/verify-email
Query parameters:
- email (string, required)
- token (string, required)

Behavior:
- Verifies the token and redirects to the configured login URL.

### POST /authentication/login
Request body (LoginUserRequestDTO):
- email (string, required)
- password (string, required)

Response:
- 200: { message, user } — also sets the session and refresh cookies
	- user: { id, email, firstName, lastName, roles }

### POST /authentication/google-login
Signs in with a Google ID token. Link-only: succeeds only if the token's email already belongs to
an existing user (created via CSV sync or by an admin). Never creates a new user.

Request body (GoogleLoginRequestDTO):
- idToken (string, required) — the ID token returned by Google Identity Services

Response:
- 200: { message, user } — identical shape to /authentication/login
	- user: { id, email, firstName, lastName, roles }
- 400: { message } — idToken missing
- 401: { message } — invalid/expired token, Google email not verified, or no account exists for that email

### POST /authentication/microsoft-login
Signs in with a Microsoft ID token. Link-only: succeeds only if the token's email already belongs to
an existing user (created via CSV sync or by an admin). Never creates a new user. Unlike Google, the
Microsoft identity platform has no email-verified claim; the `email` claim's presence is the only
signal available, so an incorrect/stale claim can only fail closed (no matching user), never sign in
as someone else.

Request body (MicrosoftLoginRequestDTO):
- idToken (string, required) — the ID token returned by the Microsoft identity platform (MSAL)

Response:
- 200: { message, user } — identical shape to /authentication/login
	- user: { id, email, firstName, lastName, roles }
- 400: { message } — idToken missing
- 401: { message } — invalid/expired token, or no account exists for that email

### POST /authentication/forgot-password
Request body (ForgotPasswordRequestDTO):
- email (string, required)

Response:
- 200: { message }

### POST /authentication/reset-password
Request body (ResetPasswordWithTokenRequestDTO):
- email (string, required)
- token (string, required)
- newPassword (string, required)

Response:
- 200: { message }

## Session (public)

### GET /authentication/me
Always 200. Returns the caller's session state derived from the request cookies; also (re)issues the
`XSRF-TOKEN` cookie. Called once by the client's app initializer, before the first route navigation.

Response:
- 200: `{ authenticated: false }`, or
- 200: `{ authenticated: true, user, impersonating, impersonator }` — `impersonator` is the admin's
  full profile (not just an id) when `impersonating` is true, otherwise `null`.

### POST /authentication/logout
Revokes the refresh token (if any) and clears both auth cookies. Always 200.

### POST /authentication/refresh
Rotates the refresh token and mints a new access token from the refresh cookie.

Response:
- 200: { message } — new cookies set
- 401: { message } — refresh cookie missing, invalid, expired, or reused outside its grace window (revokes the user's whole refresh chain)

### POST /authentication/impersonate/{userId}
Admin-only. Starts a view-only session on the target user's identity (30 min access token, no refresh
token; the admin's own refresh tokens are revoked for the duration).

Response:
- 200: { message, user, impersonating: true }
- 400/403/404: target is self, an Admin, or not found

### POST /authentication/stop-impersonation
Ends impersonation and issues a fresh access+refresh pair for the original admin (re-verified to
still exist and still hold the Admin role).

Response:
- 200: { message, user }
- 400: not currently impersonating
- 401: the original admin no longer exists or no longer holds the Admin role

## CSV sync

### POST /csvsync/upload
Uploads a user CSV, validates, and returns a comparison.

Request:
- Content-Type: multipart/form-data
- file: CSV file
- Query: skipInvalidRows (bool, optional)
- Header: X-Connection-Id (optional, for SignalR progress)

Response (ComparisonResponseDTO):
- comparisons: UserComparisonDTO[]
- totalRows, validRows, invalidRows
- errors[], warnings[]
- validationTimeMs, comparisonTimeMs, totalTimeMs
- fileName

### POST /csvsync/sync
Applies selected CSV changes.

Request body (SyncRequestDTO):
- fileName (string, optional)
- items: UserSyncItemDTO[]

UserSyncItemDTO:
- id (string)
- status (new | modified | deleted)
- csvData (CsvUserDTO, optional)
- conflicts: FieldConflictDTO[]

Response (SyncResultDTO):
- success, recordsProcessed, recordsFailed, recordsSkipped
- message, errors[], processingTimeMs

### POST /csvsync/upload-departments
Uploads a department CSV and returns differences.

Request:
- Content-Type: multipart/form-data
- file: CSV file

Response:
- CSVDepartmentComparisionDTO[]

### POST /csvsync/sync-departments
Applies department changes.

Request body (DepartmentSyncRequestDTO):
- items: CSVDepartmentComparisionDTO[]

Response:
- SyncResultDTO

## Users (protected)

### GET /user/{id}
Response: UserGETResponseDTO

### GET /user/personal-id/{personalId}
Response: UserGETResponseDTO

### GET /user
Response: UserGETResponseDTO[]
Notes:
- Admins see all users.
- Non-admins see themselves and direct reports.

### GET /user/lookup
Query: search (optional), page (default 1), pageSize (default 20, max 100)
Response (UserLookupPageDTO): { items: UserLookupResponseDTO[], totalCount }
Notes:
- Admins and SSM/SU officers search across all users.
- Non-admins are scoped to the users they can otherwise access.

### GET /user/department/{departmentId}
Response: UserGETResponseDTO[]

### GET /user/assigned-to/{assignedToId}
Response: UserGETResponseDTO[]

### POST /user
Role: Admin
Request body (UserRequestDTO):
- firstName, lastName, email (required)
- departmentId (required)
- function (optional)
- assignedToId (optional)
- roleName (optional)

Response (UserResponseDTO):
- success, message

### PUT /user/{id}/roles
Role: Admin
Sets the user's full set of role assignments (replaces, not merges).

Request body (SetUserRolesRequestDTO):
- roleNames: string[] (required)

Response (UserResponseDTO):
- success, message

### PUT /user/{id}
Request body (UserRequestDTO)

Response (UserResponseDTO):
- success, message

### DELETE /user/{id}
Response (UserResponseDTO):
- success, message

### GET /user/{id}/ssm-su-form
Response (UserSSMSUFormDTO):
- id, firstName, lastName, email, personalId
- departmentName, functionName, roleName
- managerFirstName, managerLastName, managerFunctionName
- DateOfBirth, PlaceOfBirth, Address, BloodGroup, BadgeNumber
- Education, Qualifications
- CommuteRoute, CommuteDurationMinutes
- admittedByName, admittedByFunction, admittedDate
- hireDate, createdAt
- initialTrainings[]
- latestInstructorSignature, latestInstructorSignatureMethod
- latestVerifierSignature, latestVerifierSignatureMethod

### PUT /user/{id}/ssm-su-form
Request body (UpdateUserSSMSUFormDTO):
- DateOfBirth, PlaceOfBirth, Address, BloodGroup, BadgeNumber
- Education, Qualifications
- CommuteRoute, CommuteDurationMinutes
- admittedByName, admittedByFunction, admittedDate
- initialTrainings[] (InitialTrainingEntryDTO)

Response (UserResponseDTO):
- success, message

### POST /user/bulk-initial-training
Request body (BulkInitialTrainingDTO):
- documentType (SSM | SU | Both)
- Introductory and workplace training fields
- selectedDepartmentId (optional)
- applyToAllUsers (bool)
- selectedUserIds[]

Response (BulkInitialTrainingResultDTO):
- successCount, skippedCount, failedCount, errors[]

## Departments

### GET /department/{id}
Response: DepartmentGETResponseDTO

### GET /department
Response: DepartmentGETResponseDTO[]

### GET /department/scheduled-for-deletion
Response: DepartmentGETResponseDTO[]

### POST /department/{id}/restore
Response: DepartmentResponseDTO

### POST /department
Request body (DepartmentRequestDTO):
- name (required)
- isActive (optional)

Response (DepartmentResponseDTO)

### PUT /department/{id}
Request body (DepartmentRequestDTO)

Response (DepartmentResponseDTO)

### DELETE /department/{id}
Query:
- transferToId (optional)

Response (DepartmentResponseDTO)

## Functions

### GET /function
Response: Function[]

### GET /function/{id}
Response: Function

### POST /function
Request body: string (functionName)

Response: 200 OK

### DELETE /function/{id}
Response: 200 OK

## Department functions

### GET /departmentfunction/{departmentId}
Response: Function[]

### POST /departmentfunction/{departmentId}
Request body: string (functionName)
Response: 204 No Content

### DELETE /departmentfunction/{departmentId}
Request body: string (functionName)
Response: 204 No Content

## Import history

### GET /importhistory
Response: ImportHistory[]

### GET /importhistory/{id}
Response: ImportHistory

### POST /importhistory
Request body (ImportHistoryRequestDTO):
- fileName

Response: ImportHistory

### DELETE /importhistory/{id}
Response: 204 No Content

## User change history

### GET /userchangehistory
Response: UserChangeHistory[]

### GET /userchangehistory/{id}
Response: UserChangeHistory

### GET /userchangehistory/byImportHistory/{importHistoryId}
Response: UserChangeHistory[]

### GET /userchangehistory/byUser/{userId}
Response: UserChangeHistory[]

### POST /userchangehistory
Request body (UserChangeHistoryRequestDTO):
- importHistoryId (optional)
- userId
- fieldName
- oldValue, newValue
- status (optional)

Response: UserChangeHistory

### DELETE /userchangehistory/{id}
Response: 204 No Content

## Data change requests (protected)

### GET /datachangerequest
Role: Admin
Response: DataChangeRequestDTO[]

### GET /datachangerequest/pending-count
Role: Admin
Response: { count }

### GET /datachangerequest/my-requests
Response: DataChangeRequestDTO[]

### POST /datachangerequest
Request body (CreateDataChangeRequestDTO):
- requestedChangesJson (string, required) — keys are filtered against an allowlist (matching the UI's own field list); anything outside it returns 400 naming the disallowed field(s), or is silently stripped if at least one allowed field remains.
- reason (string, required)

Response: DataChangeRequestDTO

### POST /datachangerequest/request-email-change
Role: Basic User or Line Manager
Dedicated flow for the one field the generic request above always excludes. Requires the new address to share the same domain as the user's current one; resolved by an admin like any other request.

Request body (RequestEmailChangeDTO):
- newEmail (string, required)

Response: DataChangeRequestDTO, or 400 with an error message (e.g. domain mismatch)

### GET /datachangerequest/confirm-email
Public. Dead scaffold from an earlier, abandoned design — not reachable from the current UI flow (email changes go through request-email-change + admin approval instead).
Query:
- reqId, token

### PUT /datachangerequest/{id}/resolve
Role: Admin
Request body (ResolveDataChangeRequestDTO):
- status (Approved | Rejected)

Response: DataChangeRequestDTO

## Documents (protected)

### POST /document/bulk-generate
Request body:
- documentType (SSM | SU | Both)
- selectedUserIds[] (optional)

Response:
- message, generated, skipped, generatedByType, emailsSent, emailsFailed, emailError

### POST /document/bulk-generate-async
Background variant of bulk-generate. Same request body.

Response:
- jobId, total

### GET /document/bulk-generate-status/{jobId}
Polls a job started by bulk-generate-async. 403 if the caller doesn't own the job.

Response:
- total, generated, skipped, processed, phase (generating | emailing | done), generatedByType, emailsSent, emailsFailed, emailError, emailsAborted, completed, message, error

### POST /document/generate
Request body:
- userId (guid)
- documentType (SSM | SU)

Response:
- message, documentId

### GET /document/user/{userId}
Query: page (default 1), pageSize (default 10, max 100)
Response (DocumentListPageDTO): { items: DocumentView[], totalCount }
Access: self, admin, SSM/SU officer, or the target user's line manager.

### GET /document/all
Response: DocumentView[]
Notes:
- Admins and SSM/SU officers see every document.
- Everyone else sees own documents and direct reports.

### GET /document/my-pending-signatures
Query: page, pageSize (as above)
Response (DocumentListPageDTO)

### GET /document/manager-pending-signatures
Query: page, pageSize (as above)
Response (DocumentListPageDTO)

### GET /document/my-signed-documents
Query: page, pageSize (as above)
Response (DocumentListPageDTO)

### GET /document/manager-signed-documents
Query: page, pageSize (as above)
Response (DocumentListPageDTO)

### GET /document/instructor-pending-signatures
Query: page, pageSize (as above)
Response (DocumentListPageDTO): documents awaiting the caller's SSM/SU officer signature.

### GET /document/instructor-signed-documents
Query: page, pageSize (as above)
Response (DocumentListPageDTO)

### GET /document/admin-pending-signatures
Role: Admin
Response: DocumentView[]
Legacy-only: returns rows still stuck in the `PendingAdmin` status from before the Instructor rename. No new document reaches this status.

### GET /document/admin-signed-documents
Role: Admin
Response: DocumentView[]
Same legacy scope as above.

### POST /document/regenerate-documents
Role: Admin or Line Manager
Response: { message, regenerated }

### POST /document/backfill-signature-versions
Role: Admin
One-off repair for SignatureRecords created before the Version column existed (see docs/03_data-model.md and docs/08_signature-safety.md). Safe to run more than once.
Response: { message, updated }

### GET /document/token-for-document/{documentId}
Response: { token }

### GET /document/{documentId}/view-pdf
Response: application/pdf

DocumentView fields:
- id, userId
- userFirstName, userLastName, userEmail
- userDepartment, userFunction
- documentType, status
- generatedAt, pdfFilePath, documentHash
- userSignatureMethod, userSignatureData, userSignatureIpAddress, userSignedAt
- managerSignatureMethod, managerSignatureData, managerSignatureIpAddress, managerSignedAt
- instructorSignatureMethod, instructorSignatureData, instructorSignatureIpAddress, instructorSignedAt
- adminSignatureMethod, adminSignatureData, adminSignatureIpAddress, adminSignedAt (legacy rows only)
- userSignatureId, managerSignatureId, instructorSignatureId, adminSignatureId — ids of the current `SignatureRecord` behind each signature, for use with the verification endpoints below

## Document signatures

### POST /documentsignature/request-signature
Request body:
- email
- documentId
- documentName

Response: { message }

### GET /documentsignature/validate-token/{token}
Response:
- documentId
- documentName
- email
- documentType
- isManagerSigning
- isInstructorSigning
- isAdminSigning (legacy rows only)
- periodicTrainingId

### POST /documentsignature/consume-token
Request body:
- token
- signatureMethod (Draw | Type)
- signatureData (base64)
- bulkSign (bool)
- periodicTrainingId (optional)

Response:
- message
- count

### POST /documentsignature/bulk-sign
Any authenticated user. Signs every document currently pending the caller's signature, in whatever role(s) apply to them (user/manager/instructor).
Request body:
- signatureMethod
- signatureData

Response:
- message, count

### POST /documentsignature/bulk-sign-async
Role: SSM Officer, SU Officer, or Line Manager. Background variant scoped to the officer queue only (`PendingInstructor` documents of the given type) — it does not touch `PendingManager` documents. The caller must be the officer for the requested type.
Request body:
- signatureMethod
- signatureData
- documentType (SSM | SU, default SSM)

Response:
- jobId, total

### GET /documentsignature/bulk-sign-status/{jobId}
Role: SSM Officer, SU Officer, or Line Manager. 403 if the caller doesn't own the job.
Response:
- total, signed, completed, error

### GET /documentsignature/pending-ssm-admin-count
Role: SSM Officer, SU Officer, or Line Manager
Query: documentType (SSM | SU, default SSM)
Response:
- count

## Notifications (protected)

### POST /notification/notify-user/{userId}
Request body (NotificationRequestDTO):
- documentType (SSM | SU)

Response: { message }

### POST /notification/notify-manager/{managerId}
Role: Admin
Request body (NotificationRequestDTO)

Response: { message }

### POST /notification/notify-all-managers
Role: Admin
Request body (NotificationRequestDTO)

Response: { message }

## Signature anomaly alerts (protected)
Surfaces unexpected signature-verification failures (e.g. a document whose HMAC chain broke) for admin follow-up.

### GET /signatureanomalyalert/unread
Response: SignatureAnomalyAlert[]

### POST /signatureanomalyalert/dismiss-all
Marks every unread alert as dismissed for the caller.

Response: { message }

## Roles (protected)

### GET /roles
Role: Admin
Response: Role[]

### POST /roles
Role: Admin
Request body (CreateRoleRequestDTO):
- name (required)

Response: Role, or 400 if the name already exists

### DELETE /roles/{id}
Role: Admin
Response: { message }, or 400 (e.g. attempting to delete a system role)

## Work sites (protected)

### GET /worksite/{id}
Response: WorkSiteGETResponseDTO

### GET /worksite
Response: WorkSiteGETResponseDTO[]

### GET /worksite/scheduled-for-deletion
Role: Admin
Response: WorkSiteGETResponseDTO[]

### POST /worksite/{id}/restore
Role: Admin
Response: WorkSiteResponseDTO

### POST /worksite
Role: Admin
Request body (WorkSiteRequestDTO):
- name (required)
- isActive (optional)

Response: WorkSiteResponseDTO

### PUT /worksite/{id}
Role: Admin
Request body (WorkSiteRequestDTO)

Response: WorkSiteResponseDTO

### DELETE /worksite/{id}
Role: Admin
Soft delete. Users assigned to the work site are unassigned (WorkSiteId set to null), not transferred — unlike Department, having no work site is a valid state.

Response: WorkSiteResponseDTO

## Periodic training (protected)

### POST /periodictraining
Request body (CreatePeriodicTrainingDTO):
- userId
- trainingDate, durationHours
- occupation, materialTaught
- instructorName, verifierName

Response: PeriodicTrainingResponseDTO

### GET /periodictraining/{id}
Response: PeriodicTrainingResponseDTO

### GET /periodictraining/user/{userId}
Response: PeriodicTrainingResponseDTO[]

### PUT /periodictraining/{id}
Request body (UpdatePeriodicTrainingDTO)
Response: PeriodicTrainingResponseDTO

### DELETE /periodictraining/{id}
Response: { message }

### POST /periodictraining/bulk
Request body (BulkCreatePeriodicTrainingDTO):
- trainingDate, durationHours
- occupation, materialTaught
- instructorName, verifierName
- documentType (SSM | SU | Both)
- selectedDepartmentId (optional)
- applyToAllUsers (bool)
- selectedUserIds[]

Response (BulkCreateResultDTO):
- successCount, failedCount, errors[]

## User signatures (protected)

### GET /usersignature/my
Response: UserSignatureResponseDTO | null

### GET /usersignature/{userId}
Response: UserSignatureResponseDTO

### POST /usersignature/save
Request body (SaveUserSignatureRequestDTO):
- signatureData (base64)
- signatureMethod (Draw | Type)

Response:
- message
- signature (UserSignatureResponseDTO)

### DELETE /usersignature/revoke
Response: { message }

### GET /usersignature/{userId}/history
Response: UserSignatureHistoryResponseDTO[]

### GET /usersignature/my/history
Response: UserSignatureHistoryResponseDTO[]

## Signature verification (protected)
See docs/08_signature-safety.md for the HMAC chaining and Version model behind these endpoints.

### GET /signatures/{id}/verification-status
Recomputes and returns the HMAC/chain verification status of one SignatureRecord.
Access: self, any admin, or the line manager of the signer.

Response:
- signatureId, signerUserId
- status (Valid | Invalid | ChainBroken | Legacy | NotFound)
- isHashValid, isChainValid, isLegacy
- verifiedAt

### POST /signatures/verification-status/batch
Request body (BatchVerificationStatusRequestDTO):
- signatureIds[] (max 100 per call)

Response: array of the same shape as above. Ids the caller is not allowed to see are silently omitted (NotFound entries are returned to any authenticated caller since they carry no signer-attributable data).

### GET /signatures/training/{periodicTrainingId}/history
Returns every signature made for a periodic training, grouped by signer role and ordered by signing date. Access follows the training's employee, not any individual signer (self, any admin, or the employee's line manager).

Response (PeriodicTrainingSignatureHistoryDTO):
- periodicTrainingId, userId
- versionsByRole: object keyed by signer role ("User"/"Manager"/"Admin"), each an array (ordered by signedAt ascending) of:
	- signatureId, version (which HMAC canonical schema signed this — not a resign counter), isMostRecentSignature
	- signerRole, signerUserId, signerFullNameSnapshot, signedAt
	- status (Valid | Invalid | ChainBroken | Legacy)

404 if no periodic training exists with this id.

### POST /signatures/verification-status/by-users
Recomputes and returns the verification status of every SignatureRecord belonging to each requested employee's documents (their own signature and any manager/admin countersignatures) — grouped by the document's owner, not the signer. This is the real-time check for "did launching a new session break any of this employee's existing signatures."

Request body (VerificationStatusForUsersRequestDTO):
- userIds[] (max 200 per call)

Response: object keyed by userId, each value an array of SignatureVerificationStatusResponseDTO (same shape as the single/batch endpoints above). UserIds the caller is not allowed to see are silently omitted from the response. Employees with no signatures yet get an empty array, not an error or a missing key.

400 if userIds is empty or exceeds the cap.

## Version

### GET /version
Response:
- version

## SignalR
- Hub: /hubs/sync
- Events:
	- UploadProgress { message, percent }
	- ComparisonResult (UserComparisonDTO)
	- SyncProgress { processed, failed, skipped }
	- SignatureUpdated (no payload) — broadcast to all clients after any signature is recorded, so open dashboards can refresh.
	- SignatureAnomalyAlert (payload describing the failed verification) — broadcast by the background `SignatureVerificationSweepService` when it finds a signature whose HMAC/chain no longer verifies.

## DTO location
Shared request and response contracts are defined under SyncApp26/SyncApp26.Shared/DTOs.

## Examples

### Login
Request:
```http
POST /api/authentication/login
Content-Type: application/json

{
	"email": "alex.admin.example@example.com",
	"password": "example"
}
```

Response:
```json
{
	"message": "Login successful.",
	"user": {
		"id": "2d6511d7-27c4-4bcb-8c5f-9c01e86aa7c0",
		"email": "alex.admin@example.com",
		"firstName": "Alex",
		"lastName": "Admin",
		"roles": ["Admin"]
	}
}
```
The session is carried entirely by the `syncapp26_session`/`syncapp26_refresh` httpOnly cookies set on this response — there is no token in the body for the client to store.

### CSV upload and compare
Request:
```bash
curl -X POST "http://localhost:5022/api/csvsync/upload?skipInvalidRows=false" \
	-H "X-Connection-Id: 0b3b3a5c-1f2a-44b9-9a2b-4baf1e9b2f10" \
	-F "file=@sample-csvs/valid-users.csv"
```

Response (abbreviated):
```json
{
	"comparisons": [
		{
			"id": "46baf1e0-0f2c-4f9f-b8e9-0f4f4a4b3a7a",
			"status": "modified",
			"dbUser": {
				"id": "46baf1e0-0f2c-4f9f-b8e9-0f4f4a4b3a7a",
				"personalId": "E1024",
				"roleName": "Basic User",
				"firstName": "Maria",
				"lastName": "Ionescu",
				"email": "m.ionescu@example.com",
				"departmentName": "Production"
			},
			"csvUser": {
				"personalId": "E1024",
				"firstName": "Maria",
				"lastName": "Ionescu",
				"email": "m.ionescu@example.com",
				"departmentName": "Production",
				"function": "Operator"
			},
			"conflicts": [
				{
					"field": "function",
					"dbValue": "Operator I",
					"csvValue": "Operator",
					"selected": false
				}
			],
			"selected": true
		}
	],
	"totalRows": 100,
	"validRows": 100,
	"invalidRows": 0,
	"validationTimeMs": 42,
	"comparisonTimeMs": 118,
	"totalTimeMs": 180,
	"fileName": "valid-users.csv"
}
```

### CSV sync
Request:
```http
POST /api/csvsync/sync
Content-Type: application/json

{
	"fileName": "valid-users.csv",
	"items": [
		{
			"id": "46baf1e0-0f2c-4f9f-b8e9-0f4f4a4b3a7a",
			"status": "modified",
			"csvData": {
				"personalId": "E1024",
				"firstName": "Maria",
				"lastName": "Ionescu",
				"email": "m.ionescu@example.com",
				"departmentName": "Production",
				"function": "Operator"
			},
			"conflicts": [
				{
					"field": "function",
					"dbValue": "Operator I",
					"csvValue": "Operator",
					"selectedValue": "csv",
					"selected": true
				}
			]
		}
	]
}
```

Response:
```json
{
	"success": true,
	"recordsProcessed": 1,
	"recordsFailed": 0,
	"recordsSkipped": 0,
	"message": "Sync completed",
	"errors": [],
	"processingTimeMs": 212
}
```

### Generate document
Request:
```http
POST /api/document/generate
Content-Type: application/json

{
	"userId": "4ed4e3a4-8c86-4c92-9b33-6a0f1c0798c1",
	"documentType": "SSM"
}
```

Response:
```json
{
	"message": "Document generated successfully and signature requested.",
	"documentId": "7ed0470d-8f5e-4d72-b1b2-1145a783b334"
}
```

### Consume signature token
Request:
```http
POST /api/documentsignature/consume-token
Content-Type: application/json

{
	"token": "<one-time-token>",
	"signatureMethod": "Draw",
	"signatureData": "data:image/png;base64,....",
	"bulkSign": false
}
```

Response:
```json
{
	"message": "Document successfully signed using secure link.",
	"count": 1
}
```
