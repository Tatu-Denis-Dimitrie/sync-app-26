using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public interface IDocumentSignatureService
    {
        Task<string> GenerateSignatureTokenAsync(string email, Guid documentId, string documentName);
        Task<DocumentSignatureToken?> ValidateTokenAsync(string token);
        Task<bool> ConsumeTokenAsync(string token);
    }
}
