using Microsoft.EntityFrameworkCore;
using SyncApp26.Domain.Entities;
using SyncApp26.Domain.IRepositories;
using SyncApp26.Infrastructure.Context;

namespace SyncApp26.Infrastructure.Repositories
{
    public class SignatureAnomalyAlertRepository : ISignatureAnomalyAlertRepository
    {
        private readonly ApplicationDbContext _context;

        public SignatureAnomalyAlertRepository(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(SignatureAnomalyAlert alert)
        {
            await _context.SignatureAnomalyAlerts.AddAsync(alert);
            await _context.SaveChangesAsync();
        }

        public async Task<List<SignatureAnomalyAlert>> GetUnreadAsync()
        {
            // OccurredAt is a DateTimeOffset, which SQLite can't translate into an ORDER BY clause
            // (unlike every other CreatedAt/timestamp in this codebase, which uses DateTime).
            // The unread set is small (sweep-run alerts only), so sort client-side after fetching.
            var unread = await _context.SignatureAnomalyAlerts
                .Where(a => !a.IsRead)
                .ToListAsync();

            return unread
                .OrderByDescending(a => a.OccurredAt)
                .ToList();
        }

        public async Task MarkAllAsReadAsync(Guid adminId)
        {
            var unread = await _context.SignatureAnomalyAlerts
                .Where(a => !a.IsRead)
                .ToListAsync();

            if (unread.Count == 0) return;

            var now = DateTimeOffset.UtcNow;
            foreach (var alert in unread)
            {
                alert.IsRead = true;
                alert.ReadAt = now;
                alert.ReadByAdminId = adminId;
            }

            await _context.SaveChangesAsync();
        }
    }
}
