namespace SyncApp26.Application.IServices
{
    public class GoogleTokenPayload
    {
        public string Email { get; init; } = string.Empty;
        public bool EmailVerified { get; init; }
    }

    public interface IGoogleTokenValidator
    {
        Task<GoogleTokenPayload?> ValidateAsync(string idToken);
    }
}
