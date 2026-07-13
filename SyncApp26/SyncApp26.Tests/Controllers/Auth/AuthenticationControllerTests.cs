using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<IAccountService> _accountServiceMock = new();
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IConfiguration> _configurationMock = new();

        private AuthenticationController CreateController()
        {
            var controller = new AuthenticationController(
                _accountServiceMock.Object,
                _emailServiceMock.Object,
                _configurationMock.Object);

            controller.SetAnonymousUser();
            return controller;
        }

        private static RegisterUserRequestDTO ValidRegisterRequest() => new()
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            Password = "Str0ng!Pass"
        };

        // ───────────────────────── Register ─────────────────────────

        [Fact]
        public async Task Register_ServiceReportsFailure_ReturnsBadRequest()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterUserRequestDTO>()))
                .ReturnsAsync(AccountActionResult<RegisteredAccountDTO>.Fail("Email is already registered."));

            var result = await controller.Register(ValidRegisterRequest());

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("already registered", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task Register_EmailDeliveryFails_Returns202WithWarning()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterUserRequestDTO>()))
                .ReturnsAsync(AccountActionResult<RegisteredAccountDTO>.Ok(new RegisteredAccountDTO
                {
                    Email = "john.doe@example.com",
                    FirstName = "John",
                    EmailVerificationToken = "token"
                }));
            _emailServiceMock.Setup(s => s.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("smtp down"));

            var result = await controller.Register(ValidRegisterRequest());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(202, statusResult.StatusCode);
        }

        [Fact]
        public async Task Register_Success_SendsVerificationEmailAndReturnsOk()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterUserRequestDTO>()))
                .ReturnsAsync(AccountActionResult<RegisteredAccountDTO>.Ok(new RegisteredAccountDTO
                {
                    Email = "john.doe@example.com",
                    FirstName = "John",
                    EmailVerificationToken = "token"
                }));

            var result = await controller.Register(ValidRegisterRequest());

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendVerificationEmailAsync("john.doe@example.com", "John", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Register_UnexpectedException_Returns500()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RegisterAsync(It.IsAny<RegisterUserRequestDTO>()))
                .ThrowsAsync(new Exception("db down"));

            var result = await controller.Register(ValidRegisterRequest());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        // ───────────────────────── VerifyEmail ─────────────────────────

        [Fact]
        public async Task VerifyEmail_MissingParams_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.VerifyEmail("", "");

            Assert.IsType<BadRequestObjectResult>(result);
            _accountServiceMock.Verify(s => s.VerifyEmailAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task VerifyEmail_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.VerifyEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new EmailVerificationResult { Status = EmailVerificationStatus.NotFound });

            var result = await controller.VerifyEmail("john@example.com", "token");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_InvalidOrExpiredToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.VerifyEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new EmailVerificationResult { Status = EmailVerificationStatus.InvalidToken });

            var result = await controller.VerifyEmail("john@example.com", "wrong-token");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_Verified_RedirectsToLogin()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.VerifyEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new EmailVerificationResult { Status = EmailVerificationStatus.Verified });
            _configurationMock.Setup(c => c["Frontend:LoginUrl"]).Returns((string?)null);

            var result = await controller.VerifyEmail("john@example.com", "correct-token");

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("http://localhost:4200/login", redirect.Url);
        }

        [Fact]
        public async Task VerifyEmail_Verified_UsesConfiguredLoginUrl()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.VerifyEmailAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new EmailVerificationResult { Status = EmailVerificationStatus.Verified });
            _configurationMock.Setup(c => c["Frontend:LoginUrl"]).Returns("https://custom.example.com/login");

            var result = await controller.VerifyEmail("john@example.com", "token");

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://custom.example.com/login", redirect.Url);
        }

        // ───────────────────────── Login ─────────────────────────

        [Fact]
        public async Task Login_MissingFields_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.Login(new LoginUserRequestDTO { Email = "", Password = "" });

            Assert.IsType<BadRequestObjectResult>(result);
            _accountServiceMock.Verify(s => s.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task Login_InvalidCredentials_ReturnsUnauthorized()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new LoginResult { Status = LoginStatus.InvalidCredentials });

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "WrongPass1!" });

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Login_UnverifiedEmail_ReturnsUnauthorized()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new LoginResult { Status = LoginStatus.EmailNotVerified });

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Contains("not verified", unauthorized.Value!.ToString());
        }

        [Fact]
        public async Task Login_Success_ReturnsTokenAndUserInfo()
        {
            var controller = CreateController();
            var userId = Guid.NewGuid();
            _accountServiceMock.Setup(s => s.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new LoginResult
                {
                    Status = LoginStatus.Success,
                    Token = "jwt-token",
                    UserId = userId,
                    Email = "a@b.com",
                    FirstName = "A",
                    LastName = "B",
                    Role = UserRole.Admin
                });

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            Assert.IsType<OkObjectResult>(result);
        }

        [Fact]
        public async Task Login_UnexpectedException_Returns500()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.AuthenticateAsync(It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new Exception("boom"));

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(500, statusResult.StatusCode);
        }

        // ───────────────────────── ForgotPassword ─────────────────────────

        [Fact]
        public async Task ForgotPassword_MissingEmail_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ForgotPassword_ServiceReportsFailure_ReturnsBadRequest()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RequestPasswordResetAsync(It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<PasswordResetRequestedDTO>.Fail("This email doesn't have an account."));

            var result = await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "noone@example.com" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ForgotPassword_Success_SendsResetEmailAndReturnsOk()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RequestPasswordResetAsync(It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<PasswordResetRequestedDTO>.Ok(new PasswordResetRequestedDTO
                {
                    Email = "a@b.com",
                    FirstName = "A",
                    Token = "raw-token",
                    ExpiresInMinutes = 30
                }));
            _configurationMock.Setup(c => c["Frontend:ResetPasswordUrl"]).Returns((string?)null);

            var result = await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "a@b.com" });

            Assert.IsType<OkObjectResult>(result);
            _emailServiceMock.Verify(s => s.SendPasswordResetEmailAsync("a@b.com", "A", It.IsAny<string>(), 30), Times.Once);
        }

        [Fact]
        public async Task ForgotPassword_ConfiguredResetUrlWithoutQueryString_UsesQuestionMarkSeparator()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RequestPasswordResetAsync(It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<PasswordResetRequestedDTO>.Ok(new PasswordResetRequestedDTO
                {
                    Email = "a@b.com",
                    FirstName = "A",
                    Token = "raw-token",
                    ExpiresInMinutes = 30
                }));
            _configurationMock.Setup(c => c["Frontend:ResetPasswordUrl"]).Returns("https://custom.example.com/reset");

            string? capturedUrl = null;
            _emailServiceMock.Setup(s => s.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, string, string, int>((_, _, url, _) => capturedUrl = url)
                .Returns(Task.CompletedTask);

            await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "a@b.com" });

            Assert.NotNull(capturedUrl);
            Assert.StartsWith("https://custom.example.com/reset?email=", capturedUrl);
        }

        [Fact]
        public async Task ForgotPassword_ConfiguredResetUrlWithExistingQueryString_UsesAmpersandSeparator()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.RequestPasswordResetAsync(It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<PasswordResetRequestedDTO>.Ok(new PasswordResetRequestedDTO
                {
                    Email = "a@b.com",
                    FirstName = "A",
                    Token = "raw-token",
                    ExpiresInMinutes = 30
                }));
            _configurationMock.Setup(c => c["Frontend:ResetPasswordUrl"]).Returns("https://custom.example.com/reset?lang=en");

            string? capturedUrl = null;
            _emailServiceMock.Setup(s => s.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, string, string, int>((_, _, url, _) => capturedUrl = url)
                .Returns(Task.CompletedTask);

            await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "a@b.com" });

            Assert.NotNull(capturedUrl);
            Assert.StartsWith("https://custom.example.com/reset?lang=en&email=", capturedUrl);
        }

        // ───────────────────────── ResetPassword ─────────────────────────

        [Fact]
        public async Task ResetPassword_MissingFields_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO { Email = "", Token = "", NewPassword = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ResetPassword_ServiceReportsFailure_ReturnsBadRequest()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<bool>.Fail("Invalid or expired token."));

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "tok",
                NewPassword = "Str0ng!Pass"
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid or expired token", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task ResetPassword_Success_ReturnsOk()
        {
            var controller = CreateController();
            _accountServiceMock.Setup(s => s.ResetPasswordAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(AccountActionResult<bool>.Ok(true));

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "correct-token",
                NewPassword = "Str0ng!Pass"
            });

            Assert.IsType<OkObjectResult>(result);
        }

        [Theory]
        [InlineData("", "tok", "Str0ng!Pass")]
        [InlineData("a@b.com", "", "Str0ng!Pass")]
        [InlineData("a@b.com", "tok", "")]
        public async Task ResetPassword_IndividualFieldMissing_ReturnsBadRequest(string email, string token, string newPassword)
        {
            var controller = CreateController();

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = email,
                Token = token,
                NewPassword = newPassword
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }
    }
}
