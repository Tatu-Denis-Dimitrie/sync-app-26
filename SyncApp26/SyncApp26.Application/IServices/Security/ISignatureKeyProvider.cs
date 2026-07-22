using System.Threading.Tasks;

namespace SyncApp26.Application.IServices
{
    public interface ISignatureKeyProvider
    {
        Task<byte[]> GetCurrentKeyAsync();
    }
}
