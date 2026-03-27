namespace SyncApp26.API.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
        Task SendVerificationEmailAsync(string toEmail, string firstName, string verifyUrl);
        Task SendPasswordResetEmailAsync(string toEmail, string firstName, string resetUrl, int expirationMinutes);
        Task SendDocumentSignatureEmailWithLinkAsync(string toEmail, string documentName, string secureLink);
        Task SendDocumentSignatureEmailForRegisteredUserAsync(string toEmail, string documentName, string loginLink);
        Task SendMissingSignatureToUserEmailAsync(string toEmail, string firstName, string documentName, DateTime? trainingDate, string? signLink = null);
        Task SendMissingSignatureToManagerEmailAsync(string toEmail, string managerName, string documentName, int unsignedCount);
    }
}
