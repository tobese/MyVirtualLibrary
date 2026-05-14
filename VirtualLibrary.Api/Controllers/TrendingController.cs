using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualLibrary.Api.Services;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class TrendingController : ControllerBase
{
    private readonly IOpenLibraryClient _ol;

    public TrendingController(IOpenLibraryClient ol)
    {
        _ol = ol;
    }

    /// <summary>
    /// Returns trending works from Open Library.
    /// <paramref name="period"/> must be "daily" or "weekly" (defaults to "weekly").
    /// Results are cached for 1 hour.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetTrending(
        [FromQuery] string period = "weekly",
        CancellationToken ct = default)
    {
        var result = await _ol.GetTrendingAsync(period, ct);
        return Ok(result);
    }
}
