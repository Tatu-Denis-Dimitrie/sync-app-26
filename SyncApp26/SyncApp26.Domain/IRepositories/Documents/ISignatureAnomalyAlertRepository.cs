using SyncApp26.Domain.Entities;

namespace SyncApp26.Domain.IRepositories
{
    public interface ISignatureAnomalyAlertRepository
    {
        Task AddAsync(SignatureAnomalyAlert alert);
        Task<List<SignatureAnomalyAlert>> GetUnreadAsync();
        Task MarkAllAsReadAsync(Guid adminId);
    }
}
