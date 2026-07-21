using System;
using System.Globalization;
using System.Text;

namespace SyncApp26.Application.Services
{
    /// <summary>
    /// The exact set of values bound into a signature's hash. Fields must be captured once, at
    /// creation time, and stored as-is — never re-derived from live data on each verification,
    /// or the hash would track whatever the data happens to be today instead of what was
    /// actually signed.
    /// </summary>
    public sealed record SignatureCanonicalInput(
        Guid SignerUserId,
        string SignerFullNameSnapshot,
        string SignerPositionSnapshot,
        string? MaterialTaughtSnapshot,
        decimal? DurationHoursSnapshot,
        DateTime? TrainingDateSnapshot,
        DateTimeOffset SignedAt,
        string? PreviousSignatureHash);

    /// <summary>
    /// Turns a SignatureCanonicalInput into a deterministic byte sequence suitable for keyed
    /// hashing: fixed field order, invariant formatting, and length-prefixed fields so that no
    /// two distinct inputs can ever serialize to the same output.
    /// </summary>
    public static class SignatureCanonicalSerializer
    {
        public static string Serialize(SignatureCanonicalInput input)
        {
            var sb = new StringBuilder();
            AppendField(sb, input.SignerUserId.ToString("D"));
            AppendField(sb, input.SignerFullNameSnapshot);
            AppendField(sb, input.SignerPositionSnapshot);
            AppendField(sb, input.MaterialTaughtSnapshot);
            AppendField(sb, FormatDuration(input.DurationHoursSnapshot));
            AppendField(sb, FormatTrainingDate(input.TrainingDateSnapshot));
            AppendField(sb, input.SignedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendField(sb, input.PreviousSignatureHash);
            return sb.ToString();
        }

        public static byte[] SerializeToUtf8Bytes(SignatureCanonicalInput input) =>
            Encoding.UTF8.GetBytes(Serialize(input));

        // Length-prefixing (byte count, not char count) makes field boundaries unambiguous
        // regardless of what characters the values themselves contain.
        private static void AppendField(StringBuilder sb, string? value)
        {
            var v = value ?? string.Empty;
            var byteCount = Encoding.UTF8.GetByteCount(v);
            sb.Append(byteCount.ToString(CultureInfo.InvariantCulture)).Append(':').Append(v);
        }

        private static string? FormatDuration(decimal? value) =>
            value?.ToString("F2", CultureInfo.InvariantCulture);

        // Training date is a calendar date, not a precise instant — format as date-only to
        // avoid timezone ambiguity that doesn't apply to the underlying value.
        private static string? FormatTrainingDate(DateTime? value) =>
            value?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
    }
}
