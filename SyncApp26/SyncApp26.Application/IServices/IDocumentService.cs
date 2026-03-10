using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IDocumentService
    {
        Task<UserDocument> GenerateDocumentAsync(Guid userId, string documentType, string generatedByEmail);
        Task<IEnumerable<UserDocument>> GetUserDocumentsAsync(Guid userId);
        Task<IEnumerable<UserDocument>> GetAllPendingUserDocumentsAsync(string documentType);
        Task<UserDocument?> GetDocumentByIdAsync(Guid documentId);
        Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress);
        Task<int> BulkSignDocumentsAsync(bool isAdmin, Guid signerUserId, string signatureMethod, string signatureData, string ipAddress);
        Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail);
        Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document);
        Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document);
    }
}
