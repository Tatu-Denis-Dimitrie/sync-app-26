namespace SyncApp26.API.Filters
{
    /// <summary>
    /// Marks a non-GET action as safe during impersonation, so ImpersonationReadOnlyFilter lets it
    /// through even though the request isn't a GET/HEAD/OPTIONS. Use this ONLY for actions that mutate
    /// NOTHING — e.g. a POST used purely because the query (an id list) is too long for a query
    /// string. Adding this to an action that actually writes silently defeats view-only mode.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method | AttributeTargets.Class, AllowMultiple = false, Inherited = true)]
    public sealed class AllowDuringImpersonationAttribute : Attribute
    {
    }
}
