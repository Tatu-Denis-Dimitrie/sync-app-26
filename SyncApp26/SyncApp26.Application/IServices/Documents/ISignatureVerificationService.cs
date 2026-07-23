using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Shared.DTOs.Response.SignatureVerification;

namespace SyncApp26.Application.IServices
{
    public interface ISignatureVerificationService
    {
        Task<SignatureVerificationStatusResponseDTO?> GetVerificationStatusAsync(Guid signatureId);
        Task<List<SignatureVerificationStatusResponseDTO>> GetVerificationStatusBatchAsync(IEnumerable<Guid> signatureIds);
        Task<Dictionary<Guid, DocumentSignatureIdsDTO>> GetLatestSignatureRecordIdsAsync(IEnumerable<Guid> documentIds);
    }
}
