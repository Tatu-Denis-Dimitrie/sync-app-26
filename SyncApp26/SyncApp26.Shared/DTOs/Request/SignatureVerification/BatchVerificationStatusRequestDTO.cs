namespace SyncApp26.Shared.DTOs.Request.SignatureVerification
{
    public class BatchVerificationStatusRequestDTO
    {
        public List<Guid> SignatureIds { get; set; } = new();
    }
}
