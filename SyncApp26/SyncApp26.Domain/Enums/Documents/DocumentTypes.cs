namespace SyncApp26.Domain.Enums
{
    /// <summary>
    /// The two document families the SSM/SU signing flow is built around, as stored verbatim on
    /// UserDocument.DocumentType. Code that compares against "SSM"/"SU" should go through Normalize
    /// (or the Is* helpers) instead of hardcoding the literal, so casing/whitespace handling can't
    /// quietly diverge between call sites.
    /// </summary>
    public static class DocumentTypes
    {
        public const string Ssm = "SSM";
        public const string Su = "SU";

        /// <summary>Case/whitespace-tolerant parse to the canonical constant, or null if unrecognized.</summary>
        public static string? Normalize(string? documentType) => documentType?.Trim().ToUpperInvariant() switch
        {
            Ssm => Ssm,
            Su => Su,
            _ => null
        };

        public static bool IsSsm(string? documentType) => Normalize(documentType) == Ssm;

        public static bool IsSu(string? documentType) => Normalize(documentType) == Su;
    }
}
