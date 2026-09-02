using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
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
        private readonly IStringLocalizer _localizer;

        public AuthenticationController(
            IAccountService accountService,
            IEmailService emailService,
            IConfiguration configuration,
            AuthCookieOptions authCookieOptions,
            IRefreshTokenService refreshTokenService,
            ILogger<AuthenticationController> logger,
            ILocalizationService localizationService)
        {
            _accountService = accountService;
            _emailService = emailService;
            _configuration = configuration;
            _authCookieOptions = authCookieOptions;
            _refreshTokenService = refreshTokenService;
            _logger = logger;
            _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Auth);
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
                    return Ok(new { message = _localizer["api.registrationSuccessful"].Value });
                }

                // Server-side config, not Request.Host - avoids Host header injection.
                var configuredApiBaseUrl = _configuration["Api:BaseUrl"];
                var apiBaseUrl = string.IsNullOrWhiteSpace(configuredApiBaseUrl) ? "http://localhost:5022" : configuredApiBaseUrl;
                var verifyUrl = $"{apiBaseUrl}/api/authentication/verify-email?email={Uri.EscapeDataString(registered.Email)}&token={Uri.EscapeDataString(registered.EmailVerificationToken)}";

                try
                {
                    await _emailService.SendVerificationEmailAsync(registered.Email, registered.FirstName, verifyUrl);
                }
                catch (Exception emailEx)
                {
                    // User is saved; just warn that email delivery failed.
                    _logger.LogWarning(emailEx, "Registration succeeded for {Email} but the verification email failed to send.", registered.Email);
                    return StatusCode(202, new { message = _localizer["api.registrationEmailFailed"].Value, error = emailEx.Message });
                }

                return Ok(new { message = _localizer["api.registrationSuccessful"].Value });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Registration failed for {Email}.", request?.Email);
                return StatusCode(500, new { message = _localizer["api.genericError"].Value });
            }
        }

        [HttpGet("verify-email")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> VerifyEmail([FromQuery] string email, [FromQuery] string token)
        {
            if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(token))
            {
                return BadRequest(new { message = _localizer["api.invalidVerificationLink"].Value });
            }

            var result = await _accountService.VerifyEmailAsync(email, token);

            return result.Status switch
            {
                EmailVerificationStatus.NotFound => NotFound(new { message = _localizer["api.userNotFound"].Value }),
                EmailVerificationStatus.InvalidToken => BadRequest(new { message = _localizer["api.verificationTokenInvalid"].Value }),
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
                    return BadRequest(new { message = _localizer["api.emailAndPasswordRequired"].Value });
                }

                var result = await _accountService.AuthenticateAsync(request.Email, request.Password);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = _localizer["api.invalidCredentials"].Value }),
                    LoginStatus.EmailNotVerified => Unauthorized(new { message = _localizer["api.emailNotVerified"].Value }),
                    _ => await LoginSuccess(result)
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Login failed for {Email}.", request?.Email);
                return StatusCode(500, new { message = _localizer["api.genericError"].Value });
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
                    return BadRequest(new { message = _localizer["api.googleTokenRequired"].Value });
                }

                var result = await _accountService.AuthenticateWithGoogleAsync(request.IdToken);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = _localizer["api.googleSignInInvalid"].Value }),
                    LoginStatus.GoogleEmailNotVerified => Unauthorized(new { message = _localizer["api.googleEmailNotVerified"].Value }),
                    LoginStatus.NoAccountForEmail => Unauthorized(new { message = _localizer["api.noAccountForGoogleEmail"].Value }),
                    LoginStatus.Success => await LoginSuccess(result),
                    // Explicit Success so a new status can't fall through as a 200 with no token.
                    _ => StatusCode(500, new { message = _localizer["api.genericError"].Value })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Google login failed.");
                return StatusCode(500, new { message = _localizer["api.genericError"].Value });
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
                    return BadRequest(new { message = _localizer["api.microsoftTokenRequired"].Value });
                }

                var result = await _accountService.AuthenticateWithMicrosoftAsync(request.IdToken);

                return result.Status switch
                {
                    LoginStatus.InvalidCredentials => Unauthorized(new { message = _localizer["api.microsoftSignInInvalid"].Value }),
                    LoginStatus.NoAccountForEmail => Unauthorized(new { message = _localizer["api.noAccountForMicrosoftEmail"].Value }),
                    LoginStatus.Success => await LoginSuccess(result),
                    // Explicit Success so a new status can't fall through as a 200 with no token.
                    _ => StatusCode(500, new { message = _localizer["api.genericError"].Value })
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Microsoft login failed.");
                return StatusCode(500, new { message = _localizer["api.genericError"].Value });
            }
        }

        [HttpPost("forgot-password")]
        [EnableRateLimiting("auth-sensitive")]
        public async Task<IActionResult> ForgotPassword([FromBody] ForgotPasswordRequestDTO request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Email))
            {
                return BadRequest(new { message = _localizer["api.emailRequired"].Value });
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
            return Ok(new { message = _localizer["api.resetLinkSent"].Value });
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
                return BadRequest(new { message = _localizer["api.resetFieldsRequired"].Value });
            }

            var result = await _accountService.ResetPasswordAsync(request.Email, request.Token, request.NewPassword);
            if (!result.Success)
            {
                return BadRequest(new { message = result.ErrorMessage });
            }

            return Ok(new { message = _localizer["api.passwordResetSuccessful"].Value });
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
                message = _localizer["api.loginSuccessful"].Value,
                user = new
                {
                    id = result.UserId,
                    email = result.Email,
                    firstName = result.FirstName,
                    lastName = result.LastName,
                    roles = result.Roles,
                    preferredLanguage = result.PreferredLanguage
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
