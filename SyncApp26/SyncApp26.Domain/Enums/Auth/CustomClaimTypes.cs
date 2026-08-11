namespace SyncApp26.Domain.Enums
{
    /// <summary>
    /// Claim types this app adds beyond the standard System.Security.Claims.ClaimTypes ones.
    /// </summary>
    public static class CustomClaimTypes
    {
        /// <summary>
        /// Present only on impersonation tokens; carries the id of the admin who issued the token.
        /// Its mere presence marks the session read-only — see ImpersonationReadOnlyFilter. Not one of
        /// the JWT handler's well-known claim types, so it round-trips verbatim (no inbound/outbound
        /// claim-type mapping applies) and ClaimsPrincipal.FindFirst(ImpersonatorId) works as-is.
        /// </summary>
        public const string ImpersonatorId = "impersonator_id";
    }
}
