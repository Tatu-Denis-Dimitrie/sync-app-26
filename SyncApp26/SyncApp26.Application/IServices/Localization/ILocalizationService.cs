using Microsoft.Extensions.Localization;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.IServices
{
    /// <summary>
    /// Reads the resx-backed translation catalogue. Backing content is added scope by scope (see
    /// LocalizationScopes) as SyncApp26.Application/Resources/{Scope}.resx and {Scope}.{code}.resx
    /// files are authored - a scope with no resx yet simply comes back empty, it never breaks the
    /// caller.
    /// </summary>
    public interface ILocalizationService
    {
        /// <summary>
        /// Every scope, flattened to key -> value, for the requested language. Keys missing from a
        /// non-English scope file fall back to their English value so an incomplete translation never
        /// leaves a gap for the caller to guard against.
        /// </summary>
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetTranslations(Language language);

        /// <summary>
        /// A localizer scoped to a single resx base name (e.g. "Auth"), for backend code - validation
        /// messages, email templates - that only ever needs a handful of keys from one scope rather
        /// than the whole catalogue.
        /// </summary>
        IStringLocalizer GetScopedLocalizer(string scope);

        /// <summary>
        /// Turns a raw language code (from a URL segment, a stored preference, etc.) into a language
        /// this deployment actually serves - Localization:SupportedLanguages - falling back to
        /// Localization:DefaultLanguage for anything unrecognized, unsupported, or absent. Never
        /// throws: a typo'd or not-yet-shipped code degrades to the default instead of failing the
        /// caller.
        /// </summary>
        Language ResolveLanguage(string? requestedCode);
    }
}
