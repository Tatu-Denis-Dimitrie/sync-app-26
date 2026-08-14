using System;
using System.Globalization;
using System.Text;

namespace SyncApp26.Application.Services
{
    /// <summary>
    /// The exact set of values bound into a signature's hash. Fields must be captured once, at
    /// creation time, and stored as-is — never re-derived from live data on each verification,
    /// or the hash would track whatever the data happens to be today instead of what was
    /// actually signed. Version travels with the rest of the input (not as a separate parameter)
    /// so there is exactly one place that can say which schema a given hash was computed under.
    /// </summary>
    public sealed record SignatureCanonicalInput(
        Guid SignerUserId,
        string SignerFullNameSnapshot,
        string SignerPositionSnapshot,
        string? SignerBadgeNumberSnapshot,
        string? SignerWorkSiteNameSnapshot,
        string? MaterialTaughtSnapshot,
        decimal? DurationHoursSnapshot,
        DateTime? TrainingDateSnapshot,
        DateTimeOffset SignedAt,
        string? PreviousSignatureHash,
        int Version);

    /// <summary>
    /// Turns a SignatureCanonicalInput into a deterministic byte sequence suitable for keyed
    /// hashing: fixed field order, invariant formatting, and length-prefixed fields so that no
    /// two distinct inputs can ever serialize to the same output.
    /// </summary>
    public static class SignatureCanonicalSerializer
    {
        /// <summary>The schema version new signatures are created with. Bump this — and add a new
        /// SerializeVN case below — when the field set changes; never edit an existing case.</summary>
        public const int CurrentVersion = 3;

        public static string Serialize(SignatureCanonicalInput input)
        {
            return input.Version switch
            {
                1 => SerializeV1(input),
                2 => SerializeV2(input),
                3 => SerializeV3(input),
                _ => throw new NotSupportedException($"Unknown signature canonical schema version {input.Version}.")
            };
        }

        public static byte[] SerializeToUtf8Bytes(SignatureCanonicalInput input) =>
            Encoding.UTF8.GetBytes(Serialize(input));

        private static string SerializeV1(SignatureCanonicalInput input)
        {
            var sb = new StringBuilder();
            AppendField(sb, input.Version.ToString(CultureInfo.InvariantCulture));
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

        // V1 plus the signer's badge number. Frozen from here on, same as V1 — V1 records must
        // keep verifying against SerializeV1, which is why the field is appended here rather than
        // added to the shared field list.
        private static string SerializeV2(SignatureCanonicalInput input)
        {
            var sb = new StringBuilder();
            AppendField(sb, input.Version.ToString(CultureInfo.InvariantCulture));
            AppendField(sb, input.SignerUserId.ToString("D"));
            AppendField(sb, input.SignerFullNameSnapshot);
            AppendField(sb, input.SignerPositionSnapshot);
            AppendField(sb, input.SignerBadgeNumberSnapshot);
            AppendField(sb, input.MaterialTaughtSnapshot);
            AppendField(sb, FormatDuration(input.DurationHoursSnapshot));
            AppendField(sb, FormatTrainingDate(input.TrainingDateSnapshot));
            AppendField(sb, input.SignedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendField(sb, input.PreviousSignatureHash);
            return sb.ToString();
        }

        // V2 plus the signer's work-site name at the moment they signed. Frozen from here on,
        // same as V1/V2 — reassigning the signer to a different work site later must not
        // retroactively invalidate a past signature, so this is a name snapshot (mirrors
        // SignerPositionSnapshot storing Function?.Name rather than an id), not a live lookup.
        private static string SerializeV3(SignatureCanonicalInput input)
        {
            var sb = new StringBuilder();
            AppendField(sb, input.Version.ToString(CultureInfo.InvariantCulture));
            AppendField(sb, input.SignerUserId.ToString("D"));
            AppendField(sb, input.SignerFullNameSnapshot);
            AppendField(sb, input.SignerPositionSnapshot);
            AppendField(sb, input.SignerBadgeNumberSnapshot);
            AppendField(sb, input.SignerWorkSiteNameSnapshot);
            AppendField(sb, input.MaterialTaughtSnapshot);
            AppendField(sb, FormatDuration(input.DurationHoursSnapshot));
            AppendField(sb, FormatTrainingDate(input.TrainingDateSnapshot));
            AppendField(sb, input.SignedAt.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture));
            AppendField(sb, input.PreviousSignatureHash);
            return sb.ToString();
        }

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
