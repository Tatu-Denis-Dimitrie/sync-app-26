using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.IServices
{
    public interface ITokenService
    {
        Task<string> GenerateTokenAsync(Guid userId, string email, UserRole role);
    }
}
