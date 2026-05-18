# Signature Safety and Integrity

## Purpose
This document describes how SyncApp26 handles signatures, the safety mechanisms in place, and the current integrity guarantees. It covers both stored personal signatures and document signing flows, including cryptographic proof, audit trail behavior, and known limitations.

## Scope
- User signature storage (personal signature used across documents)
- Document signature workflow (SSM/SU)
- Signature tokens and one-time links
- Cryptographic proof and hashing
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
Document signatures are captured per generated SSM/SU document and include the employee, line manager, and (for SSM) admin verification signatures.

Data stored per document:
- UserSignatureMethod, UserSignatureData, UserSignatureIpAddress, UserSignedAt
- ManagerSignatureMethod, ManagerSignatureData, ManagerSignatureIpAddress, ManagerSignedAt
- AdminSignatureMethod, AdminSignatureData, AdminSignatureIpAddress, AdminSignedAt
- UserCryptographicSignature, ManagerCryptographicSignature, AdminCryptographicSignature

Each signature event updates the document status:
- PendingUser -> PendingManager -> PendingAdmin -> Completed
- For SU, PendingAdmin is skipped and the document completes after manager signature.

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

## Audit trail
The system records a durable audit trail for user signatures:
- UserSignatureHistory records Created, Updated, and Revoked actions.
- Each history entry includes the original signature data, hash, cryptographic proof, IP address, and timestamp.

For document signatures, UserDocument and PeriodicTraining rows capture signature data and timestamps, enabling traceability by document and training session.

## Access control and signing order
Safety relies on role-aware access control and sequential signing rules:
- Only the document owner can apply the employee signature.
- Only the assigned line manager can countersign after the employee signature.
- Admin verification is allowed only for SSM documents and only after employee and manager signatures.
- Non-admin users are blocked from signing documents outside their reporting chain.

These rules are enforced in the API controller before calling UpdateDocumentSignatureAsync.

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
- Confirm signing order enforcement (user -> manager -> admin for SSM).
- Confirm DocumentHash changes after signature and PDF regeneration.
- Confirm signature metadata is captured on UserDocument and PeriodicTraining.
