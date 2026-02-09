using Microsoft.AspNetCore.Mvc;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class VersionController : ControllerBase
{
    private readonly IWebHostEnvironment _environment;

    public VersionController(IWebHostEnvironment environment)
    {
        _environment = environment;
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
            return StatusCode(500, new { error = "Failed to read version", message = ex.Message });
        }
    }
}
