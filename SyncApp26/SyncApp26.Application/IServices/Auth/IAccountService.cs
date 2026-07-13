using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Application.IServices
{
    public class AccountActionResult<T>
    {
        public bool Success { get; init; }
        public string? ErrorMessage { get; init; }
        public T? Data { get; init; }

        public static AccountActionResult<T> Ok(T data) => new() { Success = true, Data = data };
        public static AccountActionResult<T> Fail(string errorMessage) => new() { Success = false, ErrorMessage = errorMessage };
    }

    public enum EmailVerificationStatus
    {
        NotFound,
        InvalidToken,
        Verified
    }

    public class EmailVerificationResult
    {
        public EmailVerificationStatus Status { get; init; }
    }

    public enum LoginStatus
    {
        InvalidCredentials,
        EmailNotVerified,
        Success
    }

    public class LoginResult
    {
        public LoginStatus Status { get; init; }
        public string? Token { get; init; }
        public Guid UserId { get; init; }
        public string? Email { get; init; }
        public string? FirstName { get; init; }
        public string? LastName { get; init; }
        public UserRole Role { get; init; }
    }

    public class RegisteredAccountDTO
    {
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string EmailVerificationToken { get; init; } = string.Empty;
    }

    public class PasswordResetRequestedDTO
    {
        public string Email { get; init; } = string.Empty;
        public string FirstName { get; init; } = string.Empty;
        public string Token { get; init; } = string.Empty;
        public int ExpiresInMinutes { get; init; }
    }

    public interface IAccountService
    {
        Task<AccountActionResult<RegisteredAccountDTO>> RegisterAsync(RegisterUserRequestDTO request);
        Task<EmailVerificationResult> VerifyEmailAsync(string email, string token);
        Task<LoginResult> AuthenticateAsync(string email, string password);
        Task<AccountActionResult<PasswordResetRequestedDTO>> RequestPasswordResetAsync(string email);
        Task<AccountActionResult<bool>> ResetPasswordAsync(string email, string token, string newPassword);
    }
}
