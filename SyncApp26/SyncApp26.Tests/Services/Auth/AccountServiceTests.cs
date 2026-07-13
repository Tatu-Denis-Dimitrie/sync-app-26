using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;

namespace SyncApp26.Tests.Services.Auth
{
    public class AccountServiceTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IAuthenticationService> _authenticationServiceMock = new();
        private readonly Mock<ITokenService> _tokenServiceMock = new();

        private AccountService CreateService() =>
            new(_userServiceMock.Object, _authenticationServiceMock.Object, _tokenServiceMock.Object);

        private static RegisterUserRequestDTO ValidRegisterRequest() => new()
        {
            FirstName = "John",
            LastName = "Doe",
            Email = "john.doe@example.com",
            Password = "Str0ng!Pass"
        };

        // ───────────────────────── RegisterAsync ─────────────────────────

        [Theory]
        [InlineData("", "Doe", "john@example.com", "Str0ng!Pass")]
        [InlineData("John", "", "john@example.com", "Str0ng!Pass")]
        [InlineData("John", "Doe", "", "Str0ng!Pass")]
        [InlineData("John", "Doe", "john@example.com", "")]
        public async Task RegisterAsync_MissingField_Fails(string firstName, string lastName, string email, string password)
        {
            var service = CreateService();

            var result = await service.RegisterAsync(new RegisterUserRequestDTO
            {
                FirstName = firstName,
                LastName = lastName,
                Email = email,
                Password = password
            });

            Assert.False(result.Success);
            Assert.Equal("All fields are required.", result.ErrorMessage);
        }

        [Fact]
        public async Task RegisterAsync_InvalidEmailFormat_Fails()
        {
            var service = CreateService();
            var request = ValidRegisterRequest();
            request.Email = "not-an-email";

            var result = await service.RegisterAsync(request);

            Assert.False(result.Success);
            Assert.Equal("Invalid email format.", result.ErrorMessage);
        }

        [Theory]
        [InlineData("Short1!")]
        [InlineData("nouppercase1!")]
        [InlineData("NOLOWERCASE1!")]
        [InlineData("NoDigitsHere!")]
        [InlineData("NoSpecialChar1")]
        public async Task RegisterAsync_WeakPassword_Fails(string weakPassword)
        {
            var service = CreateService();
            var request = ValidRegisterRequest();
            request.Password = weakPassword;

            var result = await service.RegisterAsync(request);

            Assert.False(result.Success);
        }

        [Fact]
        public async Task RegisterAsync_EmailAlreadyRegistered_Fails()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john.doe@example.com", PersonalId = "1", Role = UserRole.BasicUser });

            var result = await service.RegisterAsync(ValidRegisterRequest());

            Assert.False(result.Success);
            Assert.Contains("already registered", result.ErrorMessage);
        }

        [Fact]
        public async Task RegisterAsync_Success_AddsUserAndNormalizesEmail()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("john.doe@example.com")).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            var request = ValidRegisterRequest();
            request.Email = "  John.Doe@EXAMPLE.com  ";

            var result = await service.RegisterAsync(request);

            Assert.True(result.Success);
            Assert.Equal("john.doe@example.com", result.Data!.Email);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.Email == "john.doe@example.com" && u.IsEmailVerified == false)), Times.Once);
        }

        // ───────────────────────── VerifyEmailAsync ─────────────────────────

        [Fact]
        public async Task VerifyEmailAsync_UserNotFound_ReturnsNotFound()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.VerifyEmailAsync("john@example.com", "token");

            Assert.Equal(EmailVerificationStatus.NotFound, result.Status);
        }

        [Fact]
        public async Task VerifyEmailAsync_AlreadyVerified_ReturnsVerified()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john@example.com", PersonalId = "1", IsEmailVerified = true, Role = UserRole.BasicUser });

            var result = await service.VerifyEmailAsync("john@example.com", "token");

            Assert.Equal(EmailVerificationStatus.Verified, result.Status);
        }

        [Fact]
        public async Task VerifyEmailAsync_InvalidToken_ReturnsInvalidToken()
        {
            var service = CreateService();
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

            var result = await service.VerifyEmailAsync("john@example.com", "wrong-token");

            Assert.Equal(EmailVerificationStatus.InvalidToken, result.Status);
        }

        [Fact]
        public async Task VerifyEmailAsync_ExpiredToken_ReturnsInvalidToken()
        {
            var service = CreateService();
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

            var result = await service.VerifyEmailAsync("john@example.com", "correct-token");

            Assert.Equal(EmailVerificationStatus.InvalidToken, result.Status);
        }

        [Fact]
        public async Task VerifyEmailAsync_ValidToken_MarksVerifiedAndUpdatesUser()
        {
            var service = CreateService();
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

            var result = await service.VerifyEmailAsync("john@example.com", "correct-token");

            Assert.Equal(EmailVerificationStatus.Verified, result.Status);
            Assert.True(user.IsEmailVerified);
            Assert.Null(user.EmailVerificationToken);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        // ───────────────────────── AuthenticateAsync ─────────────────────────

        [Fact]
        public async Task AuthenticateAsync_UserNotFound_ReturnsInvalidCredentials()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.AuthenticateAsync("a@b.com", "Str0ng!Pass");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        [Fact]
        public async Task AuthenticateAsync_WrongPassword_ReturnsInvalidCredentials()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash", IsEmailVerified = true });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.AuthenticateAsync("a@b.com", "WrongPass1!");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        [Fact]
        public async Task AuthenticateAsync_UnverifiedEmail_ReturnsEmailNotVerified()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash", IsEmailVerified = false });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);

            var result = await service.AuthenticateAsync("a@b.com", "Str0ng!Pass");

            Assert.Equal(LoginStatus.EmailNotVerified, result.Status);
        }

        [Fact]
        public async Task AuthenticateAsync_Success_ReturnsTokenAndUserInfo()
        {
            var service = CreateService();
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

            var result = await service.AuthenticateAsync("a@b.com", "Str0ng!Pass");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
            Assert.Equal(userId, result.UserId);
        }

        // ───────────────────────── RequestPasswordResetAsync ─────────────────────────

        [Fact]
        public async Task RequestPasswordResetAsync_UserNotFound_Fails()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.RequestPasswordResetAsync("noone@example.com");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task RequestPasswordResetAsync_Success_SetsTokenAndExpiry()
        {
            var service = CreateService();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);

            var result = await service.RequestPasswordResetAsync("a@b.com");

            Assert.True(result.Success);
            Assert.NotNull(user.PasswordResetToken);
            Assert.Equal(user.PasswordResetToken, result.Data!.Token);
            Assert.Equal(30, result.Data.ExpiresInMinutes);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }

        // ───────────────────────── ResetPasswordAsync ─────────────────────────

        [Fact]
        public async Task ResetPasswordAsync_WeakPassword_Fails()
        {
            var service = CreateService();

            var result = await service.ResetPasswordAsync("a@b.com", "tok", "weak");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordAsync_UserNotFound_Fails()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);

            var result = await service.ResetPasswordAsync("a@b.com", "tok", "Str0ng!Pass");

            Assert.False(result.Success);
            Assert.Equal("Invalid or expired token.", result.ErrorMessage);
        }

        [Fact]
        public async Task ResetPasswordAsync_SameAsOldPassword_Fails()
        {
            var service = CreateService();
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", Role = UserRole.BasicUser, PasswordHash = "hash" };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), "hash")).ReturnsAsync(true);

            var result = await service.ResetPasswordAsync("a@b.com", "tok", "Str0ng!Pass");

            Assert.False(result.Success);
            Assert.Contains("cannot be the same", result.ErrorMessage);
        }

        [Fact]
        public async Task ResetPasswordAsync_InvalidToken_Fails()
        {
            var service = CreateService();
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

            var result = await service.ResetPasswordAsync("a@b.com", "wrong-token", "Str0ng!Pass");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordAsync_ExpiredToken_Fails()
        {
            var service = CreateService();
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

            var result = await service.ResetPasswordAsync("a@b.com", "correct-token", "Str0ng!Pass");

            Assert.False(result.Success);
        }

        [Fact]
        public async Task ResetPasswordAsync_Success_UpdatesPasswordAndClearsToken()
        {
            var service = CreateService();
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

            var result = await service.ResetPasswordAsync("a@b.com", "correct-token", "Str0ng!Pass");

            Assert.True(result.Success);
            Assert.Equal("new-hash", user.PasswordHash);
            Assert.Null(user.PasswordResetToken);
            _userServiceMock.Verify(s => s.UpdateUserAsync(user), Times.Once);
        }
    }
}
