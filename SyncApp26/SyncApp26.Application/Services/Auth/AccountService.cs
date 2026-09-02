using System.Security.Cryptography;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging;
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
        private readonly IGoogleTokenValidator _googleTokenValidator;
        private readonly IMicrosoftTokenValidator _microsoftTokenValidator;
        private readonly ILogger<AccountService> _logger;
        private readonly IStringLocalizer _localizer;

        public AccountService(
            IUserService userService,
            IAuthenticationService authenticationService,
            ITokenService tokenService,
            IGoogleTokenValidator googleTokenValidator,
            IMicrosoftTokenValidator microsoftTokenValidator,
            ILogger<AccountService> logger,
            ILocalizationService localizationService)
        {
            _userService = userService;
            _authenticationService = authenticationService;
            _tokenService = tokenService;
            _googleTokenValidator = googleTokenValidator;
            _microsoftTokenValidator = microsoftTokenValidator;
            _logger = logger;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Auth);
        }

        private static string? ValidatePasswordFormat(string password, IStringLocalizer localizer)
        {
            if (password.Length < 8)
            {
                return localizer["validation.passwordMinLength"];
            }

            if (!Regex.IsMatch(password, @"[A-Z]"))
            {
                return localizer["validation.passwordUppercase"];
            }

            if (!Regex.IsMatch(password, @"[a-z]"))
            {
                return localizer["validation.passwordLowercase"];
            }

            if (!Regex.IsMatch(password, @"[0-9]"))
            {
                return localizer["validation.passwordDigit"];
            }

            if (!Regex.IsMatch(password, @"[!#$%&*^<>.,/?;_\-@]"))
            {
                return localizer["validation.passwordSpecialChar"];
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
                return AccountActionResult<RegisteredAccountDTO>.Fail(_localizer["validation.allFieldsRequired"]);
            }

            var normalizedEmail = request.Email.ToLowerInvariant().Trim();

            if (!Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail(_localizer["validation.invalidEmailFormat"]);
            }

            var passwordError = ValidatePasswordFormat(request.Password, _localizer);
            if (passwordError != null)
            {
                return AccountActionResult<RegisteredAccountDTO>.Fail(passwordError);
            }

            var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (existingUser != null)
            {
                // Same success shape as a real registration, just without creating anything or
                // sending a verification email - the caller must not be able to tell the two apart.
                return AccountActionResult<RegisteredAccountDTO>.Ok(new RegisteredAccountDTO
                {
                    Email = normalizedEmail,
                    FirstName = existingUser.FirstName,
                    AlreadyRegistered = true
                });
            }

            var user = new User
            {
                Id = Guid.NewGuid(),
                PersonalId = Guid.NewGuid().ToString(),
                FirstName = request.FirstName,
                LastName = request.LastName,
                Email = normalizedEmail,
                PasswordHash = await _authenticationService.HashPasswordAsync(request.Password),
                IsEmailVerified = false,
                EmailVerificationToken = Guid.NewGuid().ToString("N"),
                EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(24),
                CreatedAt = DateTime.UtcNow,
                PreferredLanguage = request.PreferredLanguage.HasValue && Enum.IsDefined(request.PreferredLanguage.Value)
                    ? request.PreferredLanguage
                    : null
            };

            var basicUserRole = await _userService.GetRoleByNameAsync(Roles.BasicUser);
            if (basicUserRole != null)
            {
                user.RoleAssignments.Add(new UserRoleAssignment { UserId = user.Id, RoleId = basicUserRole.Id });
            }

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
                _logger.LogWarning("Login failed for {Email}: invalid credentials.", normalizedEmail);
                return new LoginResult { Status = LoginStatus.InvalidCredentials };
            }

            if (user.IsEmailVerified != true)
            {
                _logger.LogWarning("Login failed for {Email}: email not verified.", normalizedEmail);
                return new LoginResult { Status = LoginStatus.EmailNotVerified };
            }

            var roleNames = user.RoleAssignments.Select(a => a.Role.Name).ToList();
            var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email, roleNames);

            _logger.LogInformation("Login succeeded for {Email}.", user.Email);

            return new LoginResult
            {
                Status = LoginStatus.Success,
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roleNames,
                PreferredLanguage = user.PreferredLanguage
            };
        }

        public async Task<LoginResult> AuthenticateWithGoogleAsync(string idToken)
        {
            var payload = await _googleTokenValidator.ValidateAsync(idToken);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogWarning("Google login failed: invalid or expired ID token.");
                return new LoginResult { Status = LoginStatus.InvalidCredentials };
            }

            if (!payload.EmailVerified)
            {
                _logger.LogWarning("Google login failed for {Email}: Google email not verified.", payload.Email);
                return new LoginResult { Status = LoginStatus.GoogleEmailNotVerified };
            }

            var normalizedEmail = payload.Email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            // Link-only: never creates an account. Deliberately no IsEmailVerified check -
            // CSV-synced and admin-created users leave it null, and Google's own
            // EmailVerified claim above covers it.
            if (user == null)
            {
                _logger.LogWarning("Google login failed: no account for {Email}.", normalizedEmail);
                return new LoginResult { Status = LoginStatus.NoAccountForEmail };
            }

            var roleNames = user.RoleAssignments.Select(a => a.Role.Name).ToList();
            var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email, roleNames);

            _logger.LogInformation("Login succeeded for {Email} via Google.", user.Email);

            return new LoginResult
            {
                Status = LoginStatus.Success,
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roleNames,
                PreferredLanguage = user.PreferredLanguage
            };
        }

        public async Task<LoginResult> AuthenticateWithMicrosoftAsync(string idToken)
        {
            var payload = await _microsoftTokenValidator.ValidateAsync(idToken);
            if (payload == null || string.IsNullOrWhiteSpace(payload.Email))
            {
                _logger.LogWarning("Microsoft login failed: invalid or expired ID token.");
                return new LoginResult { Status = LoginStatus.InvalidCredentials };
            }

            // Link-only, same as Google. Microsoft has no email-verified claim, but an
            // unmatched email is always rejected, so a wrong claim can only fail closed.
            var normalizedEmail = payload.Email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if (user == null)
            {
                _logger.LogWarning("Microsoft login failed: no account for {Email}.", normalizedEmail);
                return new LoginResult { Status = LoginStatus.NoAccountForEmail };
            }

            var roleNames = user.RoleAssignments.Select(a => a.Role.Name).ToList();
            var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email, roleNames);

            _logger.LogInformation("Login succeeded for {Email} via Microsoft.", user.Email);

            return new LoginResult
            {
                Status = LoginStatus.Success,
                Token = token,
                UserId = user.Id,
                Email = user.Email,
                FirstName = user.FirstName,
                LastName = user.LastName,
                Roles = roleNames,
                PreferredLanguage = user.PreferredLanguage
            };
        }

        public async Task<AccountActionResult<PasswordResetRequestedDTO>> RequestPasswordResetAsync(string email)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if (user == null)
            {
                _logger.LogWarning("Password reset requested for unknown email {Email}.", normalizedEmail);
                return AccountActionResult<PasswordResetRequestedDTO>.Fail(_localizer["validation.emailNotRegistered"]);
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
            var passwordError = ValidatePasswordFormat(newPassword, _localizer);
            if (passwordError != null)
            {
                return AccountActionResult<bool>.Fail(passwordError);
            }

            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (user == null)
            {
                // Server log keeps the real reason distinct from the deliberately generic client
                // message above, which never reveals whether the email has an account.
                _logger.LogWarning("Password reset failed: unknown email {Email}.", normalizedEmail);
                return AccountActionResult<bool>.Fail(_localizer["validation.invalidOrExpiredToken"]);
            }

            var verifyPassword = user.PasswordHash != null && await _authenticationService.VerifyPasswordAsync(newPassword, user.PasswordHash);
            if (verifyPassword)
            {
                return AccountActionResult<bool>.Fail(_localizer["validation.passwordSameAsOld"]);
            }

            var providedToken = token.Trim();
            if (string.IsNullOrWhiteSpace(user.PasswordResetToken) ||
                user.PasswordResetTokenExpiresAt == null ||
                user.PasswordResetTokenExpiresAt < DateTime.UtcNow ||
                !string.Equals(user.PasswordResetToken, providedToken, StringComparison.Ordinal))
            {
                _logger.LogWarning("Password reset failed for {Email}: invalid or expired token.", normalizedEmail);
                return AccountActionResult<bool>.Fail(_localizer["validation.invalidOrExpiredToken"]);
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
