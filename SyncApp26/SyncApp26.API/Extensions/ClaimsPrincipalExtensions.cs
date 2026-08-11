using System;
using System.Security.Claims;
using SyncApp26.Domain.Enums;

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

        // Who may start a training session for a document type: the officer for that type (everyone).
        // Admin is deliberately excluded — app administration and SSM/SU responsibility are separate
        // duties. A line manager may still initiate, but only for their own direct reports — callers
        // check that separately, since it isn't a role check.
        public static bool CanInitiateFor(this ClaimsPrincipal user, string documentType) =>
            user.IsInRole(DocumentTypes.IsSsm(documentType) ? Roles.SsmOfficer : Roles.SuOfficer);
    }
}
