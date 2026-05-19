namespace SyncApp26.Domain.Entities
{
    public class DocumentSignatureToken
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Email { get; set; } = string.Empty;
        
        // This links to the future SSM/SU Document entity
        public Guid DocumentId { get; set; }

        // The specific PeriodicTraining row this token is authorizing to sign
        public Guid? PeriodicTrainingId { get; set; }

        // Example: The name of the file so we can show it in the email
        public string DocumentName { get; set; } = string.Empty;
        
        public string Token { get; set; } = string.Empty;
        
        public DateTime ExpiresAt { get; set; }
        
        public bool IsUsed { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
