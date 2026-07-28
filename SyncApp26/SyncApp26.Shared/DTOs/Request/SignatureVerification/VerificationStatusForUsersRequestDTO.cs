namespace SyncApp26.Shared.DTOs.Request.SignatureVerification
{
    public class VerificationStatusForUsersRequestDTO
    {
        public List<Guid> UserIds { get; set; } = new();
    }
}
