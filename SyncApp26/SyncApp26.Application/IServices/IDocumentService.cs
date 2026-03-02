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
        Task<UserDocument?> GetDocumentByIdAsync(Guid documentId);
        Task<bool> UpdateDocumentSignatureAsync(Guid documentId, bool isUserSignature, string signatureMethod, string signatureData, string ipAddress);
        Task<string> GeneratePdfSnapshotAsync(User user, UserDocument document);
        Task<byte[]> GeneratePdfBytesAsync(User user, UserDocument document);
    }
}
