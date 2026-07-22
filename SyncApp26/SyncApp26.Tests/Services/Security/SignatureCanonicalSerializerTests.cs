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
            string? previousHash = null) => new(
                signerUserId ?? Guid.Parse("11111111-1111-1111-1111-111111111111"),
                fullName,
                position,
                material,
                duration,
                trainingDate ?? new DateTime(2026, 1, 15),
                signedAt ?? new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero),
                previousHash);

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

            Assert.Equal(SignatureCanonicalSerializer.Serialize(utc), SignatureCanonicalSerializer.Serialize(plusTwo));
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
    }
}
