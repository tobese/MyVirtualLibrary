using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Api.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class BooksController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly IOpenLibraryClient _ol;

    public BooksController(AppDbContext db, IOpenLibraryClient ol)
    {
        _db = db;
        _ol = ol;
    }

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier) ?? throw new InvalidOperationException("No user id");

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BookStatus? status, CancellationToken ct)
    {
        var query = _db.UserBooks
            .Where(ub => ub.UserId == UserId)
            .Include(ub => ub.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(ub => ub.Edition).ThenInclude(e => e.Covers)
            .Include(ub => ub.Edition).ThenInclude(e => e.Work)
            .AsQueryable();

        if (status.HasValue) query = query.Where(ub => ub.Status == status.Value);

        var list = await query.OrderByDescending(ub => ub.DateAdded).ToListAsync(ct);
        return Ok(list.Select(ub => ub.ToDto()));
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var ub = await _db.UserBooks
            .Include(x => x.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(x => x.Edition).ThenInclude(e => e.Covers)
            .Include(x => x.Edition).ThenInclude(e => e.Work)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();
        return Ok(ub.ToDto());
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddUserBookRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.Isbn))
            return BadRequest(new { error = "ISBN required" });

        var edition = await _ol.LookupByIsbnAsync(request.Isbn, ct);
        if (edition == null) return NotFound(new { error = "ISBN not found" });

        var existing = await _db.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == UserId && ub.EditionId == edition.Id, ct);
        if (existing != null)
        {
            existing.Status = request.Status;
            await _db.SaveChangesAsync(ct);
            return Ok(existing.ToDto());
        }

        var userBook = new UserBook
        {
            UserId = UserId,
            EditionId = edition.Id,
            Status = request.Status,
        };
        _db.UserBooks.Add(userBook);
        await _db.SaveChangesAsync(ct);

        // Reload with includes so the returned DTO has authors/covers.
        var withIncludes = await _db.UserBooks
            .Include(x => x.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(x => x.Edition).ThenInclude(e => e.Covers)
            .Include(x => x.Edition).ThenInclude(e => e.Work)
            .FirstAsync(x => x.Id == userBook.Id, ct);
        return Ok(withIncludes.ToDto());
    }

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserBookRequest request, CancellationToken ct)
    {
        var ub = await _db.UserBooks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();

        if (request.Status.HasValue) ub.Status = request.Status.Value;
        if (request.Rating.HasValue) ub.Rating = request.Rating.Value;
        if (request.Notes != null) ub.Notes = request.Notes;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ub = await _db.UserBooks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();
        _db.UserBooks.Remove(ub);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
