using System.Text.Json.Serialization;

namespace SyncApp26.Domain.Enums
{
    /// <summary>
    /// Languages the app can serve translated content for. Adding a language means adding a member
    /// here, adding the matching *.&lt;code&gt;.resx file for every scope, and listing it in the
    /// Localization:SupportedLanguages config section - nothing else in the pipeline changes.
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public enum Language
    {
        En
    }
}
