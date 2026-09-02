using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Shared.DTOs.Request.User;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Services.Auth
{
    public class AccountServiceTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IAuthenticationService> _authenticationServiceMock = new();
        private readonly Mock<ITokenService> _tokenServiceMock = new();
        private readonly Mock<IGoogleTokenValidator> _googleTokenValidatorMock = new();
        private readonly Mock<IMicrosoftTokenValidator> _microsoftTokenValidatorMock = new();
        private readonly Mock<IRefreshTokenService> _refreshTokenServiceMock = new();
        private readonly Mock<ILocalizationService> _localizationServiceMock = new();

        private AccountService CreateService()
        {
            _localizationServiceMock.Setup(s => s.GetScopedLocalizer(LocalizationScopes.Auth))
                .Returns(RealLocalizerFactory.ForScope(LocalizationScopes.Auth));

            return new(
                _userServiceMock.Object,
                _authenticationServiceMock.Object,
                _tokenServiceMock.Object,
                _googleTokenValidatorMock.Object,
                _microsoftTokenValidatorMock.Object,
                _refreshTokenServiceMock.Object,
                NullLogger<AccountService>.Instance,
                _localizationServiceMock.Object);
        }

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
        public async Task RegisterAsync_EmailAlreadyRegistered_ReturnsSuccessWithoutCreatingAccount()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john.doe@example.com", PersonalId = "1" });

            var result = await service.RegisterAsync(ValidRegisterRequest());

            Assert.True(result.Success);
            Assert.True(result.Data!.AlreadyRegistered);
            _userServiceMock.Verify(s => s.AddUserAsync(It.IsAny<User>()), Times.Never);
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

        [Fact]
        public async Task RegisterAsync_PreferredLanguageProvided_CarriesItOntoTheNewUser()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            var request = ValidRegisterRequest();
            request.PreferredLanguage = Language.En;

            await service.RegisterAsync(request);

            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.PreferredLanguage == Language.En)), Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_PreferredLanguageOmitted_LeavesItNull()
        {
            // Null, not the enum's default member - the client falls back to the browser locale for
            // this session; it hasn't necessarily resolved one worth persisting.
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            await service.RegisterAsync(ValidRegisterRequest());

            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.PreferredLanguage == null)), Times.Once);
        }

        [Fact]
        public async Task RegisterAsync_PreferredLanguageUndefinedEnumValue_LeavesItNullRatherThanFailingRegistration()
        {
            // Best-effort enrichment, not a required field - guards against a value that bypassed
            // JsonStringEnumConverter, same reasoning as UpdatePreferredLanguageAsync's guard.
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync((User?)null);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("hashed");

            var request = ValidRegisterRequest();
            request.PreferredLanguage = (Language)999;

            var result = await service.RegisterAsync(request);

            Assert.True(result.Success);
            _userServiceMock.Verify(s => s.AddUserAsync(It.Is<User>(u => u.PreferredLanguage == null)), Times.Once);
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
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "john@example.com", PersonalId = "1", IsEmailVerified = true });

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
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = "hash", IsEmailVerified = true });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(false);

            var result = await service.AuthenticateAsync("a@b.com", "WrongPass1!");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
        }

        [Fact]
        public async Task AuthenticateAsync_UnverifiedEmail_ReturnsEmailNotVerified()
        {
            var service = CreateService();
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>()))
                .ReturnsAsync(new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = "hash", IsEmailVerified = false });
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
                    IsEmailVerified = true
                });
            _authenticationServiceMock.Setup(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>())).ReturnsAsync(true);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateAsync("a@b.com", "Str0ng!Pass");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
            Assert.Equal(userId, result.UserId);
        }

        // ───────────────────────── AuthenticateWithGoogleAsync ─────────────────────────

        [Fact]
        public async Task AuthenticateWithGoogleAsync_InvalidToken_ReturnsInvalidCredentials()
        {
            var service = CreateService();
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>())).ReturnsAsync((GoogleTokenPayload?)null);

            var result = await service.AuthenticateWithGoogleAsync("bad-token");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
            _userServiceMock.Verify(s => s.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_GoogleEmailNotVerified_ReturnsGoogleEmailNotVerified()
        {
            var service = CreateService();
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "a@b.com", EmailVerified = false });

            var result = await service.AuthenticateWithGoogleAsync("token");

            Assert.Equal(LoginStatus.GoogleEmailNotVerified, result.Status);
            _userServiceMock.Verify(s => s.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_UnknownEmail_ReturnsNoAccountForEmailAndCreatesNoUser()
        {
            var service = CreateService();
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "unknown@example.com", EmailVerified = true });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("unknown@example.com")).ReturnsAsync((User?)null);

            var result = await service.AuthenticateWithGoogleAsync("token");

            Assert.Equal(LoginStatus.NoAccountForEmail, result.Status);
            _userServiceMock.Verify(s => s.AddUserAsync(It.IsAny<User>()), Times.Never);
            _tokenServiceMock.Verify(s => s.GenerateTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_ExistingUserWithPassword_ReturnsSuccessAndLeavesPasswordHashIntact()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = "hashed" };
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "a@b.com", EmailVerified = true });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithGoogleAsync("token");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
            Assert.Equal("hashed", user.PasswordHash);
            _userServiceMock.Verify(s => s.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_UserWithNullIsEmailVerified_ReturnsSuccess()
        {
            // CSV-synced and admin-created users leave IsEmailVerified null. Guards against
            // reintroducing that gate here.
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = null, IsEmailVerified = null };
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "a@b.com", EmailVerified = true });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithGoogleAsync("token");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_NormalizesEmailBeforeLookup()
        {
            var service = CreateService();
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "  John.Doe@Example.com  ", EmailVerified = true });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("john.doe@example.com")).ReturnsAsync((User?)null);

            await service.AuthenticateWithGoogleAsync("token");

            _userServiceMock.Verify(s => s.GetUserByEmailAsync("john.doe@example.com"), Times.Once);
        }

        [Fact]
        public async Task AuthenticateWithGoogleAsync_ReturnsIdentityFromDatabaseNotGoogle()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "DbFirst", LastName = "DbLast", Email = "a@b.com", PersonalId = "1" };
            user.RoleAssignments.Add(new UserRoleAssignment { UserId = userId, Role = new Role { Name = Roles.LineManager } });
            _googleTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new GoogleTokenPayload { Email = "a@b.com", EmailVerified = true });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithGoogleAsync("token");

            Assert.Equal("DbFirst", result.FirstName);
            Assert.Equal("DbLast", result.LastName);
            Assert.Contains(Roles.LineManager, result.Roles);
        }

        // ───────────────────────── AuthenticateWithMicrosoftAsync ─────────────────────────

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_InvalidToken_ReturnsInvalidCredentials()
        {
            var service = CreateService();
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>())).ReturnsAsync((MicrosoftTokenPayload?)null);

            var result = await service.AuthenticateWithMicrosoftAsync("bad-token");

            Assert.Equal(LoginStatus.InvalidCredentials, result.Status);
            _userServiceMock.Verify(s => s.GetUserByEmailAsync(It.IsAny<string>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_UnknownEmail_ReturnsNoAccountForEmailAndCreatesNoUser()
        {
            var service = CreateService();
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new MicrosoftTokenPayload { Email = "unknown@example.com" });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("unknown@example.com")).ReturnsAsync((User?)null);

            var result = await service.AuthenticateWithMicrosoftAsync("token");

            Assert.Equal(LoginStatus.NoAccountForEmail, result.Status);
            _userServiceMock.Verify(s => s.AddUserAsync(It.IsAny<User>()), Times.Never);
            _tokenServiceMock.Verify(s => s.GenerateTokenAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_ExistingUserWithPassword_ReturnsSuccessAndLeavesPasswordHashIntact()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = "hashed" };
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new MicrosoftTokenPayload { Email = "a@b.com" });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithMicrosoftAsync("token");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
            Assert.Equal("hashed", user.PasswordHash);
            _userServiceMock.Verify(s => s.UpdateUserAsync(It.IsAny<User>()), Times.Never);
        }

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_UserWithNullIsEmailVerified_ReturnsSuccess()
        {
            // Same guard as the Google flow: CSV-synced users leave IsEmailVerified null.
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = null, IsEmailVerified = null };
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new MicrosoftTokenPayload { Email = "a@b.com" });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithMicrosoftAsync("token");

            Assert.Equal(LoginStatus.Success, result.Status);
            Assert.Equal("jwt-token", result.Token);
        }

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_NormalizesEmailBeforeLookup()
        {
            var service = CreateService();
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new MicrosoftTokenPayload { Email = "  John.Doe@Example.com  " });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("john.doe@example.com")).ReturnsAsync((User?)null);

            await service.AuthenticateWithMicrosoftAsync("token");

            _userServiceMock.Verify(s => s.GetUserByEmailAsync("john.doe@example.com"), Times.Once);
        }

        [Fact]
        public async Task AuthenticateWithMicrosoftAsync_ReturnsIdentityFromDatabaseNotMicrosoft()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();
            var user = new User { Id = userId, FirstName = "DbFirst", LastName = "DbLast", Email = "a@b.com", PersonalId = "1" };
            user.RoleAssignments.Add(new UserRoleAssignment { UserId = userId, Role = new Role { Name = Roles.LineManager } });
            _microsoftTokenValidatorMock.Setup(v => v.ValidateAsync(It.IsAny<string>()))
                .ReturnsAsync(new MicrosoftTokenPayload { Email = "a@b.com" });
            _userServiceMock.Setup(s => s.GetUserByEmailAsync("a@b.com")).ReturnsAsync(user);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(userId, "a@b.com", It.IsAny<IEnumerable<string>>())).ReturnsAsync("jwt-token");

            var result = await service.AuthenticateWithMicrosoftAsync("token");

            Assert.Equal("DbFirst", result.FirstName);
            Assert.Equal("DbLast", result.LastName);
            Assert.Contains(Roles.LineManager, result.Roles);
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
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1" };
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
            var user = new User { Id = Guid.NewGuid(), FirstName = "A", LastName = "B", Email = "a@b.com", PersonalId = "1", PasswordHash = "hash" };
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
        public async Task ResetPasswordAsync_UserWithNullPasswordHash_DoesNotCallVerifyPassword()
        {
            // Password-less users can still reach reset-password. Verifying against a null
            // hash throws in real BCrypt, so the guard must short-circuit before that call.
            var service = CreateService();
            var user = new User
            {
                Id = Guid.NewGuid(),
                FirstName = "A",
                LastName = "B",
                Email = "a@b.com",
                PersonalId = "1",
                PasswordHash = null,
                PasswordResetToken = "correct-token",
                PasswordResetTokenExpiresAt = DateTime.UtcNow.AddMinutes(10)
            };
            _userServiceMock.Setup(s => s.GetUserByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
            _authenticationServiceMock.Setup(s => s.HashPasswordAsync(It.IsAny<string>())).ReturnsAsync("new-hash");

            var result = await service.ResetPasswordAsync("a@b.com", "correct-token", "Str0ng!Pass");

            Assert.True(result.Success);
            Assert.Equal("new-hash", user.PasswordHash);
            _authenticationServiceMock.Verify(s => s.VerifyPasswordAsync(It.IsAny<string>(), It.IsAny<string>()), Times.Never);
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
