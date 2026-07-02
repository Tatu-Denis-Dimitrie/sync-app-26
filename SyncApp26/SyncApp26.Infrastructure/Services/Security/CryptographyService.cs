using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using SyncApp26.Application.IServices;

namespace SyncApp26.Infrastructure.Services
{
    public class CryptographyService : ICryptographyService
    {
        private const string KeyFileName = "server_rsa_key.json";
        private readonly string _keyFilePath;

        public CryptographyService()
        {
            _keyFilePath = Path.Combine(Directory.GetCurrentDirectory(), KeyFileName);
            EnsureKeyExists();
        }

        private void EnsureKeyExists()
        {
            if (!File.Exists(_keyFilePath))
            {
                using var rsa = RSA.Create(2048);
                var privateKeyJson = rsa.ExportRSAPrivateKey();
                File.WriteAllBytes(_keyFilePath, privateKeyJson);
            }
        }

        private RSA GetRsa()
        {
            var rsa = RSA.Create();
            var privateKeyBytes = File.ReadAllBytes(_keyFilePath);
            rsa.ImportRSAPrivateKey(privateKeyBytes, out _);
            return rsa;
        }

        public Task<string> SignDataAsync(string dataToSign)
        {
            using var rsa = GetRsa();
            var dataBytes = Encoding.UTF8.GetBytes(dataToSign);
            var signatureBytes = rsa.SignData(dataBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var signatureBase64 = Convert.ToBase64String(signatureBytes);
            return Task.FromResult(signatureBase64);
        }

        public Task<bool> VerifySignatureAsync(string data, string signatureBase64)
        {
            using var rsa = GetRsa();
            var dataBytes = Encoding.UTF8.GetBytes(data);
            var signatureBytes = Convert.FromBase64String(signatureBase64);
            var isValid = rsa.VerifyData(dataBytes, signatureBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            return Task.FromResult(isValid);
        }
    }
}
