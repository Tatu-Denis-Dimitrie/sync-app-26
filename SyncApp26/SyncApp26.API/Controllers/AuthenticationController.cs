using Microsoft.AspNetCore.Mvc;
using SyncApp26.Domain.Entities;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.API.Services;

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

                var existingUser = await _userService.GetUserByEmailAsync(normalizedEmail);
                if (existingUser != null)
                {
                    return BadRequest(new { message = "Email is already registered." });
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

                return Ok(new { message = "Login successful.", token });
            }
            catch (Exception ex)
            {
                // Log the exception here if you have a logger
                return StatusCode(500, new { message = "An error occurred while processing your request.", error = ex.Message });
            }
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
    }
}