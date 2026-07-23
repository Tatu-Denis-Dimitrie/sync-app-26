namespace SyncApp26.Shared.DTOs.Response.SignatureVerification
{
    public class DocumentSignatureIdsDTO
    {
        public Guid? UserSignatureId { get; set; }
        public Guid? ManagerSignatureId { get; set; }
        public Guid? AdminSignatureId { get; set; }
    }
}
