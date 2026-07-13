using System.Security.Cryptography;
using System.Text.RegularExpressions;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Application.Services
{
    public class AccountService : IAccountService
    {
        private const int PasswordResetExpiryMinutes = 30;

        private readonly IUserService _userService;
        private readonly IAuthenticationService _authenticationService;
        private readonly ITokenService _tokenService;

        public AccountService(IUserService userService, IAuthenticationService authenticationService, ITokenService tokenService)
        {
            _userService = userService;
            _authenticationService = authenticationService;
            _tokenService = tokenService;
        }

        private static string? ValidatePasswordFormat(string password)
        {
            if (password.Length < 8)
            {
                return "Password must be at least 8 characters long.";
            }

            if (!Regex.IsMatch(password, @"[A-Z]"))
            {
                return "Password must contain at least one uppercase letter.";
            }

            if (!Regex.IsMatch(password, @"[a-z]"))
            {
                return "Password must contain at least one lowercase letter.";
            }

            if (!Regex.IsMatch(password, @"[0-9]"))
            {
                return "Password must contain at least one digit.";
            }

            if (!Regex.IsMatch(password, @"[!#$%&*^<>.,/?;_\-@]"))
            {
                return "Password must contain at least one special character.";
            }

            return null;
        }

        public async Task<AccountActionResult<RegisteredAccountDTO>> RegisterAsync(RegisterUserRequestDTO request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.FirstName) ||
                string.IsNullOrWhiteSpace(request.LastName) ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Password))
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail("All fields are required.");
            }

            var normalizedEmail = request.Email.ToLowerInvariant().Trim();

            if (!Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail("Invalid email format.");
            }

            var passwordError = ValidatePasswordFormat(request.Password);
            if (passwordError != null)
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail(passwordError);
            }

            var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (existingUser != null)
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail("Email is already registered.");
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = Guid.NewGuid().ToString(),
                Role = UserRole.BasicUser,
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = normalizedEmail,
                PasswordHash = await _authenticationService.HashPasswordAsync(request.Password),
                IsEmailVerified = false,
                EmailVerificationToken = Guid.NewGuid().ToString("N"),
                EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow
            };

            await _userService.AddUserAsync(user);

            return AccountActionResult<RegisteredAccountDTO>.Ok(new RegisteredAccountDTO
            {
                Email = user.Email,
                FirstName = user.FirstName,
                EmailVerificationToken = user.EmailVerificationToken!
            });
        }

        public async Task<EmailVerificationResult> VerifyEmailAsync(string email, string token)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if (user == null)
            {
                return new EmailVerificationResult { Status = EmailVerificationStatus.NotFound };
            }

            if (user.IsEmailVerified == true)
            {
                return new EmailVerificationResult { Status = EmailVerificationStatus.Verified };
            }

            if (string.IsNullOrWhiteSpace(user.EmailVerificationToken) ||
                user.EmailVerificationTokenExpiresAt == null ||
                user.EmailVerificationTokenExpiresAt < DateTime.UtcNow ||
                !string.Equals(user.EmailVerificationToken, token, StringComparison.Ordinal))
            {
                return new EmailVerificationResult { Status = EmailVerificationStatus.InvalidToken };
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return new EmailVerificationResult { Status = EmailVerificationStatus.Verified };
        }

        public async Task<LoginResult> AuthenticateAsync(string email, string password)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (user == null || user.PasswordHash == null || !await _authenticationService.VerifyPasswordAsync(password, user.PasswordHash))
            {
                return new LoginResult { Status = LoginStatus.InvalidCredentials };
            }

            if (user.IsEmailVerified != true)
            {
                return new LoginResult { Status = LoginStatus.EmailNotVerified };
            }

            var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email, user.Role);

            return new LoginResult
            {
                Status = LoginStatus.Success,
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Role = user.Role
            };
        }

        public async Task<AccountActionResult<PasswordResetRequestedDTO>> RequestPasswordResetAsync(string email)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if (user == null)
            {
                return AccountActionResult<PasswordResetRequestedDTO>.Fail("This email doesn't have an account.");
            }

            var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
            user.PasswordResetToken = token;
            user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(PasswordResetExpiryMinutes);
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return AccountActionResult<PasswordResetRequestedDTO>.Ok(new PasswordResetRequestedDTO
            {
                Email = user.Email,
                FirstName = user.FirstName,
                Token = token,
                ExpiresInMinutes = PasswordResetExpiryMinutes
            });
        }

        public async Task<AccountActionResult<bool>> ResetPasswordAsync(string email, string token, string newPassword)
        {
            var passwordError = ValidatePasswordFormat(newPassword);
            if (passwordError != null)
            {
                return AccountActionResult<bool>.Fail(passwordError);
            }

            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (user == null)
            {
                return AccountActionResult<bool>.Fail("Invalid or expired token.");
            }

            var verifyPassword = await _authenticationService.VerifyPasswordAsync(newPassword, user.PasswordHash!);
            if (verifyPassword)
            {
                return AccountActionResult<bool>.Fail("New password cannot be the same as the old password.");
            }

            var providedToken = token.Trim();
            if (string.IsNullOrWhiteSpace(user.PasswordResetToken) ||
                user.PasswordResetTokenExpiresAt == null ||
                user.PasswordResetTokenExpiresAt < DateTime.UtcNow ||
                !string.Equals(user.PasswordResetToken, providedToken, StringComparison.Ordinal))
            {
                return AccountActionResult<bool>.Fail("Invalid or expired token.");
            }

            user.PasswordHash = await _authenticationService.HashPasswordAsync(newPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return AccountActionResult<bool>.Ok(true);
        }
    }
}
