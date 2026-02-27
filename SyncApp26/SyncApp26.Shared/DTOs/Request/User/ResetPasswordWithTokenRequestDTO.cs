namespace SyncApp26.Shared.DTOs.Request.User
{
    public class ResetPasswordWithTokenRequestDTO
    {
        public required string Email { get; set; }
        public required string Token { get; set; }
        public required string NewPassword { get; set; }
    }
}