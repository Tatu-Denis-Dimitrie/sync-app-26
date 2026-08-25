using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;
using SyncApp26.Domain.IRepositories;

namespace SyncApp26.Tests.Services.Auth
{
    public class ImpersonationServiceTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<ITokenService> _tokenServiceMock = new();
        private readonly Mock<IImpersonationLogRepository> _logRepositoryMock = new();

        private ImpersonationService CreateService() =>
            new(_userServiceMock.Object, _tokenServiceMock.Object, _logRepositoryMock.Object);

        private static User MakeUser(Guid id, string email = "target@test.com", params string[] roleNames)
        {
            var user = new User { Id = id, FirstName = "Target", LastName = "User", Email = email, PersonalId = id.ToString() };
            foreach (var roleName in roleNames)
            {
                user.RoleAssignments.Add(new UserRoleAssignment { UserId = id, Role = new Role { Name = roleName } });
            }
            return user;
        }

        [Fact]
        public async Task StartAsync_HappyPath_ReturnsSuccessWithTargetIdentityAndLogsAudit()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var target = MakeUser(targetId, "target@test.com", Roles.BasicUser);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(target);
            _tokenServiceMock.Setup(s => s.GenerateImpersonationTokenAsync(targetId, "target@test.com",
                    It.Is<IEnumerable<string>>(r => r.Single() == Roles.BasicUser), adminId))
                .ReturnsAsync("fake-token");

            var result = await service.StartAsync(adminId, targetId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.Success, result.Status);
            Assert.Equal("fake-token", result.Token);
            Assert.Equal(targetId, result.UserId);
            Assert.Equal("target@test.com", result.Email);
            Assert.Equal(new[] { Roles.BasicUser }, result.Roles);

            _logRepositoryMock.Verify(r => r.AddAsync(It.Is<ImpersonationLog>(l =>
                l.ImpersonatorUserId == adminId && l.TargetUserId == targetId && l.IpAddress == "1.2.3.4")), Times.Once);
        }

        [Fact]
        public async Task StartAsync_TargetIsAdmin_RefusesWithoutTokenOrAuditRow()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(MakeUser(targetId, roleNames: new[] { Roles.Admin }));

            var result = await service.StartAsync(adminId, targetId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.TargetIsAdmin, result.Status);
            Assert.Null(result.Token);
            _tokenServiceMock.Verify(s => s.GenerateImpersonationTokenAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid>()), Times.Never);
            _logRepositoryMock.Verify(r => r.AddAsync(It.IsAny<ImpersonationLog>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_TargetHasAdminAmongSeveralRoles_StillRefused()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId))
                .ReturnsAsync(MakeUser(targetId, roleNames: new[] { Roles.LineManager, Roles.Admin }));

            var result = await service.StartAsync(adminId, targetId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.TargetIsAdmin, result.Status);
        }

        [Fact]
        public async Task StartAsync_TargetNotFound_ReturnsTargetNotFoundWithoutToken()
        {
            var service = CreateService();
            var targetId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync((User?)null);

            var result = await service.StartAsync(Guid.NewGuid(), targetId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.TargetNotFound, result.Status);
            Assert.Null(result.Token);
            _logRepositoryMock.Verify(r => r.AddAsync(It.IsAny<ImpersonationLog>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_SelfImpersonation_Refused()
        {
            var service = CreateService();
            var userId = Guid.NewGuid();

            var result = await service.StartAsync(userId, userId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.SelfImpersonation, result.Status);
            Assert.Null(result.Token);
            _userServiceMock.Verify(s => s.GetUserByIdAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_AuditWriteThrows_PropagatesAndNeverIssuesToken()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(MakeUser(targetId, roleNames: new[] { Roles.BasicUser }));
            _logRepositoryMock.Setup(r => r.AddAsync(It.IsAny<ImpersonationLog>())).ThrowsAsync(new InvalidOperationException("db down"));

            await Assert.ThrowsAsync<InvalidOperationException>(() => service.StartAsync(adminId, targetId, "1.2.3.4"));

            _tokenServiceMock.Verify(s => s.GenerateImpersonationTokenAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task StartAsync_TargetEmailNotVerified_StillSucceeds()
        {
            // Locks in the deliberate absence of an IsEmailVerified check: CSV-synced/admin-created
            // accounts leave it null, and an admin must still be able to view exactly those accounts.
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            var target = MakeUser(targetId, roleNames: new[] { Roles.BasicUser });
            target.IsEmailVerified = null;
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(target);
            _tokenServiceMock.Setup(s => s.GenerateImpersonationTokenAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>(), It.IsAny<Guid>()))
                .ReturnsAsync("fake-token");

            var result = await service.StartAsync(adminId, targetId, "1.2.3.4");

            Assert.Equal(ImpersonationStatus.Success, result.Status);
        }

        [Fact]
        public async Task StopAsync_HappyPath_ReturnsSuccessWithAdminIdentity()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            var admin = MakeUser(adminId, "admin@test.com", Roles.Admin);
            _userServiceMock.Setup(s => s.GetUserByIdAsync(adminId)).ReturnsAsync(admin);
            _tokenServiceMock.Setup(s => s.GenerateTokenAsync(adminId, "admin@test.com",
                    It.Is<IEnumerable<string>>(r => r.Single() == Roles.Admin)))
                .ReturnsAsync("fresh-admin-token");

            var result = await service.StopAsync(adminId);

            Assert.Equal(ImpersonationStatus.Success, result.Status);
            Assert.Equal("fresh-admin-token", result.Token);
            Assert.Equal(adminId, result.UserId);
            Assert.Equal("admin@test.com", result.Email);
            Assert.Equal(new[] { Roles.Admin }, result.Roles);
        }

        [Fact]
        public async Task StopAsync_AdminNoLongerExists_ReturnsImpersonatorNotFoundWithoutToken()
        {
            var service = CreateService();
            var adminId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(adminId)).ReturnsAsync((User?)null);

            var result = await service.StopAsync(adminId);

            Assert.Equal(ImpersonationStatus.ImpersonatorNotFound, result.Status);
            Assert.Null(result.Token);
            _tokenServiceMock.Verify(s => s.GenerateTokenAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }

        [Fact]
        public async Task StopAsync_AdminRoleWasRevokedSinceImpersonationStarted_ReturnsImpersonatorNotAdminWithoutToken()
        {
            // The admin's own role can change mid-impersonation (another admin revokes it) - stopping
            // mints a brand new token, so it must not hand back Admin access that no longer applies.
            var service = CreateService();
            var adminId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(adminId)).ReturnsAsync(MakeUser(adminId, roleNames: new[] { Roles.BasicUser }));

            var result = await service.StopAsync(adminId);

            Assert.Equal(ImpersonationStatus.ImpersonatorNotAdmin, result.Status);
            Assert.Null(result.Token);
            _tokenServiceMock.Verify(s => s.GenerateTokenAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<IEnumerable<string>>()), Times.Never);
        }
    }
}
