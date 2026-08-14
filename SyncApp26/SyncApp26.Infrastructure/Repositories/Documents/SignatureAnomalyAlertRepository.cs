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
            return await _context.SignatureAnomalyAlerts
                .Where(a => !a.IsRead)
                .OrderByDescending(a => a.OccurredAt)
                .ToListAsync();
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
