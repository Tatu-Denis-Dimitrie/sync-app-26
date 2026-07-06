using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SyncApp26.Tests.TestHelpers
{
    public static class ControllerTestExtensions
    {
        public static void SetUser<T>(this T controller, Guid userId, string role = "Basic User", string email = "user@test.com")
            where T : ControllerBase
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Email, email),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "TestAuthType");
            var principal = new ClaimsPrincipal(identity);

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = principal,
                    Request = { Scheme = "https", Host = new HostString("localhost", 5001) }
                }
            };
        }

        public static void SetAnonymousUser<T>(this T controller) where T : ControllerBase
        {
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity()),
                    Request = { Scheme = "https", Host = new HostString("localhost", 5001) }
                }
            };
        }
    }
}
