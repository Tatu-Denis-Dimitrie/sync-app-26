using System.Threading.Tasks;

namespace SyncApp26.Application.IServices
{
    public interface IHmacSignatureService
    {
        Task<string> ComputeHmacAsync(string canonicalInput);
        Task<bool> VerifyHmacAsync(string canonicalInput, string expectedHmac);
    }
}
