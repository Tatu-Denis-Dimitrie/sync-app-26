using System.Resources;
using Microsoft.Extensions.Localization;
using Moq;
using SyncApp26.Application.Services;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Services.Localization
{
    public class LocalizationServiceTests
    {
        private readonly Mock<IStringLocalizerFactory> _factoryMock = new();

        private LocalizationService CreateService() => new(_factoryMock.Object);

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
    }
}
