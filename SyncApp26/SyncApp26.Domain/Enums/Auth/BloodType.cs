using System.Text.Json.Serialization;

namespace SyncApp26.Domain.Enums
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum BloodType
    {
        APositive,
        ANegative,
        BPositive,
        BNegative,
        ABPositive,
        ABNegative,
        OPositive,
        ONegative
    }

    public static class BloodTypeExtensions
    {
        public static string ToDisplayString(this BloodType bloodType) => bloodType switch
        {
            BloodType.APositive => "A+",
            BloodType.ANegative => "A-",
            BloodType.BPositive => "B+",
            BloodType.BNegative => "B-",
            BloodType.ABPositive => "AB+",
            BloodType.ABNegative => "AB-",
            BloodType.OPositive => "O+",
            BloodType.ONegative => "O-",
            _ => bloodType.ToString()
        };
    }
}
