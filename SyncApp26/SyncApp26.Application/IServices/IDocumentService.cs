using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IDocumentService
    {
        Task<UserDocument> GenerateDocumentAsync(Guid userId, string documentType, string generatedByEmail);
        Task<HashSet<Guid>> GetUserIdsWithDocumentTypeAsync(string documentType);
        Task<IEnumerable<UserDocument>> GetUserDocumentsAsync(Guid userId);
        Task<IEnumerable<UserDocument>> GetAllPendingUserDocumentsAsync(string documentType);
        Task<IEnumerable<UserDocument>> GetAllDocumentsAsync();
        Task<UserDocument?> GetDocumentByIdAsync(Guid documentId);
        Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress, bool isAdminSignature = false);
        Task<int> BulkSignDocumentsAsync(bool isAdmin, Guid signerUserId, string signatureMethod, string signatureData, string ipAddress);
        Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail);
        Task<int> BulkSignAndSendGeneratedDocumentsAsync(string documentType, string signatureMethod, string signatureData, string ipAddress);
        Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document);
        Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document);
        Task<int> GetPendingSsmDocumentsForAdminAsync();
        Task<List<UserDocument>> GetPendingSsmDocumentsForAdminListAsync();
        Task SignSingleDocumentAsAdminAsync(UserDocument doc, string signatureMethod, string signatureData, string ipAddress);
    }
}
