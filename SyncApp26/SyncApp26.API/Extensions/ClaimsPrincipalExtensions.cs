using System;
using System.Security.Claims;

namespace SyncApp26.API.Extensions
{
    public static class ClaimsPrincipalExtensions
    {
        public static Guid? GetUserId(this ClaimsPrincipal user)
        {
            var raw = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return Guid.TryParse(raw, out var id) ? id : null;
        }

        public static string? GetEmail(this ClaimsPrincipal user)
            => user.FindFirst(ClaimTypes.Email)?.Value;

        public static string? GetRole(this ClaimsPrincipal user)
            => user.FindFirst(ClaimTypes.Role)?.Value;
    }
}
