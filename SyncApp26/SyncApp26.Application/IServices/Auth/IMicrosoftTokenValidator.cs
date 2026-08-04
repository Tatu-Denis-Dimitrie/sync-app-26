namespace SyncApp26.Application.IServices
{
    public class MicrosoftTokenPayload
    {
        public string Email { get; init; } = string.Empty;
    }

    public interface IMicrosoftTokenValidator
    {
        Task<MicrosoftTokenPayload?> ValidateAsync(string idToken);
    }
}
