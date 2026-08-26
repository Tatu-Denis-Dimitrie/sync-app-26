# Signature Safety and Integrity

## Purpose
This document describes how SyncApp26 handles signatures, the safety mechanisms in place, and the current integrity guarantees. It covers both stored personal signatures and document signing flows, including cryptographic proof, audit trail behavior, and known limitations.

## Scope
- User signature storage (personal signature used across documents)
- Document signature workflow (SSM/SU)
- Signature tokens and one-time links
- Cryptographic proof and hashing
- Signature records, HMAC chaining, and version history
- Audit trail and traceability
- Known limitations and recommended hardening

## Signature types

### Personal signature (UserSignature)
A personal signature is the reusable signature a user can store for later document signing.

Data stored:
- SignatureData: base64-encoded image data (PNG recommended)
- SignatureMethod: Draw or Type
- SignatureHash: SHA-256 of SignatureData
- CryptographicProof: RSA signature over a canonical string
- IpAddress, CreatedAt, UpdatedAt, RevokedAt

Behavior:
- Only one active signature is stored per user.
- Each save or revoke action writes an immutable history entry.

### Document signatures (UserDocument)
Document signatures are captured per generated SSM/SU document and include the employee, line manager, and instructor (the document type's SSM/SU officer) signatures. Both document types follow the same chain — there is no SSM/SU divergence in the status flow.

Data stored per document:
- UserSignatureMethod, UserSignatureData, UserSignatureIpAddress, UserSignedAt
- ManagerSignatureMethod, ManagerSignatureData, ManagerSignatureIpAddress, ManagerSignedAt
- InstructorSignatureMethod, InstructorSignatureData, InstructorSignatureIpAddress, InstructorSignedAt
- AdminSignatureMethod, AdminSignatureData, AdminSignatureIpAddress, AdminSignedAt — legacy fields, populated only on rows created before the Instructor rename
- UserCryptographicSignature, ManagerCryptographicSignature, InstructorCryptographicSignature, AdminCryptographicSignature (legacy)

Each signature event updates the document status:
- PendingUser -> PendingManager -> PendingInstructor -> Completed, identically for SSM and SU
- PendingAdmin is a legacy-only status: no document reaches it under the current flow, but historical rows created before this design may still carry it

## Token-based signing safety
SyncApp26 supports token-based signing for users without accounts or when direct sign links are required.

Token characteristics:
- 32 random bytes, base64 URL-safe encoding
- Single-use: tokens are marked IsUsed on consumption
- Expiration: 7 days from creation
- Token stored alongside Email, DocumentId, and optional PeriodicTrainingId

Validation and consumption:
- Tokens are validated against expiry and IsUsed
- Consumption marks the token as used
- Signing enforces role sequence and status rules

## Cryptographic proof model
SyncApp26 uses RSA signatures to provide server-issued proof that a signature was accepted.

### Algorithms
- RSA key: 2048-bit
- Hash: SHA-256
- Padding: PKCS#1 v1.5

### User signature proof
When a user stores a personal signature, the server computes:
- SignatureHash = SHA-256(SignatureData)
- Canonical string: "{SignatureHash}|{UserId}|{TimestampUtcTicks}"
- CryptographicProof = RSA.Sign(canonical)

The proof is stored in both UserSignature and UserSignatureHistory.

### Document signature proof
When a document is signed, the server computes:
- Canonical string: "{DocumentId}|{DocumentHash}|{IpAddress}|{TimestampUtc}"
- CryptographicSignature = RSA.Sign(canonical)

The cryptographic signature is stored on the document in the relevant role field:
- UserCryptographicSignature
- ManagerCryptographicSignature
- AdminCryptographicSignature

## Document hash and PDF snapshots
Each generated PDF snapshot is hashed and stored in UserDocument.DocumentHash.

Current flow:
1. PDF is generated (or regenerated after signing).
2. SHA-256 is computed over PDF bytes.
3. DocumentHash is updated with the new value.

This provides a tamper-evident hash for the stored PDF snapshot and enables downstream integrity checks.

## Signature records, HMAC chaining, and version history
Beyond the flat per-document/per-training signature fields described above, every signing event also writes an immutable SignatureRecord row — the authoritative audit trail for document and training signatures, distinct from the personal-signature audit trail (UserSignatureHistory).

Frozen at signing time and never re-derived from live data on verification:
- SignerFullNameSnapshot, SignerPositionSnapshot, SignerBadgeNumberSnapshot, SignerWorkSiteNameSnapshot: the signer's identity as of that moment, so a later name, badge, or work-site reassignment never retroactively invalidates a past signature. The badge number arrived with schema V2 (null on V1 records); the work-site name arrived with schema V3 (null on V1/V2 records).
- MaterialTaughtSnapshot, DurationHoursSnapshot, TrainingDateSnapshot: the training content, when the record is linked to a PeriodicTraining row.

### HMAC chaining
Each record stores:
- SignatureHmac: HMAC-SHA256 over a canonical serialization of the frozen fields above (SignatureCanonicalSerializer).
- PreviousSignatureHash: the same signer's previous SignatureHmac, across all of their documents, in signing order.

This forms a per-signer hash chain. Verification reconstructs the canonical string using the SignatureCanonicalSerializer schema recorded in the record's own Version (see below) — never today's schema — combined with the live training content only when this is the most recent signature in its slot (so an edit after signing is detected there), and confirms the recomputed hash matches the stored value, and separately confirms PreviousSignatureHash matches the prior record's SignatureHmac. IsLegacyUnverified marks rows backfilled before this mechanism existed; these are never treated as verified.

### Version
Each SignatureRecord also carries a Version: which SignatureCanonicalSerializer schema (field set and format) computed its SignatureHmac. It travels as a field on SignatureCanonicalInput itself (not a side parameter), so there is exactly one place that says which schema a given hash was computed under. It is set once at signing time to `SignatureCanonicalSerializer.CurrentVersion` and never changes afterward. This exists so that if the canonical schema ever changes — a field is added, removed, or reformatted — verification can still reconstruct the exact format a given signature was made under, instead of hashing today's field set against a signature made under an older one. Each schema version's serialization logic is frozen in code forever once real signatures exist under it; a schema change adds a new version (a new private SerializeVN method plus a bumped `CurrentVersion`) rather than editing an existing one. Version is **not** a resign counter: many signatures legitimately share the same Version (they were all made under the same schema). How many times a training slot has been re-signed is instead derived from ordering its SignatureRecords by SignedAt.

The version number itself is bound into the hashed bytes as the first field (domain separation), starting with V1 — this prevents a signature made under one schema from ever being misread as belonging to another. Because this was built into V1 from the start (not added retroactively), there was never a version predating it to worry about breaking.

Schema versions to date:
- **V1** — signer identity (id, full name, position), training content (material, duration, date), SignedAt as ISO-8601, previous hash. Frozen; records signed before the V2 bump still verify against it unchanged.
- **V2** — V1 plus the signer's badge number, inserted after the position field. Frozen; records signed before the V3 bump still verify against it unchanged, never reading the work-site name.
- **V3** (current) — V2 plus the signer's work-site name, inserted after the badge number. New signatures are made under this schema; V1/V2 records keep verifying under their own schema.

### Verification service
SignatureVerificationService recomputes each record's status on demand (never cached), returning one of: Valid, Invalid (recomputed hash no longer matches, e.g. training content changed since signing), ChainBroken (PreviousSignatureHash does not match the signer's actual prior record), Legacy (IsLegacyUnverified), or NotFound. Exposed via `GET /api/signatures/{id}/verification-status`, `POST /api/signatures/verification-status/batch`, and `GET /api/signatures/training/{periodicTrainingId}/history` (full signing history for a training, grouped by role), access-controlled the same way as document signatures (self, any admin, or the relevant line manager).

## Audit trail
The system records a durable audit trail for user signatures:
- UserSignatureHistory records Created, Updated, and Revoked actions.
- Each history entry includes the original signature data, hash, cryptographic proof, IP address, and timestamp.

For document signatures, UserDocument and PeriodicTraining rows capture signature data and timestamps, enabling traceability by document and training session.

## Access control and signing order
Safety relies on role-aware access control and sequential signing rules:
- Only the document owner can apply the employee signature.
- Only the assigned line manager can countersign after the employee signature.
- The instructor signature is allowed only after employee and manager signatures, and only for a caller holding the document type's officer role (SsmOfficer for SSM, SuOfficer for SU) — any employee's document, not limited to a reporting line. Admin has no signing role in this chain at all.
- The trainee can never sign their own manager or instructor steps, even if they hold that role themselves for other employees' documents — someone else must fill those slots.

These rules are enforced in `DocumentSigningService` before a signature is recorded.

## Signature data storage
Signature data is stored as base64 strings in:
- UserSignature (personal signature)
- UserDocument (per-document signature)
- PeriodicTraining (row-level signature snapshots)
- UserInitialTraining (first-time signature capture for initial training sections)

The system does not store the user password or raw authentication tokens in signature records.

## Key management
The RSA private key is stored in server_rsa_key.json in the API working directory.
- The key is created automatically if missing.
- The private key is used to sign user and document proof strings.
- Access to this file must be restricted in production.

Recommended hardening:
- Store keys outside the application directory.
- Use OS-level secret storage or a dedicated key vault.
- Rotate keys periodically and keep a key history for proof verification.

## Limitations and current behavior
The signature system provides strong auditability but is not a full legal e-signature solution. Notable limitations:
- Signature data is not tied to external identity providers; it is tied to the authenticated account at time of signing.
- The server signs a canonical string based on the document hash at the time of signing. The PDF is then regenerated to embed the signature, which updates DocumentHash. This means the cryptographic signature reflects the pre-regeneration hash, not the final PDF hash.
- Tokens are stored in the database in clear text (required for validation).

## Recommendations for higher assurance
If higher legal or compliance guarantees are required, consider:
- Re-sign the canonical string after PDF regeneration to bind proof to the final hash.
- Store the final PDF hash and sign a stable payload that includes the final hash.
- Add timestamp authority (TSA) integration for time-stamping signatures.
- Enforce shorter token TTLs and rate-limiting on token validation.
- Encrypt signature data at rest and add access logging for signature retrieval.

## Verification checklist (QA)
- Verify that user signatures create a history entry on every save and revoke.
- Verify token expiry and single-use behavior.
- Confirm signing order enforcement (user -> manager -> instructor, identically for SSM and SU).
- Confirm DocumentHash changes after signature and PDF regeneration.
- Confirm signature metadata is captured on UserDocument and PeriodicTraining.
- Confirm an older, superseded signature in a slot still verifies as Valid after the slot is re-signed and the training content is edited again (it must check against its own frozen snapshot, not live content).
- Confirm only the most recent signature in a slot is compared against live training content.
- Confirm GET /api/signatures/{id}/verification-status, the batch endpoint, and the training history endpoint return the expected status for Valid/Invalid/ChainBroken/Legacy cases.
