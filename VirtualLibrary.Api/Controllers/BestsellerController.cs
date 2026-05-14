using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VirtualLibrary.Api.Services;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class BestsellerController : ControllerBase
{
    private readonly INytBooksService _nyt;

    public BestsellerController(INytBooksService nyt)
    {
        _nyt = nyt;
    }

    /// <summary>
    /// Returns the available NYT bestseller list names (e.g. "hardcover-fiction").
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetLists(CancellationToken ct)
    {
        var lists = await _nyt.GetAvailableListsAsync(ct);
        return Ok(lists);
    }

    /// <summary>
    /// Returns the current NYT bestseller list for <paramref name="listName"/>
    /// (e.g. "hardcover-fiction", "hardcover-nonfiction", "paperback-trade-fiction").
    /// Results are cached for 24 hours.
    /// </summary>
    [HttpGet("{listName}")]
    public async Task<IActionResult> GetList(string listName, CancellationToken ct)
    {
        var result = await _nyt.GetBestsellerListAsync(listName, ct);
        if (result == null) return NotFound(new { error = $"Bestseller list '{listName}' not found or NYT API key not configured." });
        return Ok(result);
    }
}
