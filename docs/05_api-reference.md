# API Reference

Base path: /api

## Conventions
- Authentication uses JWT Bearer tokens on protected endpoints.
- Use Authorization: Bearer <token> for authenticated calls.
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
- 500: server error

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
- 200: { message, token, user }
	- user: { id, email, firstName, lastName, role }

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

### GET /user/department/{departmentId}
Response: UserGETResponseDTO[]

### GET /user/assigned-to/{assignedToId}
Response: UserGETResponseDTO[]

### POST /user
Request body (UserRequestDTO):
- firstName, lastName, email (required)
- departmentId (required)
- function (optional)
- assignedToId (optional)
- roleName (optional)

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

### GET /datachangerequest/my-requests
Response: DataChangeRequestDTO[]

### POST /datachangerequest
Request body (CreateDataChangeRequestDTO):
- requestedChangesJson (string, required)
- reason (string, required)

Response: DataChangeRequestDTO

### GET /datachangerequest/confirm-email
Public, used when requests require email verification.
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
- message, generated, skipped

### POST /document/generate
Request body:
- userId (guid)
- documentType (SSM | SU)

Response:
- message, documentId

### GET /document/user/{userId}
Response: DocumentView[]

### GET /document/all
Response: DocumentView[]
Notes:
- Admins see all.
- Non-admins see own documents and direct reports.

### GET /document/my-pending-signatures
Response: DocumentView[]

### GET /document/manager-pending-signatures
Response: DocumentView[]

### GET /document/my-signed-documents
Response: DocumentView[]

### GET /document/manager-signed-documents
Response: DocumentView[]

### GET /document/admin-pending-signatures
Role: Admin
Response: DocumentView[]

### GET /document/admin-signed-documents
Role: Admin
Response: DocumentView[]

### POST /document/regenerate-documents
Role: Admin
Response: { message, regenerated }

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
- adminSignatureMethod, adminSignatureData, adminSignatureIpAddress, adminSignedAt

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
- isAdminSigning
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
Role: Admin or Line Manager
Request body:
- signatureMethod
- signatureData

Response:
- message, count

### POST /documentsignature/bulk-sign-async
Role: Admin or Line Manager
Request body:
- signatureMethod
- signatureData

Response:
- jobId, total

### GET /documentsignature/bulk-sign-status/{jobId}
Role: Admin or Line Manager
Response:
- total, signed, completed, error

### POST /documentsignature/admin-sign-and-send-generated-documents
Role: Admin
Request body:
- documentType (SSM | SU | Both)
- signatureMethod
- signatureData

Response:
- message, documentsSigned, emailsSent

### GET /documentsignature/pending-ssm-admin-count
Role: Admin
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
	- SignatureUpdated (no payload)

## DTO location
Shared request and response contracts are defined under SyncApp26/SyncApp26.Shared/DTOs.

## Examples

### Login
Request:
```http
POST /api/authentication/login
Content-Type: application/json

{
	"email": "alex.admin@example.com",
	"password": "example"
}
```

Response:
```json
{
	"message": "Login successful.",
	"token": "<jwt>",
	"user": {
		"id": "2d6511d7-27c4-4bcb-8c5f-9c01e86aa7c0",
		"email": "alex.admin@example.com",
		"firstName": "Alex",
		"lastName": "Admin",
		"role": "Admin"
	}
}
```

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
Authorization: Bearer <jwt>

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
