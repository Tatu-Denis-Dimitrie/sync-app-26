namespace SyncApp26.API.Services
{
    public interface IEmailService
    {
        Task SendEmailAsync(string toEmail, string subject, string htmlBody);
        Task SendVerificationEmailAsync(string toEmail, string firstName, string verifyUrl);
        Task SendDocumentSignatureEmailWithLinkAsync(string toEmail, string documentName, string secureLink);
        Task SendDocumentSignatureEmailForRegisteredUserAsync(string toEmail, string documentName, string loginLink);
    }
}
