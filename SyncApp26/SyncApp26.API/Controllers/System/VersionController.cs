using Microsoft.AspNetCore.Mvc;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<VersionController> _logger;

    public VersionController(IWebHostEnvironment environment, ILogger<VersionController> logger)
    {
        _environment = environment;
        _logger = logger;
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
            return StatusCode(500, new { error = "Failed to read version" });
        }
    }
}
