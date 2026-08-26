using Microsoft.AspNetCore.Mvc;
using SyncApp26.Application.IServices;

namespace SyncApp26.API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LocalizationController : ControllerBase
{
    private readonly ILocalizationService _localizationService;

    public LocalizationController(ILocalizationService localizationService)
    {
        _localizationService = localizationService;
    }

    [HttpGet("{lang}")]
    public IActionResult GetTranslations(string lang)
    {
        var language = _localizationService.ResolveLanguage(lang);
        return Ok(_localizationService.GetTranslations(language));
    }
}
