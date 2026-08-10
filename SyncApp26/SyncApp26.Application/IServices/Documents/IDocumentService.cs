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
        Task<HashSet<Guid>> GetUserIdsWithUnsignedDocumentTypeAsync(string documentType);
        Task<IEnumerable<UserDocument>> GetUserDocumentsAsync(Guid userId);
        Task<IEnumerable<UserDocument>> GetAllPendingUserDocumentsAsync(string documentType);
        Task<IEnumerable<UserDocument>> GetAllDocumentsAsync();
        Task<UserDocument?> GetDocumentByIdAsync(Guid documentId);
        Task<bool> UpdateDocumentSignatureAsync(Guid documentId, Guid signerUserId, string signerRole, string signatureMethod, string signatureData, string ipAddress, Guid? periodicTrainingId = null);
        Task<Guid?> GetCurrentTrainingIdForDocumentAsync(Guid documentId);
        Task<int> BulkSignDocumentsAsync(Guid signerUserId, string signatureMethod, string signatureData, string ipAddress);
        Task<(int generated, int skipped)> BulkGenerateDocumentsAsync(string documentType, string generatedByEmail, List<Guid>? selectedUserIds = null, Guid? restrictToAssignedToId = null);
        Task<IEnumerable<UserDocument>> GetManagerPendingSignaturesAsync(Guid managerId);
        Task<IEnumerable<UserDocument>> GetManagerSignedDocumentsAsync(Guid managerId);
        Task<IEnumerable<UserDocument>> GetInstructorPendingSignaturesAsync(Guid instructorId);
        Task<IEnumerable<UserDocument>> GetInstructorSignedDocumentsAsync(Guid instructorId);
        Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document);
        Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document, bool viewerIsAdmin = false);
        Task<int> GetPendingDocumentsForOfficerAsync(string documentType);
        Task<List<UserDocument>> GetPendingDocumentsForOfficerListAsync(string documentType);
        Task SignSingleDocumentAsOfficerAsync(UserDocument doc, Guid signerUserId, string signatureMethod, string signatureData, string ipAddress);
        Task<List<UserDocument>> GetAdminPendingDocumentsAsync();
        Task<List<UserDocument>> GetAdminSignedDocumentsAsync();
        Task<int> RegenerateDocumentsAsync();
        Task<bool> DeleteDocumentAsync(Guid documentId);
        Task<int> BackfillSignatureRecordVersionsAsync();
    }
}
