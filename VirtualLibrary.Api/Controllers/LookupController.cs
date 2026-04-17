using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class LookupController : ControllerBase
{
    private readonly IOpenLibraryClient _ol;

    public LookupController(IOpenLibraryClient ol)
    {
        _ol = ol;
    }

    /// <summary>
    /// Look up book metadata by ISBN (ISBN-10 or ISBN-13).
    /// Writes through to our cache tables on first fetch.
    /// </summary>
    [HttpPost("{isbn}")]
    public async Task<IActionResult> LookupIsbn(string isbn, CancellationToken ct)
    {
        var edition = await _ol.LookupByIsbnAsync(isbn, ct);
        if (edition == null) return NotFound(new { error = "ISBN not found" });

        var dto = edition.ToDto();
        var work = edition.Work?.ToDto();
        return Ok(new IsbnLookupResponse(dto, work));
    }
}
