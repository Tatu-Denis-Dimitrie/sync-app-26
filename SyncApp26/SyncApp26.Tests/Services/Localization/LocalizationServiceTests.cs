using System.Resources;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Localization;
using Moq;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Localization
{
    public class LocalizationServiceTests
    {
        private readonly Mock<IStringLocalizerFactory> _factoryMock = new();

        private static IConfiguration BuildConfiguration(string[]? supportedLanguages = null, string defaultLanguage = "En")
        {
            var data = new Dictionary<string, string?>
            {
                ["Localization:DefaultLanguage"] = defaultLanguage
            };

            var languages = supportedLanguages ?? new[] { "En" };
            for (var i = 0; i < languages.Length; i++)
            {
                data[$"Localization:SupportedLanguages:{i}"] = languages[i];
            }

            return new ConfigurationBuilder().AddInMemoryCollection(data).Build();
        }

        private LocalizationService CreateService(IConfiguration? configuration = null) =>
            new(_factoryMock.Object, configuration ?? BuildConfiguration());

        [Fact]
        public void GetTranslations_NoResxAuthoredYet_ReturnsEveryScopeEmptyInsteadOfThrowing()
        {
            _factoryMock.Setup(f => f.Create(It.IsAny<string>(), It.IsAny<string>()))
                .Throws(new MissingManifestResourceException());

            var result = CreateService().GetTranslations(Language.En);

            Assert.Equal(LocalizationScopes.All.Count, result.Count);
            foreach (var scope in LocalizationScopes.All)
            {
                Assert.True(result.ContainsKey(scope));
                Assert.Empty(result[scope]);
            }
        }

        [Fact]
        public void GetScopedLocalizer_PassesScopeAsBaseNameAndOwnAssemblyAsLocation()
        {
            var localizerMock = new Mock<IStringLocalizer>();
            _factoryMock.Setup(f => f.Create(LocalizationScopes.Auth, typeof(LocalizationService).Assembly.GetName().Name!))
                .Returns(localizerMock.Object);

            var result = CreateService().GetScopedLocalizer(LocalizationScopes.Auth);

            Assert.Same(localizerMock.Object, result);
            _factoryMock.VerifyAll();
        }

        [Fact]
        public void GetTranslations_English_ReadsAllStringsUnderInvariantCulture()
        {
            var localizerMock = new Mock<IStringLocalizer>();
            localizerMock.Setup(l => l.GetAllStrings(false)).Returns(
                new[] { new LocalizedString("greeting", "Hello"), new LocalizedString("farewell", "Goodbye") });

            _factoryMock.Setup(f => f.Create(LocalizationScopes.Common, It.IsAny<string>()))
                .Returns(localizerMock.Object);
            _factoryMock.Setup(f => f.Create(It.Is<string>(s => s != LocalizationScopes.Common), It.IsAny<string>()))
                .Throws(new MissingManifestResourceException());

            var result = CreateService().GetTranslations(Language.En);

            Assert.Equal("Hello", result[LocalizationScopes.Common]["greeting"]);
            Assert.Equal("Goodbye", result[LocalizationScopes.Common]["farewell"]);
        }

        [Fact]
        public void ResolveLanguage_SupportedCode_ReturnsParsedLanguageCaseInsensitively()
        {
            var service = CreateService(BuildConfiguration(new[] { "En" }, "En"));

            Assert.Equal(Language.En, service.ResolveLanguage("en"));
            Assert.Equal(Language.En, service.ResolveLanguage("EN"));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not-a-real-code")]
        [InlineData("ro")] // not defined on Language yet, so it can never be "supported" either
        public void ResolveLanguage_UnrecognizedOrUnsupportedCode_FallsBackToConfiguredDefault(string? requestedCode)
        {
            var service = CreateService(BuildConfiguration(new[] { "En" }, "En"));

            Assert.Equal(Language.En, service.ResolveLanguage(requestedCode));
        }

        [Fact]
        public void ResolveLanguage_CodeDefinedButNotInSupportedList_FallsBackToDefault()
        {
            var service = CreateService(BuildConfiguration(Array.Empty<string>(), "En"));

            Assert.Equal(Language.En, service.ResolveLanguage("en"));
        }
    }
}
