using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Enums
{
    public class BloodTypeExtensionsTests
    {
        [Theory]
        [InlineData(BloodType.APositive, "A+")]
        [InlineData(BloodType.ANegative, "A-")]
        [InlineData(BloodType.BPositive, "B+")]
        [InlineData(BloodType.BNegative, "B-")]
        [InlineData(BloodType.ABPositive, "AB+")]
        [InlineData(BloodType.ABNegative, "AB-")]
        [InlineData(BloodType.OPositive, "O+")]
        [InlineData(BloodType.ONegative, "O-")]
        public void ToDisplayString_KnownValue_ReturnsExpectedLabel(BloodType bloodType, string expected)
        {
            Assert.Equal(expected, bloodType.ToDisplayString());
        }
    }
}
