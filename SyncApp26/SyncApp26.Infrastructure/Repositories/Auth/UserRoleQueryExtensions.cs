using SyncApp26.Domain.Entities;

namespace SyncApp26.Infrastructure.Repositories
{
    /// <summary>
    /// Role-membership filters over IQueryable&lt;User&gt;. Each method appends a plain .Where(...)
    /// onto the queryable it's given, so the resulting expression tree contains only translatable
    /// navigation/Any() access — the extension method call itself never appears in the tree EF sees.
    /// </summary>
    public static class UserRoleQueryExtensions
    {
        public static IQueryable<User> WithRole(this IQueryable<User> query, string roleName) =>
            query.Where(u => u.RoleAssignments.Any(a => a.Role.Name == roleName));

        public static IQueryable<User> WithoutRole(this IQueryable<User> query, string roleName) =>
            query.Where(u => !u.RoleAssignments.Any(a => a.Role.Name == roleName));
    }
}
