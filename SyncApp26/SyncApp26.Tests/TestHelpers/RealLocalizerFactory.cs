using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using SyncApp26.Application.IServices;
using SyncApp26.Application.Services;

namespace SyncApp26.Tests.TestHelpers
{
    /// <summary>
    /// Builds a real, resx-backed IStringLocalizer for a scope - used by service tests that need
    /// ILocalizationService.GetScopedLocalizer(scope) to return actual English text instead of a
    /// mock. Deliberately reads the real Resources/*.resx files rather than a hardcoded fake
    /// dictionary: a message that drifts from what the resx actually says fails these tests, the
    /// same way a Mock<IStringLocalizer> returning canned strings never would.
    /// </summary>
    public static class RealLocalizerFactory
    {
        public static IStringLocalizer ForScope(string scope)
        {
            var factory = new ResourceManagerStringLocalizerFactory(
                Options.Create(new LocalizationOptions { ResourcesPath = "Resources" }),
                NullLoggerFactory.Instance);

            return factory.Create(scope, typeof(LocalizationService).Assembly.GetName().Name!);
        }

        public static ILocalizationService LocalizationService()
        {
            var mock = new Mock<ILocalizationService>();
            mock.Setup(s => s.GetScopedLocalizer(It.IsAny<string>()))
                .Returns<string>(ForScope);
            return mock.Object;
        }

        public static IServiceProvider ServiceProvider()
        {
            var services = new ServiceCollection();
            services.AddSingleton(LocalizationService());
            return services.BuildServiceProvider();
        }
    }
}
