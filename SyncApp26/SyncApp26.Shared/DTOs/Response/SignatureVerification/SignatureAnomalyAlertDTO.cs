namespace SyncApp26.Shared.DTOs.Response.SignatureVerification
{
    public class SignatureAnomalyAlertDTO
    {
        public Guid Id { get; set; }
        public int RecordsChecked { get; set; }
        public int AnomaliesFound { get; set; }
        public DateTimeOffset OccurredAt { get; set; }
    }
}
