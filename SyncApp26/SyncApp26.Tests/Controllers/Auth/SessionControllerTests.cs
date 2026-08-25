using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.API.Extensions;
using SyncApp26.API.Filters;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Controllers.Auth
{
    public class SessionControllerTests
    {
        private readonly Mock<IUserService> _userServiceMock = new();
        private readonly Mock<IImpersonationService> _impersonationServiceMock = new();
        private readonly AuthCookieOptions _authCookieOptions = new();

        private SessionController CreateController() =>
            new(_userServiceMock.Object, _impersonationServiceMock.Object, _authCookieOptions);

        private static User MakeUser(Guid id, string email, params string[] roleNames)
        {
            var user = new User { Id = id, FirstName = "First", LastName = "Last", Email = email, PersonalId = id.ToString() };
            foreach (var roleName in roleNames)
            {
                user.RoleAssignments.Add(new UserRoleAssignment { UserId = id, Role = new Role { Name = roleName } });
            }
            return user;
        }

        private static void SetPrincipal(SessionController controller, ClaimsPrincipal principal)
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = principal }
            };
        }

        private static ClaimsPrincipal MakePrincipal(Guid userId, string role = Roles.BasicUser, string? impersonatorId = null)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, userId.ToString()),
                new(ClaimTypes.Role, role)
            };
            if (impersonatorId != null)
            {
                claims.Add(new Claim(CustomClaimTypes.ImpersonatorId, impersonatorId));
            }
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuthType"));
        }

        private static ClaimsPrincipal AnonymousPrincipal() => new(new ClaimsIdentity());

        // ───────────────────────── Me ─────────────────────────

        [Fact]
        public async Task Me_Anonymous_ReturnsAuthenticatedFalse()
        {
            var controller = CreateController();
            SetPrincipal(controller, AnonymousPrincipal());

            var result = await controller.Me();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False((bool)ok.Value!.GetType().GetProperty("authenticated")!.GetValue(ok.Value)!);
        }

        [Fact]
        public async Task Me_AuthenticatedUser_ReturnsRolesFromTokenNotDb()
        {
            // DB has Admin, token claim has BasicUser - Me must trust the signed token, not the DB,
            // so the UI's view of roles can never diverge from what the API will actually authorize.
            var controller = CreateController();
            var userId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(userId)).ReturnsAsync(MakeUser(userId, "u@test.com", Roles.Admin));
            SetPrincipal(controller, MakePrincipal(userId, role: Roles.BasicUser));

            var result = await controller.Me();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True((bool)ok.Value!.GetType().GetProperty("authenticated")!.GetValue(ok.Value)!);
            var user = ok.Value.GetType().GetProperty("user")!.GetValue(ok.Value)!;
            var roles = (List<string>)user.GetType().GetProperty("roles")!.GetValue(user)!;
            Assert.Equal(new[] { Roles.BasicUser }, roles);
        }

        [Fact]
        public async Task Me_UserNoLongerExists_ReturnsAuthenticatedFalse()
        {
            var controller = CreateController();
            var userId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(userId)).ReturnsAsync((User?)null);
            SetPrincipal(controller, MakePrincipal(userId));

            var result = await controller.Me();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.False((bool)ok.Value!.GetType().GetProperty("authenticated")!.GetValue(ok.Value)!);
        }

        [Fact]
        public async Task Me_Impersonating_ReturnsFullImpersonatorBlock()
        {
            var controller = CreateController();
            var targetId = Guid.NewGuid();
            var adminId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(MakeUser(targetId, "target@test.com", Roles.BasicUser));
            _userServiceMock.Setup(s => s.GetUserByIdAsync(adminId)).ReturnsAsync(MakeUser(adminId, "admin@test.com", Roles.Admin));
            SetPrincipal(controller, MakePrincipal(targetId, role: Roles.BasicUser, impersonatorId: adminId.ToString()));

            var result = await controller.Me();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True((bool)ok.Value!.GetType().GetProperty("impersonating")!.GetValue(ok.Value)!);
            var impersonator = ok.Value.GetType().GetProperty("impersonator")!.GetValue(ok.Value);
            Assert.NotNull(impersonator);
            Assert.Equal("admin@test.com", impersonator!.GetType().GetProperty("email")!.GetValue(impersonator));
        }

        [Fact]
        public async Task Me_ImpersonatorNoLongerExists_StaysAuthenticatedWithNullImpersonator()
        {
            var controller = CreateController();
            var targetId = Guid.NewGuid();
            var adminId = Guid.NewGuid();
            _userServiceMock.Setup(s => s.GetUserByIdAsync(targetId)).ReturnsAsync(MakeUser(targetId, "target@test.com", Roles.BasicUser));
            _userServiceMock.Setup(s => s.GetUserByIdAsync(adminId)).ReturnsAsync((User?)null);
            SetPrincipal(controller, MakePrincipal(targetId, role: Roles.BasicUser, impersonatorId: adminId.ToString()));

            var result = await controller.Me();

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.True((bool)ok.Value!.GetType().GetProperty("authenticated")!.GetValue(ok.Value)!);
            Assert.False((bool)ok.Value.GetType().GetProperty("impersonating")!.GetValue(ok.Value)!);
            Assert.Null(ok.Value.GetType().GetProperty("impersonator")!.GetValue(ok.Value));
        }

        // ───────────────────────── Logout ─────────────────────────

        [Fact]
        public void Logout_DeletesSessionCookie()
        {
            var controller = CreateController();
            SetPrincipal(controller, AnonymousPrincipal());

            var result = controller.Logout();

            Assert.IsType<OkObjectResult>(result);
            var setCookie = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains(_authCookieOptions.Name, setCookie);
            Assert.Contains("1970", setCookie);
        }

        [Fact]
        public void Logout_CarriesAllowDuringImpersonationAttribute()
        {
            // Lock-in for the exact failure mode this attribute prevents: the logout button is
            // visible during impersonation, and ImpersonationReadOnlyFilter blocks non-GET requests
            // on an impersonating principal unless the action carries this marker.
            var method = typeof(SessionController).GetMethod(nameof(SessionController.Logout))!;

            Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowDuringImpersonationAttribute), inherit: false));
        }

        // ───────────────────────── StopImpersonation ─────────────────────────

        [Fact]
        public async Task StopImpersonation_NotImpersonating_ReturnsBadRequestWithoutCallingService()
        {
            var controller = CreateController();
            SetPrincipal(controller, MakePrincipal(Guid.NewGuid()));

            var result = await controller.StopImpersonation();

            Assert.IsType<BadRequestObjectResult>(result);
            _impersonationServiceMock.Verify(s => s.StopAsync(It.IsAny<Guid>()), Times.Never);
        }

        [Fact]
        public async Task StopImpersonation_Success_SetsFreshCookieAndReturnsAdminIdentity()
        {
            var controller = CreateController();
            var adminId = Guid.NewGuid();
            var targetId = Guid.NewGuid();
            SetPrincipal(controller, MakePrincipal(targetId, impersonatorId: adminId.ToString()));
            _impersonationServiceMock.Setup(s => s.StopAsync(adminId)).ReturnsAsync(new ImpersonationResult
            {
                Status = ImpersonationStatus.Success,
                Token = "fresh-token",
                UserId = adminId,
                Email = "admin@test.com",
                FirstName = "Admin",
                LastName = "User",
                Roles = new[] { Roles.Admin }
            });

            var result = await controller.StopImpersonation();

            Assert.IsType<OkObjectResult>(result);
            var setCookie = controller.HttpContext.Response.Headers["Set-Cookie"].ToString();
            Assert.Contains(_authCookieOptions.Name, setCookie);
            Assert.Contains("fresh-token", setCookie);
        }

        [Fact]
        public async Task StopImpersonation_ImpersonatorNoLongerExists_ReturnsUnauthorized()
        {
            var controller = CreateController();
            var adminId = Guid.NewGuid();
            SetPrincipal(controller, MakePrincipal(Guid.NewGuid(), impersonatorId: adminId.ToString()));
            _impersonationServiceMock.Setup(s => s.StopAsync(adminId))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.ImpersonatorNotFound });

            var result = await controller.StopImpersonation();

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public async Task StopImpersonation_ImpersonatorLostAdminRole_ReturnsUnauthorized()
        {
            var controller = CreateController();
            var adminId = Guid.NewGuid();
            SetPrincipal(controller, MakePrincipal(Guid.NewGuid(), impersonatorId: adminId.ToString()));
            _impersonationServiceMock.Setup(s => s.StopAsync(adminId))
                .ReturnsAsync(new ImpersonationResult { Status = ImpersonationStatus.ImpersonatorNotAdmin });

            var result = await controller.StopImpersonation();

            Assert.IsType<UnauthorizedObjectResult>(result);
        }

        [Fact]
        public void StopImpersonation_CarriesAllowDuringImpersonationAttribute()
        {
            var method = typeof(SessionController).GetMethod(nameof(SessionController.StopImpersonation))!;

            Assert.NotEmpty(method.GetCustomAttributes(typeof(AllowDuringImpersonationAttribute), inherit: false));
        }
    }
}
