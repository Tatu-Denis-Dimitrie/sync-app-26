using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Services;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class AuthenticationControllerTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<ITokenService> _tokenServiceMock = new();
        private readonly Mock<IAuthenticationService> _authenticationServiceMock = new();
        private readonly Mock<IEmailService> _emailServiceMock = new();
        private readonly Mock<IConfiguration> _configurationMock = new();

        private AuthenticationController CreateController()
        {
            var controller = new AuthenticationController(
                _userServiceMock.Object,
                _tokenServiceMock.Object,
                _authenticationServiceMock.Object,
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
        public async Task Register_MissingFields_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.Register(new RegisterUserRequestDTO
            {
                FirstName = "",
                LastName = "Doe",
                Email = "john@example.com",
                Password = "Str0ng!Pass"
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Register_InvalidEmailFormat_ReturnsBadRequest()
        {
            var controller = CreateController();
            var request = ValidRegisterRequest();
            request.Email = "not-an-email";

            var result = await controller.Register(request);

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("Invalid email format", badRequest.Value!.ToString());
        }

        [Theory]
        [InlineData("Short1!")]      // too short
        [InlineData("nouppercase1!")] // no uppercase
        [InlineData("NOLOWERCASE1!")] // no lowercase
        [InlineData("NoDigitsHere!")] // no digit
        [InlineData("NoSpecialChar1")] // no special char
        public async Task Register_WeakPassword_ReturnsBadRequest(string weakPassword)
        {
            var controller = CreateController();
            var request = ValidRegisterRequest();
            request.Password = weakPassword;

            var result = await controller.Register(request);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Register_EmailAlreadyRegistered_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john.doe@example.com", PersonalId = "1", Role = UserRole.BasicUser });

            var result = await controller.Register(ValidRegisterRequest());

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("already registered", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task Register_EmailDeliveryFails_Returns202WithWarning()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");
            _emailServiceMock.Setup(s => s.SendVerificationEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>()))
                .ThrowsAsync(new InvalidOperationException("smtp down"));

            var result = await controller.Register(ValidRegisterRequest());

            var statusResult = Assert.IsType<ObjectResult>(result);
            Assert.Equal(202, statusResult.StatusCode);
            _userServiceMock.Verify(s => s.AddUserAsync(It.IsAny<User>()), Times.Once);
        }

        [Fact]
        public async Task Register_Success_AddsUserAndReturnsOk()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            var result = await controller.Register(ValidRegisterRequest());

            Assert.IsType<OkObjectResult>(result);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.Email == "john.doe@example.com" && u.IsEmailVerified == false)), Times.Once);
            _emailServiceMock.Verify(s => s.SendVerificationEmailAsync("john.doe@example.com", "John", It.IsAny<string>()), Times.Once);
        }

        [Fact]
        public async Task Register_UnexpectedException_Returns500()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ThrowsAsync(new Exception("db down"));

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
        }

        [Fact]
        public async Task VerifyEmail_UserNotFound_ReturnsNotFound()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await controller.VerifyEmail("john@example.com", "token");

            Assert.IsType<NotFoundObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_AlreadyVerified_RedirectsToLogin()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john@example.com", PersonalId = "1", IsEmailVerified = true, Role = UserRole.BasicUser });
            _configurationMock.Setup(c => c["Frontend:LoginUrl"]).Returns((string?)null);

            var result = await controller.VerifyEmail("john@example.com", "token");

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("http://localhost:4200/login", redirect.Url);
        }

        [Fact]
        public async Task VerifyEmail_InvalidOrExpiredToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "A",
                    LastName = "B",
                    Email = "john@example.com",
                    PersonalId = "1",
                    Role = UserRole.BasicUser,
                    IsEmailVerified = false,
                    EmailVerificationToken = "correct-token",
                    EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(1)
                });

            var result = await controller.VerifyEmail("john@example.com", "wrong-token");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_ExpiredToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User
                {
                    Id = Guid.NewGuid(),
                    FirstName = "A",
                    LastName = "B",
                    Email = "john@example.com",
                    PersonalId = "1",
                    Role = UserRole.BasicUser,
                    IsEmailVerified = false,
                    EmailVerificationToken = "correct-token",
                    EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(-1)
                });

            var result = await controller.VerifyEmail("john@example.com", "correct-token");

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_ValidToken_UpdatesUserAndRedirects()
        {
            var controller = CreateController();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Email = "john@example.com",
                PersonalId = "1",
                Role = UserRole.BasicUser,
                IsEmailVerified = false,
                EmailVerificationToken = "correct-token",
                EmailVerificationTokenExpiresAt = DateTime.UtcNow.AddHours(1)
            };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _configurationMock.Setup(c => c["Frontend:LoginUrl"]).Returns("http://localhost:4200/login");

            var result = await controller.VerifyEmail("john@example.com", "correct-token");

            Assert.IsType<RedirectResult>(result);
            Assert.True(user.IsEmailVerified);
            Assert.Null(user.EmailVerificationToken);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        // ───────────────────────── Login ─────────────────────────

        [Fact]
        public async Task Login_MissingFields_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.Login(new LoginUserRequestDTO { Email = "", Password = "" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Login_UserNotFound_ReturnsUnauthorized()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Login_WrongPassword_ReturnsUnauthorized()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash", IsEmailVerified = true });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "WrongPass1!" });

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task Login_UnverifiedEmail_ReturnsUnauthorized()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash", IsEmailVerified = false });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            var unauthorized = Assert.IsType<UnauthorizedObjectResult>(result);
            Assert.Contains("not verified", unauthorized.Value!.ToString());
        }

        [Fact]
        public async Task Login_Success_ReturnsTokenAndUserInfo()
        {
            var controller = CreateController();
            var userId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User
                {
                    Id = userId,
                    FirstName = "A",
                    LastName = "B",
                    Email = "a@b.com",
                    PersonalId = "1",
                    PasswordHash = "hash",
                    IsEmailVerified = true,
                    Role = UserRole.Admin
                });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", UserRole.Admin)).ReturnsAsync("jwt-token");

            var result = await controller.Login(new LoginUserRequestDTO { Email = "a@b.com", Password = "Str0ng!Pass" });

            var ok = Assert.IsType<OkObjectResult>(result);
            _tokenServiceMock.Verify(s => s.GenerateTokenAsync(userId, "a@b.com", UserRole.Admin), Times.Once);
        }

        [Fact]
        public async Task Login_UnexpectedException_Returns500()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ThrowsAsync(new Exception("boom"));

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
        public async Task ForgotPassword_UserNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "noone@example.com" });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ForgotPassword_Success_SendsResetEmailAndReturnsOk()
        {
            var controller = CreateController();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _configurationMock.Setup(c => c["Frontend:ResetPasswordUrl"]).Returns((string?)null);

            var result = await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "a@b.com" });

            Assert.IsType<OkObjectResult>(result);
            Assert.NotNull(user.PasswordResetToken);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
            _emailServiceMock.Verify(s => s.SendPasswordResetEmailAsync("a@b.com", "A", It.IsAny<string>(), 30), Times.Once);
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
        public async Task ResetPassword_WeakNewPassword_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "tok",
                NewPassword = "weak"
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ResetPassword_UserNotFound_ReturnsBadRequest()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

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
        public async Task ResetPassword_SameAsOldPassword_ReturnsBadRequest()
        {
            var controller = CreateController();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash" };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), "hash")).ReturnsAsync(true);

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "tok",
                NewPassword = "Str0ng!Pass"
            });

            var badRequest = Assert.IsType<BadRequestObjectResult>(result);
            Assert.Contains("cannot be the same", badRequest.Value!.ToString());
        }

        [Fact]
        public async Task ResetPassword_InvalidToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Email = "a@b.com",
                PersonalId = "1",
                Role = UserRole.BasicUser,
                PasswordHash = "hash",
                PasswordResetToken = "correct-token",
                PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), "hash")).ReturnsAsync(false);

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "wrong-token",
                NewPassword = "Str0ng!Pass"
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task ResetPassword_Success_UpdatesPasswordAndReturnsOk()
        {
            var controller = CreateController();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Email = "a@b.com",
                PersonalId = "1",
                Role = UserRole.BasicUser,
                PasswordHash = "hash",
                PasswordResetToken = "correct-token",
                PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), "hash")).ReturnsAsync(false);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("new-hash");

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "correct-token",
                NewPassword = "Str0ng!Pass"
            });

            Assert.IsType<OkObjectResult>(result);
            Assert.Equal("new-hash", user.PasswordHash);
            Assert.Null(user.PasswordResetToken);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        [Fact]
        public async Task ResetPassword_ExpiredToken_ReturnsBadRequest()
        {
            var controller = CreateController();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Email = "a@b.com",
                PersonalId = "1",
                Role = UserRole.BasicUser,
                PasswordHash = "hash",
                PasswordResetToken = "correct-token",
                PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(-1)
            };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), "hash")).ReturnsAsync(false);

            var result = await controller.ResetPassword(new ResetPasswordWithTokenRequestDTO
            {
                Email = "a@b.com",
                Token = "correct-token",
                NewPassword = "Str0ng!Pass"
            });

            Assert.IsType<BadRequestObjectResult>(result);
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

        // ───────────────────────── Additional Register edge cases ─────────────────────────

        [Fact]
        public async Task Register_NullRequest_ReturnsBadRequest()
        {
            var controller = CreateController();

            var result = await controller.Register(null!);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Theory]
        [InlineData("", "Doe", "john@example.com", "Str0ng!Pass")]
        [InlineData("John", "", "john@example.com", "Str0ng!Pass")]
        [InlineData("John", "Doe", "", "Str0ng!Pass")]
        [InlineData("John", "Doe", "john@example.com", "")]
        public async Task Register_IndividualFieldMissing_ReturnsBadRequest(string firstName, string lastName, string email, string password)
        {
            var controller = CreateController();

            var result = await controller.Register(new RegisterUserRequestDTO
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Password = password
            });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task Register_Success_NormalizesEmailToLowercaseAndTrimmed()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("john.doe@example.com")).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            var request = ValidRegisterRequest();
            request.Email = "  John.Doe@EXAMPLE.com  ";

            var result = await controller.Register(request);

            Assert.IsType<OkObjectResult>(result);
            _userServiceMock.Verify(s => s.GetUserByEmailAsync("john.doe@example.com"), Times.Once);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.Email == "john.doe@example.com")), Times.Once);
        }

        // ───────────────────────── Additional VerifyEmail edge cases ─────────────────────────

        [Theory]
        [InlineData("", "token")]
        [InlineData("john@example.com", "")]
        public async Task VerifyEmail_IndividualParamMissing_ReturnsBadRequest(string email, string token)
        {
            var controller = CreateController();

            var result = await controller.VerifyEmail(email, token);

            Assert.IsType<BadRequestObjectResult>(result);
        }

        [Fact]
        public async Task VerifyEmail_AlreadyVerified_UsesConfiguredLoginUrl()
        {
            var controller = CreateController();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john@example.com", PersonalId = "1", IsEmailVerified = true, Role = UserRole.BasicUser });
            _configurationMock.Setup(c => c["Frontend:LoginUrl"]).Returns("https://custom.example.com/login");

            var result = await controller.VerifyEmail("john@example.com", "token");

            var redirect = Assert.IsType<RedirectResult>(result);
            Assert.Equal("https://custom.example.com/login", redirect.Url);
        }

        // ───────────────────────── Additional Login edge cases ─────────────────────────

        [Theory]
        [InlineData("", "Str0ng!Pass")]
        [InlineData("a@b.com", "")]
        public async Task Login_IndividualFieldMissing_ReturnsBadRequest(string email, string password)
        {
            var controller = CreateController();

            var result = await controller.Login(new LoginUserRequestDTO { Email = email, Password = password });

            Assert.IsType<BadRequestObjectResult>(result);
        }

        // ───────────────────────── Additional ForgotPassword edge cases ─────────────────────────

        [Fact]
        public async Task ForgotPassword_ConfiguredResetUrlWithoutQueryString_UsesQuestionMarkSeparator()
        {
            var controller = CreateController();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
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
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _configurationMock.Setup(c => c["Frontend:ResetPasswordUrl"]).Returns("https://custom.example.com/reset?lang=en");

            string? capturedUrl = null;
            _emailServiceMock.Setup(s => s.SendPasswordResetEmailAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<int>()))
                .Callback<string, string, string, int>((_, _, url, _) => capturedUrl = url)
                .Returns(Task.CompletedTask);

            await controller.ForgotPassword(new ForgotPasswordRequestDTO { Email = "a@b.com" });

            Assert.NotNull(capturedUrl);
            Assert.StartsWith("https://custom.example.com/reset?lang=en&email=", capturedUrl);
        }
    }
}
