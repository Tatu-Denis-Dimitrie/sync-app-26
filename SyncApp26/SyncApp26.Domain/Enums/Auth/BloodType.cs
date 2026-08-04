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
}
