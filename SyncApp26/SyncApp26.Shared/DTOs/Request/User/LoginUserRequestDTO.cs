namespace SyncApp26.Shared.DTOs.Request.User
{
    public class LoginUserRequestDTO
    {
        public required string Email { get; set; }
        public required string Password { get; set; }
    }
}