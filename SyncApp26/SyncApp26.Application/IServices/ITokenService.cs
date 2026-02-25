namespace SyncApp26.Application.IServices
{
    public interface ITokenService
    {
        Task<string> GenerateTokenAsync(Guid userId, string email);
    }
}