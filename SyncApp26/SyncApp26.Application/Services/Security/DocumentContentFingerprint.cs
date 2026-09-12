using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using SyncApp26.Domain.Entities;

namespace SyncApp26.Application.Services
{
    /// <summary>Attestation content only: initial-training entries and the admission date. No people, no bio data, no signatures.</summary>
    public sealed record DocumentContentInput(
        string DocumentType,
        DateTime? IntroductoryTrainingDate,
        int? IntroductoryTrainingHours,
        string? IntroductoryTrainingContent,
        DateTime? WorkplaceTrainingDate,
        string? WorkplaceTrainingLocation,
        int? WorkplaceTrainingHours,
        string? WorkplaceTrainingContent,
        DateTime? AdmittedDate)
    {
        /// <summary>Requires InitialTrainings to be loaded.</summary>
        public static DocumentContentInput FromUser(User user, string documentType)
        {
            var it = user.InitialTrainings?.FirstOrDefault(t => t.DocumentType == documentType);

            return new DocumentContentInput(
                documentType,
                it?.IntroductoryTrainingDate,
                it?.IntroductoryTrainingHours,
                it?.IntroductoryTrainingContent,
                it?.WorkplaceTrainingDate,
                it?.WorkplaceTrainingLocation,
                it?.WorkplaceTrainingHours,
                it?.WorkplaceTrainingContent,
                user.AdmittedDate);
        }
    }

    /// <summary>SHA-256 of a canonical serialization of DocumentContentInput. Frozen at signing, recomputed at verification; layout is part of schema V4.</summary>
    public static class DocumentContentFingerprint
    {
        public static string Compute(DocumentContentInput input)
        {
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(Serialize(input)));
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public static string Serialize(DocumentContentInput input)
        {
            var sb = new StringBuilder();
            AppendField(sb, "1"); // layout version
            AppendField(sb, input.DocumentType);
            AppendField(sb, FormatDate(input.IntroductoryTrainingDate));
            AppendField(sb, FormatInt(input.IntroductoryTrainingHours));
            AppendField(sb, input.IntroductoryTrainingContent);
            AppendField(sb, FormatDate(input.WorkplaceTrainingDate));
            AppendField(sb, input.WorkplaceTrainingLocation);
            AppendField(sb, FormatInt(input.WorkplaceTrainingHours));
            AppendField(sb, input.WorkplaceTrainingContent);
            AppendField(sb, FormatDate(input.AdmittedDate));
            return sb.ToString();
        }

        private static void AppendField(StringBuilder sb, string? value)
        {
            var v = value ?? string.Empty;
            sb.Append(Encoding.UTF8.GetByteCount(v).ToString(CultureInfo.InvariantCulture)).Append(':').Append(v);
        }

        private static string? FormatDate(DateTime? value) =>
            value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        private static string? FormatInt(int? value) =>
            value?.ToString(CultureInfo.InvariantCulture);
    }
}
