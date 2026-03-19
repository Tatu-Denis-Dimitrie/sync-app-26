namespace SyncApp26.Shared.DTOs.Request.UserSignature
{
    public class SaveUserSignatureRequestDTO
    {
        /// <summary>Base64-encoded image data (PNG recommended) of the signature.</summary>
        public string SignatureData { get; set; } = string.Empty;

        /// <summary>"Draw" (canvas) or "Type" (text-rendered).</summary>
        public string SignatureMethod { get; set; } = string.Empty;
    }
}
