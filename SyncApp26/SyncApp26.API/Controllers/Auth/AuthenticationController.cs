using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using SyncApp26.Application.IServices;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.API.Services;
using SyncApp26.API.Extensions;

namespace SyncApp26.API.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class AuthenticationController : ControllerBase
    {
        // Just a hint to the browser - the JWT's own exp claim is what's actually enforced.
        private static readonly TimeSpan AccessTokenCookieLifetime = TimeSpan.FromMinutes(15);

        // The session's absolute cap - rotation never extends past what IssueAsync was given.
        private static readonly TimeSpan RefreshTokenLifetime = TimeSpan.FromHours(8);

        private readonly IAccountService _accountService;
        private readonly IEmailService _emailService;
        private readonly IConfiguration _configuration;
        private readonly AuthCookieOptions _authCookieOptions;
        private readonly IRefreshTokenService _refreshTokenService;
        private readonly ILogger<AuthenticationController> _logger;

        public AuthenticationController(
            IAccountService accountService,
            IEmailService emailService,
            IConfiguration configuration,
            AuthCookieOptions authCookieOptions,
            IRefreshTokenService refreshTokenService,
            ILogger<AuthenticationController> logger)
        {
            _accountService = accountService;
            _emailService = emailService;
            _configuration = configuration;
            _authCookieOptions = authCookieOptions;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
        }

        [HttpPost("register")]
        [EnableRateLimiting("auth-sensitive")]
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
                if (registered.AlreadyRegistered)
                {
                    return Ok(new { message = "Registration successful. Check your email to verify your account." });
                }

                var apiBaseUrl = $"{Request.Scheme}://{Request.Host}";
                var verifyUrl = $"{apiBaseUrl}/api/authentication/verify-email?email={Uri.EscapeDataString(registered.Email)}&token={Uri.EscapeDataString(registered.EmailVerificationToken)}";

                try
                {
                    await _emailService.SendVerificationEmailAsync(registered.Email, registered.FirstName, verifyUrl);
                }
                catch (Exception emailEx)
                {
                    // User is saved; just warn that email delivery failed.
                    _logger.LogWarning(emailEx, "Registration succeeded for {Email} but the verification email failed to send.", registered.Email);
                    return StatusCode(202, new { message = "Account created, but we could not send the verification email. Please contact an administrator.", error = emailEx.Message });
                }

                return Ok(new { message = "Registration successful. Check your email to verify your account." });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for {Email}.", request?.Email);
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpGet("verify-email")]
        [EnableRateLimiting("auth-sensitive")]
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
        [EnableRateLimiting("login")]
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
                    _ => await LoginSuccess(result)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for {Email}.", request?.Email);
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpPost("google-login")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> GoogleLogin([FromBody] GoogleLoginRequestDTO request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
                {
                    return BadRequest(new { message = "Google ID token is required." });
                }

                var result = await _accountService.AuthenticateWithGoogleAsync(request.IdToken);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = "Invalid or expired Google sign-in. Please try again." }),
                    LoginStatus.GoogleEmailNotVerified => Unauthorized(new { message = "Your Google account email is not verified. Verify it with Google and try again." }),
                    LoginStatus.NoAccountForEmail => Unauthorized(new { message = "No SyncApp26 account exists for this Google email. Contact an administrator." }),
                    LoginStatus.Success => await LoginSuccess(result),
                    // Explicit Success so a new status can't fall through as a 200 with no token.
                    _ => StatusCode(500, new { message = "An error occurred while processing your request." })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed.");
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpPost("microsoft-login")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> MicrosoftLogin([FromBody] MicrosoftLoginRequestDTO request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.IdToken))
                {
                    return BadRequest(new { message = "Microsoft ID token is required." });
                }

                var result = await _accountService.AuthenticateWithMicrosoftAsync(request.IdToken);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = "Invalid or expired Microsoft sign-in. Please try again." }),
                    LoginStatus.NoAccountForEmail => Unauthorized(new { message = "No SyncApp26 account exists for this Microsoft email. Contact an administrator." }),
                    LoginStatus.Success => await LoginSuccess(result),
                    // Explicit Success so a new status can't fall through as a 200 with no token.
                    _ => StatusCode(500, new { message = "An error occurred while processing your request." })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Microsoft login failed.");
                return StatusCode(500, new { message = "An error occurred while processing your request." });
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = "Email is required." });
            }

            var result = await _accountService.RequestPasswordResetAsync(request.Email);
            if (result.Success)
            {
                var reset = result.Data!;
                var resetUrl = BuildResetPasswordUrl(reset.Email, reset.Token);

                await _emailService.SendPasswordResetEmailAsync(
                    reset.Email,
                    reset.FirstName,
                    resetUrl,
                    reset.ExpiresInMinutes);
            }

            // Same response whether or not the account exists, so the caller can't tell.
            return Ok(new { message = "A reset link has been sent to your email." });
        }

        [HttpPost("reset-password")]
        [EnableRateLimiting("auth-sensitive")]
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

        // Shared by login/google-login/microsoft-login. No XSRF-TOKEN cookie here - User is still
        // anonymous at this point in the request, so a token minted now would bind to the wrong
        // identity. SessionController.Me() issues it correctly on the client's next request.
        private async Task<IActionResult> LoginSuccess(LoginResult result)
        {
            Response.AppendAuthCookie(_authCookieOptions, result.Token!, AccessTokenCookieLifetime);

            var refreshToken = await _refreshTokenService.IssueAsync(result.UserId, DateTime.UtcNow.Add(RefreshTokenLifetime));
            Response.AppendRefreshCookie(_authCookieOptions, refreshToken.RawToken, refreshToken.ExpiresAt);

            return Ok(new
            {
                message = "Login successful.",
                token = result.Token,
                user = new
                {
                    id = result.UserId,
                    email = result.Email,
                    firstName = result.FirstName,
                    lastName = result.LastName,
                    roles = result.Roles
                }
            });
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
