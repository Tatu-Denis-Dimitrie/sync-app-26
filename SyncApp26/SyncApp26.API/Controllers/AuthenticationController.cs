using Microsoft.AspNetCore.Mvc;
using SyncApp26.Domain.Entities;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.API.Services;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IUserService _userService;
        private readonly ITokenService _tokenService;
        private readonly IAuthenticationService _authenticationService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        public AuthenticationController(
            IUserService userService,
            ITokenService tokenService,
            IAuthenticationService authenticationService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _userService = userService;
            _tokenService = tokenService;
            _authenticationService = authenticationService;
            _emailService = emailService;
            _configuration = configuration;
        }

        private async Task<IActionResult> VerifyPasswordFormat(string password)
        {
            if(password.Length < 8)
            {
                return BadRequest(new { message = "Password must be at least 8 characters long." });
            }

            if(!Regex.IsMatch(password, @"[A-Z]"))
            {
                return BadRequest(new { message = "Password must contain at least one uppercase letter." });
            }

            if (!Regex.IsMatch(password, @"[a-z]"))
            {
                return BadRequest(new { message = "Password must contain at least one lowercase letter." });
            }

            if (!Regex.IsMatch(password, @"[0-9]"))
            {
                return BadRequest(new { message = "Password must contain at least one digit." });
            }

            if(!Regex.IsMatch(password, @"[!#$%&*.,/?;_-]"))
            {
                return BadRequest(new { message = "Password must contain at least one special character."});
            }

            return Ok();
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserRequestDTO request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.FirstName) ||
                    string.IsNullOrWhiteSpace(request.LastName) ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "All fields are required." });
                }

                // Normalize email to lowercase for consistent checking
                var normalizedEmail = request.Email.ToLowerInvariant().Trim();

                if (!Regex.IsMatch(normalizedEmail, @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                {
                    return BadRequest(new { message = "Invalid email format." });
                }

                if (!normalizedEmail.EndsWith("@gmail.com", StringComparison.OrdinalIgnoreCase))
                {
                    return BadRequest(new { message = "Email must be a @gmail.com address." });
                }

                var result = await VerifyPasswordFormat(request.Password);
                if(result is BadRequestObjectResult badRequest)
                {
                    return badRequest;
                }

                var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "Email is already registered." });
                }

                var basicUserRoleId = await _userService.GetRoleIdByNameAsync("Basic User");
                if (basicUserRoleId == null)
                {
                    return StatusCode(500, new { message = "Required role 'Basic User' is missing." });
                }

                var user = new User
                {
                    Id = Guid.NewGuid(),
                    PersonalId = Guid.NewGuid().ToString(),
                    RoleId = basicUserRoleId.Value,
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

                var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
                var verifyUrl = $"{apiBaseUrl}/api/authentication/verify-email?email={Uri.EscapeDataString(user.Email)}&token={Uri.EscapeDataString(user.EmailVerificationToken!)}";

                await _emailService.SendVerificationEmailAsync(user.Email, user.FirstName, verifyUrl);

                return Ok(new { message = "Registration successful. Please check your email to verify your account." });
            }
            catch (Exception ex)
            {
                // Log the exception here if you have a logger
                return StatusCode(500, new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpGet("verify-email")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { message = "Invalid verification link." });
            }

            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if (user == null)
            {
                return NotFound(new { message = "User not found." });
            }

            if (user.IsEmailVerified == true)
            {
                return Redirect(GetLoginRedirectUrl());
            }

            if (string.IsNullOrWhiteSpace(user.EmailVerificationToken) ||
                user.EmailVerificationTokenExpiresAt == null ||
                user.EmailVerificationTokenExpiresAt < DateTime.UtcNow ||
                !string.Equals(user.EmailVerificationToken, token, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Verification token is invalid or expired." });
            }

            user.IsEmailVerified = true;
            user.EmailVerificationToken = null;
            user.EmailVerificationTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Redirect(GetLoginRedirectUrl());
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginUserRequestDTO request)
        {
            try
            {
                if (request == null ||
                    string.IsNullOrWhiteSpace(request.Email) ||
                    string.IsNullOrWhiteSpace(request.Password))
                {
                    return BadRequest(new { message = "Email and password are required." });
                }

                var normalizedEmail = request.Email.ToLowerInvariant().Trim();
                var user = await _userService.GetUserByEmailAsync(normalizedEmail);
                if (user == null || user.PasswordHash == null || !await _authenticationService.VerifyPasswordAsync(request.Password, user.PasswordHash))
                {
                    return Unauthorized(new { message = "Invalid email or password." });
                }

                if (user.IsEmailVerified != true)
                {
                    return Unauthorized(new { message = "Email is not verified. Please check your email for verification instructions." });
                }

                var token = await _tokenService.GenerateTokenAsync(user.Id, user.Email);

                return Ok(new
                {
                    message = "Login successful.",
                    token,
                    user = new
                    {
                        id = user.Id,
                        email = user.Email,
                        firstName = user.FirstName,
                        lastName = user.LastName,
                        role = user.Role?.Name ?? "Basic User"
                    }
                });
            }
            catch (Exception ex)
            {
                // Log the exception here if you have a logger
                return StatusCode(500, new { message = "An error occurred while processing your request.", error = ex.Message });
            }
        }

        [HttpPost("forgot-password")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var normalizedEmail = request.Email.ToLowerInvariant().Trim();
            var user = await _userService.GetUserByEmailAsync(normalizedEmail);

            if(user == null)
            {
                return BadRequest(new { message = "This email doesn't have an account." });
            }

            if (user != null)
            {
                var token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
                user.PasswordResetToken = token;
                user.PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(30);
                user.UpdatedAt = DateTime.UtcNow;

                await _userService.UpdateUserAsync(user);

                var resetUrl = BuildResetPasswordUrl(user.Email, token);

                await _emailService.SendPasswordResetEmailAsync(
                    user.Email,
                    user.FirstName,
                    resetUrl,
                    30);
            }

            return Ok(new { message = "A password reset link has been sent." });
        }

        [HttpPost("reset-password")]
        public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordWithTokenRequestDTO request)
        {
            if (request == null ||
                string.IsNullOrWhiteSpace(request.Email) ||
                string.IsNullOrWhiteSpace(request.Token) ||
                string.IsNullOrWhiteSpace(request.NewPassword))
            {
                return BadRequest(new { message = "Email, token and new password are required." });
            }

            var result = await VerifyPasswordFormat(request.NewPassword);
            if(result is BadRequestObjectResult badRequest)
            {
                return badRequest;
            }

            var normalizedEmail = request.Email.ToLowerInvariant().Trim();

            var user = await _userService.GetUserByEmailAsync(normalizedEmail);
            if (user == null)
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            var verifyPassword = await _authenticationService.VerifyPasswordAsync(request.NewPassword, user.PasswordHash!);
            if (verifyPassword)            
            {
                return BadRequest(new { message = "New password cannot be the same as the old password." });
            }

            var providedToken = request.Token.Trim();
            if (string.IsNullOrWhiteSpace(user.PasswordResetToken) ||
                user.PasswordResetTokenExpiresAt == null ||
                user.PasswordResetTokenExpiresAt < DateTime.UtcNow ||
                !string.Equals(user.PasswordResetToken, providedToken, StringComparison.Ordinal))
            {
                return BadRequest(new { message = "Invalid or expired token." });
            }

            user.PasswordHash = await _authenticationService.HashPasswordAsync(request.NewPassword);
            user.PasswordResetToken = null;
            user.PasswordResetTokenExpiresAt = null;
            user.UpdatedAt = DateTime.UtcNow;

            await _userService.UpdateUserAsync(user);

            return Ok(new { message = "Password reset successfully." });
        }

        private string GetLoginRedirectUrl()
        {
            var loginUrl = _configuration["Frontend:LoginUrl"];
            if (string.IsNullOrWhiteSpace(loginUrl))
            {
                return "http://localhost:4200/login";
            }

            return loginUrl;
        }

        private string BuildResetPasswordUrl(string email, string token)
        {
            var configuredResetUrl = _configuration["Frontend:ResetPasswordUrl"];
            var resetBaseUrl = string.IsNullOrWhiteSpace(configuredResetUrl)
                ? "http://localhost:4200/reset-password"
                : configuredResetUrl;

            var separator = resetBaseUrl.Contains('?') ? "&" : "?";
            return $"{resetBaseUrl}{separator}email={Uri.EscapeDataString(email)}&token={Uri.EscapeDataString(token)}";
        }
    }
}