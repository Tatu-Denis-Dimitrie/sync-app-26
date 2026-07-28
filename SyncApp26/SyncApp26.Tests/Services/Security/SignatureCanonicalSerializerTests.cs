using SyncApp26.Application.Services;

namespace SyncApp26.Tests.Services.Security
{
    public class SignatureCanonicalSerializerTests
    {
        private static SignatureCanonicalInput MakeInput(
            Guid? signerUserId = null,
            string fullName = "Adela Popescu",
            string position = "Operator",
            string? material = "Norme SSM generale",
            decimal? duration = 2m,
            DateTime? trainingDate = null,
            DateTimeOffset? signedAt = null,
            string? previousHash = null,
            int? version = null) => new(
                signerUserId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                fullName,
                position,
                material,
                duration,
                trainingDate ?? new DateTime(2026, 1, 15),
                signedAt ?? new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
                previousHash,
                version ?? SignatureCanonicalSerializer.CurrentVersion);

        [Fact]
        public void Serialize_SameInput_ProducesIdenticalString()
        {
            var input = MakeInput();

            var first = SignatureCanonicalSerializer.Serialize(input);
            var second = SignatureCanonicalSerializer.Serialize(input);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Serialize_SameInstantDifferentOffset_ProducesIdenticalString()
        {
            var utc = MakeInput(signedAt: new DateTimeOffset(2026, 1, 15, 12, 30, 0, TimeSpan.Zero));
            var plusTwo = MakeInput(signedAt: new DateTimeOffset(2026, 1, 15, 14, 30, 0, TimeSpan.FromHours(2)));

            Assert.Equal(
                SignatureCanonicalSerializer.Serialize(utc),
                SignatureCanonicalSerializer.Serialize(plusTwo));
        }

        [Fact]
        public void Serialize_DifferentFieldBoundarySplit_DoesNotCollide()
        {
            // "ab" + "c" and "a" + "bc" would hash identically under naive concatenation.
            var first = MakeInput(fullName: "ab", position: "c");
            var second = MakeInput(fullName: "a", position: "bc");

            Assert.NotEqual(
                SignatureCanonicalSerializer.Serialize(first),
                SignatureCanonicalSerializer.Serialize(second));
        }

        [Fact]
        public void Serialize_NullOptionalFields_DoesNotThrowAndStaysDeterministic()
        {
            var input = MakeInput(material: null, duration: null, trainingDate: null, previousHash: null);

            var first = SignatureCanonicalSerializer.Serialize(input);
            var second = SignatureCanonicalSerializer.Serialize(input);

            Assert.Equal(first, second);
        }

        [Fact]
        public void Serialize_ChangedDuration_ProducesDifferentString()
        {
            var twoHours = MakeInput(duration: 2m);
            var oneHour = MakeInput(duration: 1m);

            Assert.NotEqual(
                SignatureCanonicalSerializer.Serialize(twoHours),
                SignatureCanonicalSerializer.Serialize(oneHour));
        }

        [Fact]
        public void SerializeToUtf8Bytes_MatchesUtf8OfSerializedString()
        {
            var input = MakeInput(fullName: "Ștefan Ionescu");

            var bytes = SignatureCanonicalSerializer.SerializeToUtf8Bytes(input);
            var expected = System.Text.Encoding.UTF8.GetBytes(SignatureCanonicalSerializer.Serialize(input));

            Assert.Equal(expected, bytes);
        }

        [Fact]
        public void Serialize_UnknownVersion_Throws()
        {
            var input = MakeInput(version: 999);

            Assert.Throws<NotSupportedException>(() => SignatureCanonicalSerializer.Serialize(input));
        }

        [Fact]
        public void Serialize_BindsVersionNumberAsFirstField()
        {
            var input = MakeInput(version: 1);

            var output = SignatureCanonicalSerializer.Serialize(input);

            Assert.StartsWith("1:1", output);
        }

        [Fact]
        public void CurrentVersion_Is1()
        {
            // Locks in which version new signatures are made under today — if this changes, it
            // should be a deliberate version bump (see the class doc comment), not an accident.
            Assert.Equal(1, SignatureCanonicalSerializer.CurrentVersion);
        }

        [Fact]
        public void SerializeV1_MustNeverChange_MatchesIndependentlyRebuiltFormat()
        {
            // V1 is permanently frozen once any real signature exists under it — every one of them
            // depends on reproducing this exact byte layout to still verify. The expected string is
            // rebuilt here independently (length-prefix arithmetic done fresh, not by calling
            // AppendField) so this test would actually catch an accidental edit to SerializeV1, not
            // just mirror whatever it currently does.
            var input = MakeInput(fullName: "Ștefan Ionescu", version: 1);

            string Field(string? value)
            {
                var v = value ?? string.Empty;
                return $"{System.Text.Encoding.UTF8.GetByteCount(v)}:{v}";
            }

            var expected =
                Field("1") +
                Field(input.SignerUserId.ToString("D")) +
                Field(input.SignerFullNameSnapshot) +
                Field(input.SignerPositionSnapshot) +
                Field(input.MaterialTaughtSnapshot) +
                Field(input.DurationHoursSnapshot!.Value.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)) +
                Field(input.TrainingDateSnapshot!.Value.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture)) +
                Field(input.SignedAt.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture)) +
                Field(input.PreviousSignatureHash);

            Assert.Equal(expected, SignatureCanonicalSerializer.Serialize(input));
        }

        [Fact]
        public void Serialize_V1AndV2_ProduceDifferentOutputForSameLogicalFields()
        {
            // V2 exists specifically to prove the version-dispatch mechanism (see
            // SignatureVerificationServiceTests' mixed-version tests) — this confirms it actually
            // picks a genuinely different formula, not the same one twice under a different label.
            var v1 = MakeInput(version: 1);
            var v2 = MakeInput(version: 2);

            Assert.NotEqual(
                SignatureCanonicalSerializer.Serialize(v1),
                SignatureCanonicalSerializer.Serialize(v2));
        }

        [Fact]
        public void Serialize_V2_FormatsSignedAtAsUnixSeconds()
        {
            var signedAt = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero);
            var input = MakeInput(signedAt: signedAt, version: 2);

            var output = SignatureCanonicalSerializer.Serialize(input);

            var expectedUnixSeconds = signedAt.ToUnixTimeSeconds().ToString();
            Assert.Contains($"{expectedUnixSeconds.Length}:{expectedUnixSeconds}", output);
        }
    }
}
