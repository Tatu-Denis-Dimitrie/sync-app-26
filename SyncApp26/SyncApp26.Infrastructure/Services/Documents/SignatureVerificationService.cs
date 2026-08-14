using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Infrastructure.Context;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;

namespace SyncApp26.Infrastructure.Services
{
    /// <summary>
    /// Recomputes SignatureRecord.SignatureHmac from each record's frozen signer-identity fields
    /// (SignerFullNameSnapshot/SignerPositionSnapshot — never re-derived from the live User row,
    /// so a later name change never retroactively invalidates a past signature) combined with
    /// training-content values (MaterialTaught/DurationHours/TrainingDate) when the record is
    /// linked to a PeriodicTraining, reserializing with the SAME SignatureCanonicalSerializer
    /// schema version the record was originally signed under (SignatureRecord.Version) — never
    /// today's schema — so a later schema change never retroactively invalidates a past signature.
    /// Only the MOST RECENT signature in a record's signing slot — determined by SignedAt, not by
    /// Version — compares against the LIVE training row, so editing content after signing correctly
    /// fails verification for the current signature and forces a re-sign. Older, superseded
    /// signatures always compare against their own frozen snapshot instead: they must stay
    /// verifiable as "what was actually signed at the time" regardless of later edits, which is the
    /// entire point of keeping signature history. Also checks the per-signer hash chain that
    /// DocumentService.CreateSignatureRecordAsync builds — a record's stored PreviousSignatureHash
    /// must match its signer's actual prior SignatureHmac.
    /// </summary>
    public class SignatureVerificationService : ISignatureVerificationService
    {
        private readonly ApplicationDbContext _context;
        private readonly IHmacSignatureService _hmacSignatureService;

        public SignatureVerificationService(ApplicationDbContext context, IHmacSignatureService hmacSignatureService)
        {
            _context = context;
            _hmacSignatureService = hmacSignatureService;
        }

        public async Task<SignatureVerificationStatusResponseDTO?> GetVerificationStatusAsync(Guid signatureId)
        {
            var record = await _context.SignatureRecords.FirstOrDefaultAsync(r => r.Id == signatureId);
            if (record == null) return null;

            var signerChain = await LoadSignerChainAsync(record.SignerUserId);
            var previous = FindPreviousRecord(record, signerChain);
            var isMostRecent = await IsMostRecentInSlotAsync(record);
            var liveTraining = isMostRecent && record.PeriodicTrainingId.HasValue
                ? await _context.PeriodicTrainings.FirstOrDefaultAsync(t => t.Id == record.PeriodicTrainingId.Value)
                : null;
            return await ComputeStatusAsync(record, previous, liveTraining);
        }

        // True when this record is the most recently signed one in its signing slot —
        // (PeriodicTrainingId, SignerRole), or (UserDocumentId, SignerRole) when unlinked — going by
        // SignedAt/CreatedAt, not by Version (which records an HMAC schema, not a signing order).
        // Drives whether ComputeStatusAsync compares against live training content (most recent
        // signature only) or strictly against the record's own frozen snapshot (every superseded
        // signature). SQLite's EF provider can't order by DateTimeOffset server-side, so the
        // (already small, slot-filtered) results are sorted client-side — same pattern as
        // LoadSignerChainAsync below.
        private async Task<bool> IsMostRecentInSlotAsync(SignatureRecord record)
        {
            var siblings = record.PeriodicTrainingId.HasValue
                ? await _context.SignatureRecords
                    .Where(r => r.PeriodicTrainingId == record.PeriodicTrainingId.Value && r.SignerRole == record.SignerRole)
                    .ToListAsync()
                : await _context.SignatureRecords
                    .Where(r => r.PeriodicTrainingId == null && r.UserDocumentId == record.UserDocumentId && r.SignerRole == record.SignerRole)
                    .ToListAsync();

            var mostRecent = siblings
                .OrderByDescending(r => r.SignedAt)
                .ThenByDescending(r => r.CreatedAt)
                .First();

            return mostRecent.Id == record.Id;
        }

        public async Task<List<SignatureVerificationStatusResponseDTO>> GetVerificationStatusBatchAsync(IEnumerable<Guid> signatureIds)
        {
            var ids = signatureIds.Distinct().ToList();
            var records = await _context.SignatureRecords.Where(r => ids.Contains(r.Id)).ToListAsync();
            var recordsById = records.ToDictionary(r => r.Id);

            var chainsBySigner = new Dictionary<Guid, List<SignatureRecord>>();
            foreach (var signerId in records.Select(r => r.SignerUserId).Distinct())
            {
                chainsBySigner[signerId] = await LoadSignerChainAsync(signerId);
            }

            var trainingIds = records.Where(r => r.PeriodicTrainingId.HasValue).Select(r => r.PeriodicTrainingId!.Value).Distinct().ToList();
            var trainingsById = trainingIds.Count == 0
                ? new Dictionary<Guid, PeriodicTraining>()
                : (await _context.PeriodicTrainings.Where(t => trainingIds.Contains(t.Id)).ToListAsync())
                    .ToDictionary(t => t.Id);

            var (mostRecentIdByTraining, mostRecentIdByDocument) = await LoadMostRecentIdsBySlotAsync(records);

            var results = new List<SignatureVerificationStatusResponseDTO>();
            foreach (var id in ids)
            {
                if (!recordsById.TryGetValue(id, out var record))
                {
                    results.Add(new SignatureVerificationStatusResponseDTO
                    {
                        SignatureId = id,
                        SignerUserId = Guid.Empty,
                        Status = "NotFound",
                        IsHashValid = false,
                        IsChainValid = false,
                        IsLegacy = false,
                        VerifiedAt = DateTimeOffset.UtcNow
                    });
                    continue;
                }

                var previous = FindPreviousRecord(record, chainsBySigner[record.SignerUserId]);
                var mostRecentId = record.PeriodicTrainingId.HasValue
                    ? mostRecentIdByTraining[(record.PeriodicTrainingId.Value, record.SignerRole)]
                    : mostRecentIdByDocument[(record.UserDocumentId, record.SignerRole)];
                var isMostRecent = record.Id == mostRecentId;
                var liveTraining = isMostRecent && record.PeriodicTrainingId.HasValue
                    ? trainingsById.GetValueOrDefault(record.PeriodicTrainingId.Value)
                    : null;
                results.Add(await ComputeStatusAsync(record, previous, liveTraining));
            }

            return results;
        }

        // Grouped most-recent-signature-per-slot lookup for a batch, so determining "is this the
        // most recent signature in its slot" for every record costs two queries total instead of
        // one fetch per record (which IsMostRecentInSlotAsync does for the single-record path,
        // where that cost is fine).
        private async Task<(Dictionary<(Guid, string), Guid> ByTraining, Dictionary<(Guid, string), Guid> ByDocument)> LoadMostRecentIdsBySlotAsync(List<SignatureRecord> batchRecords)
        {
            var trainingIds = batchRecords.Where(r => r.PeriodicTrainingId.HasValue).Select(r => r.PeriodicTrainingId!.Value).Distinct().ToList();
            var docIds = batchRecords.Where(r => !r.PeriodicTrainingId.HasValue).Select(r => r.UserDocumentId).Distinct().ToList();

            var byTraining = trainingIds.Count == 0
                ? new Dictionary<(Guid, string), Guid>()
                : (await _context.SignatureRecords
                        .Where(r => r.PeriodicTrainingId.HasValue && trainingIds.Contains(r.PeriodicTrainingId.Value))
                        .ToListAsync())
                    .GroupBy(r => (r.PeriodicTrainingId!.Value, r.SignerRole))
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SignedAt).ThenByDescending(r => r.CreatedAt).First().Id);

            var byDocument = docIds.Count == 0
                ? new Dictionary<(Guid, string), Guid>()
                : (await _context.SignatureRecords
                        .Where(r => r.PeriodicTrainingId == null && docIds.Contains(r.UserDocumentId))
                        .ToListAsync())
                    .GroupBy(r => (r.UserDocumentId, r.SignerRole))
                    .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SignedAt).ThenByDescending(r => r.CreatedAt).First().Id);

            return (byTraining, byDocument);
        }

        public async Task<Dictionary<Guid, DocumentSignatureIdsDTO>> GetLatestSignatureRecordIdsAsync(IEnumerable<Guid> documentIds)
        {
            var ids = documentIds.Distinct().ToList();
            if (ids.Count == 0) return new Dictionary<Guid, DocumentSignatureIdsDTO>();

            // Picks the current authoritative signature per (document, role) regardless of which
            // PeriodicTraining (if any) it was captured against — this answers "what's currently
            // filling this document's User/Manager/Admin sign-off slot," the same question the
            // document list/pending-queue endpoints already answer via UserSignedAt etc.
            var records = (await _context.SignatureRecords
                    .Where(r => ids.Contains(r.UserDocumentId))
                    .ToListAsync())
                .OrderByDescending(r => r.SignedAt)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();

            var result = new Dictionary<Guid, DocumentSignatureIdsDTO>();
            foreach (var group in records.GroupBy(r => r.UserDocumentId))
            {
                var dto = new DocumentSignatureIdsDTO();
                foreach (var record in group)
                {
                    switch (record.SignerRole)
                    {
                        case "User" when dto.UserSignatureId == null:
                            dto.UserSignatureId = record.Id;
                            break;
                        case "Manager" when dto.ManagerSignatureId == null:
                            dto.ManagerSignatureId = record.Id;
                            break;
                        case "Instructor" when dto.InstructorSignatureId == null:
                            dto.InstructorSignatureId = record.Id;
                            break;
                        case "Admin" when dto.AdminSignatureId == null:
                            dto.AdminSignatureId = record.Id;
                            break;
                    }
                }
                result[group.Key] = dto;
            }

            return result;
        }

        public async Task<PeriodicTrainingSignatureHistoryDTO?> GetSignatureHistoryForTrainingAsync(Guid periodicTrainingId)
        {
            var training = await _context.PeriodicTrainings.FirstOrDefaultAsync(t => t.Id == periodicTrainingId);
            if (training == null) return null;

            var records = (await _context.SignatureRecords
                    .Where(r => r.PeriodicTrainingId == periodicTrainingId)
                    .ToListAsync())
                .OrderBy(r => r.SignerRole)
                .ThenBy(r => r.SignedAt)
                .ThenBy(r => r.CreatedAt)
                .ToList();

            var mostRecentIdByRole = records
                .GroupBy(r => r.SignerRole)
                .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.SignedAt).ThenByDescending(r => r.CreatedAt).First().Id);

            var signerChainsBySignerId = new Dictionary<Guid, List<SignatureRecord>>();
            var versionsByRole = new Dictionary<string, List<SignatureVersionSummaryDTO>>();

            foreach (var record in records)
            {
                var isMostRecent = record.Id == mostRecentIdByRole[record.SignerRole];

                if (!signerChainsBySignerId.TryGetValue(record.SignerUserId, out var signerChain))
                {
                    signerChain = await LoadSignerChainAsync(record.SignerUserId);
                    signerChainsBySignerId[record.SignerUserId] = signerChain;
                }
                var previous = FindPreviousRecord(record, signerChain);
                var status = await ComputeStatusAsync(record, previous, isMostRecent ? training : null);

                if (!versionsByRole.TryGetValue(record.SignerRole, out var roleVersions))
                {
                    roleVersions = new List<SignatureVersionSummaryDTO>();
                    versionsByRole[record.SignerRole] = roleVersions;
                }

                roleVersions.Add(new SignatureVersionSummaryDTO
                {
                    SignatureId = record.Id,
                    Version = record.Version,
                    IsMostRecentSignature = isMostRecent,
                    SignerRole = record.SignerRole,
                    SignerUserId = record.SignerUserId,
                    SignerFullNameSnapshot = record.SignerFullNameSnapshot,
                    SignedAt = record.SignedAt,
                    Status = status.Status
                });
            }

            return new PeriodicTrainingSignatureHistoryDTO
            {
                PeriodicTrainingId = periodicTrainingId,
                UserId = training.UserId,
                DocumentType = training.DocumentType,
                VersionsByRole = versionsByRole
            };
        }

        // "Did launching a new session break any of this employee's existing signatures" — scoped
        // by the document's owner (UserDocument.UserId), not the signer, so a manager's
        // countersignature is reported under the employee whose document it's on, not the manager.
        public async Task<Dictionary<Guid, List<SignatureVerificationStatusResponseDTO>>> GetVerificationStatusForUsersAsync(IEnumerable<Guid> userIds)
        {
            var ids = userIds.Distinct().ToList();
            var result = ids.ToDictionary(id => id, _ => new List<SignatureVerificationStatusResponseDTO>());
            if (ids.Count == 0) return result;

            // Resolves employees -> their document ids first, so the SignatureRecords query below
            // is a single plain UserDocumentId filter — bounded queries, not one per requested user.
            var employeeIdByDocumentId = await _context.UserDocuments
                .Where(d => ids.Contains(d.UserId))
                .Select(d => new { d.Id, d.UserId })
                .ToDictionaryAsync(d => d.Id, d => d.UserId);
            if (employeeIdByDocumentId.Count == 0) return result;

            var docIds = employeeIdByDocumentId.Keys.ToList();
            var records = await _context.SignatureRecords
                .Where(r => docIds.Contains(r.UserDocumentId))
                .ToListAsync();
            if (records.Count == 0) return result;

            var chainsBySigner = new Dictionary<Guid, List<SignatureRecord>>();
            foreach (var signerId in records.Select(r => r.SignerUserId).Distinct())
            {
                chainsBySigner[signerId] = await LoadSignerChainAsync(signerId);
            }

            var trainingIds = records.Where(r => r.PeriodicTrainingId.HasValue).Select(r => r.PeriodicTrainingId!.Value).Distinct().ToList();
            var trainingsById = trainingIds.Count == 0
                ? new Dictionary<Guid, PeriodicTraining>()
                : (await _context.PeriodicTrainings.Where(t => trainingIds.Contains(t.Id)).ToListAsync())
                    .ToDictionary(t => t.Id);

            var (mostRecentIdByTraining, mostRecentIdByDocument) = await LoadMostRecentIdsBySlotAsync(records);

            foreach (var record in records)
            {
                var previous = FindPreviousRecord(record, chainsBySigner[record.SignerUserId]);
                var mostRecentId = record.PeriodicTrainingId.HasValue
                    ? mostRecentIdByTraining[(record.PeriodicTrainingId.Value, record.SignerRole)]
                    : mostRecentIdByDocument[(record.UserDocumentId, record.SignerRole)];
                var isMostRecent = record.Id == mostRecentId;
                var liveTraining = isMostRecent && record.PeriodicTrainingId.HasValue
                    ? trainingsById.GetValueOrDefault(record.PeriodicTrainingId.Value)
                    : null;
                var status = await ComputeStatusAsync(record, previous, liveTraining);

                result[employeeIdByDocumentId[record.UserDocumentId]].Add(status);
            }

            return result;
        }

        private async Task<List<SignatureRecord>> LoadSignerChainAsync(Guid signerUserId)
        {
            return (await _context.SignatureRecords
                    .Where(r => r.SignerUserId == signerUserId)
                    .ToListAsync())
                .OrderByDescending(r => r.SignedAt)
                .ThenByDescending(r => r.CreatedAt)
                .ToList();
        }

        private static SignatureRecord? FindPreviousRecord(SignatureRecord record, List<SignatureRecord> signerChainDescending)
        {
            var index = signerChainDescending.FindIndex(r => r.Id == record.Id);
            if (index < 0 || index + 1 >= signerChainDescending.Count) return null;
            return signerChainDescending[index + 1];
        }

        private async Task<SignatureVerificationStatusResponseDTO> ComputeStatusAsync(SignatureRecord record, SignatureRecord? previous, PeriodicTraining? liveTraining)
        {
            var now = DateTimeOffset.UtcNow;

            if (record.IsLegacyUnverified || record.SignatureHmac == null)
            {
                return new SignatureVerificationStatusResponseDTO
                {
                    SignatureId = record.Id,
                    SignerUserId = record.SignerUserId,
                    UserDocumentId = record.UserDocumentId,
                    Status = "Legacy",
                    IsHashValid = false,
                    IsChainValid = false,
                    IsLegacy = true,
                    VerifiedAt = now
                };
            }

            // Signer identity stays frozen (a later rename must not retroactively invalidate a
            // past signature), but training content tracks the LIVE row when linked to one, so
            // editing it after signing changes the recomputed hash and correctly fails
            // verification — forcing a re-sign instead of silently going stale. Chosen per
            // triplet, not per field via `??`, so a live field cleared to null is itself treated
            // as a change rather than silently falling back to the frozen value.
            var materialTaught = liveTraining != null ? liveTraining.MaterialTaught : record.MaterialTaughtSnapshot;
            var durationHours = liveTraining != null ? liveTraining.DurationHours : record.DurationHoursSnapshot;
            var trainingDate = liveTraining != null ? liveTraining.TrainingDate : record.TrainingDateSnapshot;

            // Reserialize with the schema this exact record was signed under (record.Version),
            // never today's schema — that's the whole point of storing Version.
            var canonicalInput = new SignatureCanonicalInput(
                record.SignerUserId,
                record.SignerFullNameSnapshot,
                record.SignerPositionSnapshot,
                record.SignerBadgeNumberSnapshot,
                materialTaught,
                durationHours,
                trainingDate,
                record.SignedAt,
                record.PreviousSignatureHash,
                record.Version);
            var canonical = SignatureCanonicalSerializer.Serialize(canonicalInput);

            var isHashValid = await _hmacSignatureService.VerifyHmacAsync(canonical, record.SignatureHmac);
            var isChainValid = record.PreviousSignatureHash == previous?.SignatureHmac;

            var status = !isHashValid ? "Invalid" : !isChainValid ? "ChainBroken" : "Valid";

            return new SignatureVerificationStatusResponseDTO
            {
                SignatureId = record.Id,
                SignerUserId = record.SignerUserId,
                UserDocumentId = record.UserDocumentId,
                Status = status,
                IsHashValid = isHashValid,
                IsChainValid = isChainValid,
                IsLegacy = false,
                VerifiedAt = now
            };
        }
    }
}
