using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Localization;
using SyncApp26.Application.IServices;
using SyncApp26.Domain.Enums;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<VersionController> _logger;
    private readonly IStringLocalizer _localizer;

    public VersionController(IWebHostEnvironment environment, ILogger<VersionController> logger, ILocalizationService localizationService)
    {
        _environment = environment;
        _logger = logger;
        _localizer = localizationService.GetScopedLocalizer(LocalizationScopes.Common);
    }

    [HttpGet]
    public IActionResult GetVersion()
    {
        try
        {
            var versionFilePath = Path.Combine(_environment.ContentRootPath, "..", "VERSION");
            if (System.IO.File.Exists(versionFilePath))
            {
                var version = System.IO.File.ReadAllText(versionFilePath).Trim();
                return Ok(new { version });
            }
            return Ok(new { version = "1.0.0" }); // Fallback version
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to read version file.");
            return StatusCode(500, new { error = _localizer["errors.failedToReadVersion"].Value });
        }
    }
}
