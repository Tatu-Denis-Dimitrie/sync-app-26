using Microsoft.AspNetCore.Mvc;
using Moq;
using SyncApp26.API.Controllers;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.Tests.Controllers.Localization
{
    public class LocalizationControllerTests
    {
        private readonly Mock<ILocalizationService> _localizationServiceMock = new();

        private LocalizationController CreateController() => new(_localizationServiceMock.Object);

        [Fact]
        public void GetTranslations_ResolvesRequestedCodeThenReturnsCatalogueForIt()
        {
            var controller = CreateController();
            var catalogue = new Dictionary<string, IReadOnlyDictionary<string, string>>
            {
                [LocalizationScopes.Common] = new Dictionary<string, string> { ["greeting"] = "Hello" }
            };
            _localizationServiceMock.Setup(s => s.ResolveLanguage("en")).Returns(Language.En);
            _localizationServiceMock.Setup(s => s.GetTranslations(Language.En)).Returns(catalogue);

            var result = controller.GetTranslations("en");

            var ok = Assert.IsType<OkObjectResult>(result);
            Assert.Same(catalogue, ok.Value);
            _localizationServiceMock.Verify(s => s.ResolveLanguage("en"), Times.Once);
        }

        [Fact]
        public void GetTranslations_UnrecognizedCode_StillServesWhateverResolveLanguageFellBackTo()
        {
            // The controller never rejects an unrecognized code itself - ResolveLanguage's fallback
            // to Localization:DefaultLanguage is what keeps this a 200, not the controller's job.
            var controller = CreateController();
            _localizationServiceMock.Setup(s => s.ResolveLanguage("xx-not-real")).Returns(Language.En);
            _localizationServiceMock.Setup(s => s.GetTranslations(Language.En))
                .Returns(new Dictionary<string, IReadOnlyDictionary<string, string>>());

            var result = controller.GetTranslations("xx-not-real");

            Assert.IsType<OkObjectResult>(result);
        }
    }
}
