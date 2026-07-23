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
    /// so a later name change never retroactively invalidates a past signature) combined with the
    /// LIVE training-content values (MaterialTaught/DurationHours/TrainingDate) when the record is
    /// linked to a PeriodicTraining — so editing that content after signing changes the recomputed
    /// hash and correctly fails verification, forcing a re-sign. Also checks the per-signer hash
    /// chain that DocumentService.CreateSignatureRecordAsync builds — a record's stored
    /// PreviousSignatureHash must match its signer's actual prior SignatureHmac.
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
            var liveTraining = record.PeriodicTrainingId.HasValue
                ? await _context.PeriodicTrainings.FirstOrDefaultAsync(t => t.Id == record.PeriodicTrainingId.Value)
                : null;
            return await ComputeStatusAsync(record, previous, liveTraining);
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
                var liveTraining = record.PeriodicTrainingId.HasValue
                    ? trainingsById.GetValueOrDefault(record.PeriodicTrainingId.Value)
                    : null;
                results.Add(await ComputeStatusAsync(record, previous, liveTraining));
            }

            return results;
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

            var canonicalInput = new SignatureCanonicalInput(
                record.SignerUserId,
                record.SignerFullNameSnapshot,
                record.SignerPositionSnapshot,
                materialTaught,
                durationHours,
                trainingDate,
                record.SignedAt,
                record.PreviousSignatureHash);
            var canonical = SignatureCanonicalSerializer.Serialize(canonicalInput);

            var isHashValid = await _hmacSignatureService.VerifyHmacAsync(canonical, record.SignatureHmac);
            var isChainValid = record.PreviousSignatureHash == previous?.SignatureHmac;

            var status = !isHashValid ? "Invalid" : !isChainValid ? "ChainBroken" : "Valid";

            return new SignatureVerificationStatusResponseDTO
            {
                SignatureId = record.Id,
                SignerUserId = record.SignerUserId,
                Status = status,
                IsHashValid = isHashValid,
                IsChainValid = isChainValid,
                IsLegacy = false,
                VerifiedAt = now
            };
        }
    }
}
