using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.IServices
{
    public class SigningTokenResult
    {
        public bool Forbidden { get; init; }
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public string? Token { get; init; }
    }

    public class SigningContextResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public Guid DocumentId { get; init; }
        public string? DocumentName { get; init; }
        public string? Email { get; init; }
        public string? DocumentType { get; init; }
        public bool IsManagerSigning { get; init; }
        public bool IsInstructorSigning { get; init; }
        public bool IsAdminSigning { get; init; }
        public Guid? PeriodicTrainingId { get; init; }
    }

    public class ConsumeSigningTokenRequest
    {
        public string Token { get; init; } = string.Empty;
        public string SignatureMethod { get; init; } = string.Empty;
        public string SignatureData { get; init; } = string.Empty;
        public bool BulkSign { get; init; }
        public Guid? PeriodicTrainingId { get; init; }
        public string IpAddress { get; init; } = "Unknown";
    }

    public class SigningNotification
    {
        public string Email { get; init; } = string.Empty;
        public string Token { get; init; } = string.Empty;
        public string DocumentName { get; init; } = string.Empty;
    }

    public class ConsumeSigningTokenResult
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public int TotalSigned { get; init; }
        public List<SigningNotification> NextSignerNotifications { get; init; } = new();
    }

    public interface IDocumentSigningService
    {
        Task<SigningTokenResult> RequestSigningTokenAsync(UserDocument document, User caller);
        Task<SigningContextResult> GetSigningContextAsync(string token);
        Task<ConsumeSigningTokenResult> ConsumeSigningTokenAsync(ConsumeSigningTokenRequest request);
    }
}
