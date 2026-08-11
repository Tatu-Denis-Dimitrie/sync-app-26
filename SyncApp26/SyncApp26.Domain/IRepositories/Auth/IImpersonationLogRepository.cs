using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface IImpersonationLogRepository
    {
        Task AddAsync(ImpersonationLog log);
    }
}
