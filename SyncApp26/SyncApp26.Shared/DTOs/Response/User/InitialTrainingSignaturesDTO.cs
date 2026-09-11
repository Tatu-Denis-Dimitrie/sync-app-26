using System;
using System.Collections.Generic;

namespace SyncApp26.Shared.DTOs.Response.User
{
    /// <summary>One signature plus the identity/date the "Digitally signed by" caption is built from.</summary>
    public class InitialTrainingSignatureBlockDTO
    {
        public string? SignerName { get; set; }
        public string? SignerFunction { get; set; }
        public string? SignatureData { get; set; }
        public string? SignatureMethod { get; set; }
        public DateTime? SignedAtUtc { get; set; }
    }

    /// <summary>Signature line-up for one training item. <see cref="Verifier"/> is null for SU.</summary>
    public class InitialTrainingSignatureSetDTO
    {
        public InitialTrainingSignatureBlockDTO? Trainee { get; set; }
        public InitialTrainingSignatureBlockDTO? Trainer { get; set; }
        public InitialTrainingSignatureBlockDTO? Verifier { get; set; }
    }

    /// <summary>Signatures for one document type, resolved server-side the same way the PDF resolves them.</summary>
    public class InitialTrainingSignaturesDTO
    {
        public string DocumentType { get; set; } = string.Empty;
        public InitialTrainingSignatureSetDTO? Introductory { get; set; }
        public InitialTrainingSignatureSetDTO? Workplace { get; set; }
        public InitialTrainingSignatureBlockDTO? AdmittedToWork { get; set; }
        public DateTime? AdmittedDate { get; set; }
    }
}
