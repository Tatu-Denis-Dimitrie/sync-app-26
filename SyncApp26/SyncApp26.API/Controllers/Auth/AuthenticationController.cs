using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.API.Services;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        private readonly IAccountService _accountService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;

        public AuthenticationController(
            IAccountService accountService,
            IEmailService emailService,
            IConfiguration configuration)
        {
            _accountService = accountService;
            _emailService = emailService;
            _configuration = configuration;
        }

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterUserRequestDTO request)
        {
            try
            {
                var result = await _accountService.RegisterAsync(request);
                if (!result.Success)
                {
                    return BadRequest(new { message = result.ErrorMessage });
                }

                var registered = result.Data!;
                var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
                var verifyUrl = $"{apiBaseUrl}/api/authentication/verify-email?email={Uri.EscapeDataString(registered.Email)}&token={Uri.EscapeDataString(registered.EmailVerificationToken)}";

                try
                {
                    await _emailService.SendVerificationEmailAsync(registered.Email, registered.FirstName, verifyUrl);
                }
                catch (Exception emailEx)
                {
                    // User is saved; just warn that email delivery failed.
                    return StatusCode(202, new { message = "Account created, but we could not send the verification email. Please contact an administrator.", error = emailEx.Message });
                }

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

            var result = await _accountService.VerifyEmailAsync(email, token);

            return result.Status switch
            {
                EmailVerificationStatus.NotFound => NotFound(new { message = "User not found." }),
                EmailVerificationStatus.InvalidToken => BadRequest(new { message = "Verification token is invalid or expired." }),
                _ => Redirect(GetLoginRedirectUrl())
            };
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

                var result = await _accountService.AuthenticateAsync(request.Email, request.Password);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = "Invalid email or password." }),
                    LoginStatus.EmailNotVerified => Unauthorized(new { message = "Email is not verified. Please check your email for verification instructions." }),
                    _ => Ok(new
                    {
                        message = "Login successful.",
                        token = result.Token,
                        user = new
                        {
                            id = result.UserId,
                            email = result.Email,
                            firstName = result.FirstName,
                            lastName = result.LastName,
                            role = result.Role
                        }
                    })
                };
            }
            catch (Exception ex)
            {
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

            var result = await _accountService.RequestPasswordResetAsync(request.Email);
            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            var reset = result.Data!;
            var resetUrl = BuildResetPasswordUrl(reset.Email, reset.Token);

            await _emailService.SendPasswordResetEmailAsync(
                reset.Email,
                reset.FirstName,
                resetUrl,
                reset.ExpiresInMinutes);

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

            var result = await _accountService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

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
