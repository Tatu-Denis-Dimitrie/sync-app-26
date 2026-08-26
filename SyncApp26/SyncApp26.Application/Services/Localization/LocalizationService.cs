using System.Globalization;
using System.Resources;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Application.Services
{
    public class LocalizationService : ILocalizationService
    {
        private static readonly string ResourceAssemblyName = typeof(LocalizationService).Assembly.GetName().Name!;

        private readonly IStringLocalizerFactory _localizerFactory;

        public LocalizationService(IStringLocalizerFactory localizerFactory)
        {
            _localizerFactory = localizerFactory;
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
