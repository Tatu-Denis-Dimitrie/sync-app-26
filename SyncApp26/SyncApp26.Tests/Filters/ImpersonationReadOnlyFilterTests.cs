using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using SyncApp26.API.Filters;
using SyncApp26.Domain.Enums;
using SyncApp26.Tests.TestHelpers;

namespace SyncApp26.Tests.Filters
{
    public class ImpersonationReadOnlyFilterTests
    {
        private static AuthorizationFilterContext CreateContext(
            string method, ClaimsPrincipal user, IList<object>? endpointMetadata = null)
        {
            var httpContext = new DefaultHttpContext
            {
                User = user,
                RequestServices = RealLocalizerFactory.ServiceProvider()
            };
            httpContext.Request.Method = method;

            var actionDescriptor = new ControllerActionDescriptor
            {
                EndpointMetadata = endpointMetadata ?? new List<object>()
            };

            var actionContext = new ActionContext(httpContext, new RouteData(), actionDescriptor);
            return new AuthorizationFilterContext(actionContext, new List<IFilterMetadata>());
        }

        private static ClaimsPrincipal ImpersonatingPrincipal(string impersonatorIdValue = "admin-id") =>
            new(new ClaimsIdentity(new[] { new Claim(CustomClaimTypes.ImpersonatorId, impersonatorIdValue) }, "TestAuthType"));

        private static ClaimsPrincipal NormalPrincipal() =>
            new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, Guid.NewGuid().ToString()) }, "TestAuthType"));

        private static ClaimsPrincipal UnauthenticatedPrincipal() => new(new ClaimsIdentity());

        [Fact]
        public void OnAuthorization_ImpersonatingPost_BlocksWithObjectResultNotForbidResult()
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext("POST", ImpersonatingPrincipal());

            filter.OnAuthorization(context);

            var result = Assert.IsType<ObjectResult>(context.Result);
            Assert.IsNotType<ForbidResult>(context.Result);
            Assert.Equal(StatusCodes.Status403Forbidden, result.StatusCode);
            var codeProp = result.Value!.GetType().GetProperty("code")!.GetValue(result.Value);
            Assert.Equal(ImpersonationReadOnlyFilter.BlockedCode, codeProp);
        }

        [Theory]
        [InlineData("POST")]
        [InlineData("PUT")]
        [InlineData("PATCH")]
        [InlineData("DELETE")]
        public void OnAuthorization_ImpersonatingNonGetVerbs_AllBlocked(string method)
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext(method, ImpersonatingPrincipal());

            filter.OnAuthorization(context);

            Assert.NotNull(context.Result);
        }

        [Theory]
        [InlineData("GET")]
        [InlineData("HEAD")]
        [InlineData("OPTIONS")]
        public void OnAuthorization_ImpersonatingReadVerbs_NotBlocked(string method)
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext(method, ImpersonatingPrincipal());

            filter.OnAuthorization(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OnAuthorization_NormalUserNonGet_NotBlocked()
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext("POST", NormalPrincipal());

            filter.OnAuthorization(context);

            Assert.Null(context.Result);
        }

        [Fact]
        public void OnAuthorization_ImpersonatingWithAllowDuringImpersonationMarker_NotBlocked()
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext("POST", ImpersonatingPrincipal(),
                new List<object> { new AllowDuringImpersonationAttribute() });

            filter.OnAuthorization(context);

            Assert.Null(context.Result);
        }

        [Theory]
        [InlineData("not-a-guid")]
        [InlineData("")]
        public void OnAuthorization_MalformedImpersonatorIdClaim_StillBlocked(string malformedValue)
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext("POST", ImpersonatingPrincipal(malformedValue));

            filter.OnAuthorization(context);

            Assert.NotNull(context.Result);
        }

        [Fact]
        public void OnAuthorization_Unauthenticated_NotBlocked()
        {
            var filter = new ImpersonationReadOnlyFilter(NullLogger<ImpersonationReadOnlyFilter>.Instance);
            var context = CreateContext("POST", UnauthenticatedPrincipal());

            filter.OnAuthorization(context);

            Assert.Null(context.Result);
        }
    }
}
