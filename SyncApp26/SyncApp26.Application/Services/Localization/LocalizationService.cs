using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
    public class LocalizationService : ILocalizationService
    {
        private static readonly string ResourceAssemblyName = typeof(LocalizationService).Assembly.GetName().Name!;

        private readonly IStringLocalizerFactory _localizerFactory;
        private readonly IConfiguration _configuration;

        public LocalizationService(IStringLocalizerFactory localizerFactory, IConfiguration configuration)
        {
            _localizerFactory = localizerFactory;
            _configuration = configuration;
        }

        public Language ResolveLanguage(string? requestedCode)
        {
            var supportedLanguages = _configuration.GetSection("Localization:SupportedLanguages").Get<string[]>()
                ?? Array.Empty<string>();

            if (requestedCode != null &&
                Enum.TryParse<Language>(requestedCode, ignoreCase: true, out var requestedLanguage) &&
                supportedLanguages.Contains(requestedLanguage.ToString(), StringComparer.OrdinalIgnoreCase))
            {
                return requestedLanguage;
            }

            var defaultLanguageCode = _configuration["Localization:DefaultLanguage"];
            return Enum.TryParse<Language>(defaultLanguageCode, ignoreCase: true, out var defaultLanguage)
                ? defaultLanguage
                : Language.En;
        }

        public IStringLocalizer GetScopedLocalizer(string scope) =>
            _localizerFactory.Create(scope, ResourceAssemblyName);

        public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> GetTranslations(Language language)
        {
            var result = new Dictionary<string, IReadOnlyDictionary<string, string>>();

            foreach (var scope in LocalizationScopes.All)
            {
                result[scope] = GetScopeValues(scope, language);
            }

            return result;
        }

        private IReadOnlyDictionary<string, string> GetScopeValues(string scope, Language language)
        {
            try
            {
                var localizer = GetScopedLocalizer(scope);
                var values = ReadAllStrings(localizer, ToCulture(language));

                if (language != Language.En)
                {
                    foreach (var fallback in ReadAllStrings(localizer, CultureInfo.InvariantCulture))
                    {
                        values.TryAdd(fallback.Key, fallback.Value);
                    }
                }

                return values;
            }
            catch (MissingManifestResourceException)
            {
                return new Dictionary<string, string>();
            }
        }

        private static Dictionary<string, string> ReadAllStrings(IStringLocalizer localizer, CultureInfo culture)
        {
            var originalCulture = CultureInfo.CurrentUICulture;
            try
            {
                CultureInfo.CurrentUICulture = culture;
                return localizer.GetAllStrings(includeParentCultures: false)
                    .ToDictionary(s => s.Name, s => s.Value);
            }
            finally
            {
                CultureInfo.CurrentUICulture = originalCulture;
            }
        }

        private static CultureInfo ToCulture(Language language) =>
            language == Language.En
                ? CultureInfo.InvariantCulture
                : new CultureInfo(language.ToString().ToLowerInvariant());
    }
}
