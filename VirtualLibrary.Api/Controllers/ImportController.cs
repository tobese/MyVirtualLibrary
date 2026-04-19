using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualLibrary.Api.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class ImportController : ControllerBase
{
    private const int MaxIsbnsPerRequest = 500;

    private readonly IBulkImportService _importService;

    public ImportController(IBulkImportService importService)
    {
        _importService = importService;
    }

    private string UserId =>
        User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No user id");

    /// <summary>
    /// Bulk-import a list of ISBNs.
    ///
    /// For each ISBN the endpoint:
    ///   1. Fetches metadata from OpenLibrary (or refreshes it if
    ///      OpenLibrary reports a newer <c>last_modified</c> timestamp).
    ///   2. Adds the book to the calling user's library if not already present.
    ///
    /// Returns a per-row log plus a summary.  Capped at <c>500</c> ISBNs per
    /// request; blank strings and duplicates are stripped before processing.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> Import(
        [FromBody] BulkImportRequest request, CancellationToken ct)
    {
        var isbns = (request.Isbns ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxIsbnsPerRequest)
            .ToList();

        if (isbns.Count == 0)
            return BadRequest(new { error = "No valid ISBNs provided." });

        var result = await _importService.ImportAsync(
            UserId, isbns, request.DefaultStatus, request.DefaultIsOwned, ct);

        return Ok(result);
    }
}
