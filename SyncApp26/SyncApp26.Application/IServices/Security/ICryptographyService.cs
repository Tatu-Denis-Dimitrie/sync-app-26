using System.Threading.Tasks;

namespace SyncApp26.Application.IServices
{
    public interface ICryptographyService
    {
        Task<string> SignDataAsync(string dataToSign);
        Task<bool> VerifySignatureAsync(string data, string signatureBase64);
    }
}
