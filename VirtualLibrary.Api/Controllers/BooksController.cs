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

    // ── List ──────────────────────────────────────────────────────────────────

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] BookStatus? status, [FromQuery] bool? isOwned, CancellationToken ct)
    {
        var query = _db.UserBooks
            .Where(ub => ub.UserId == UserId)
            .Include(ub => ub.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(ub => ub.Edition).ThenInclude(e => e.Covers)
            .Include(ub => ub.Edition).ThenInclude(e => e.Work)
            .Include(ub => ub.ReadRecords)
            .AsQueryable();

        if (status.HasValue)  query = query.Where(ub => ub.Status == status.Value);
        if (isOwned.HasValue) query = query.Where(ub => ub.IsOwned == isOwned.Value);

        var list = await query.OrderByDescending(ub => ub.DateAdded).ToListAsync(ct);
        return Ok(list.Select(ub => ub.ToDto()));
    }

    // ── Get ───────────────────────────────────────────────────────────────────

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken ct)
    {
        var ub = await _db.UserBooks
            .Include(x => x.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(x => x.Edition).ThenInclude(e => e.Covers)
            .Include(x => x.Edition).ThenInclude(e => e.Work)
            .Include(x => x.ReadRecords)
            .FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();
        return Ok(ub.ToDto());
    }

    // ── Add ───────────────────────────────────────────────────────────────────

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
            existing.Status  = request.Status;
            existing.IsOwned = request.IsOwned;
            await _db.SaveChangesAsync(ct);
            return Ok(existing.ToDto());
        }

        var userBook = new UserBook
        {
            UserId   = UserId,
            EditionId = edition.Id,
            Status   = request.Status,
            IsOwned  = request.IsOwned,
        };
        _db.UserBooks.Add(userBook);
        await _db.SaveChangesAsync(ct);

        var withIncludes = await _db.UserBooks
            .Include(x => x.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(x => x.Edition).ThenInclude(e => e.Covers)
            .Include(x => x.Edition).ThenInclude(e => e.Work)
            .Include(x => x.ReadRecords)
            .FirstAsync(x => x.Id == userBook.Id, ct);
        return Ok(withIncludes.ToDto());
    }

    // ── Update ────────────────────────────────────────────────────────────────

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateUserBookRequest request, CancellationToken ct)
    {
        var ub = await _db.UserBooks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();

        if (request.Status.HasValue)  ub.Status  = request.Status.Value;
        if (request.IsOwned.HasValue) ub.IsOwned = request.IsOwned.Value;
        if (request.Rating.HasValue)  ub.Rating  = request.Rating.Value;
        if (request.Notes != null)    ub.Notes   = request.Notes;
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Delete ────────────────────────────────────────────────────────────────

    [HttpDelete("{id:guid}")]
    public async Task<IActionResult> Delete(Guid id, CancellationToken ct)
    {
        var ub = await _db.UserBooks.FirstOrDefaultAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (ub == null) return NotFound();
        _db.UserBooks.Remove(ub);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }

    // ── Read records ──────────────────────────────────────────────────────────

    /// <summary>GET /api/books/{id}/reads — list all read records for a book.</summary>
    [HttpGet("{id:guid}/reads")]
    public async Task<IActionResult> ListReads(Guid id, CancellationToken ct)
    {
        var owned = await _db.UserBooks.AnyAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (!owned) return NotFound();

        var records = await _db.ReadRecords
            .Where(r => r.UserBookId == id)
            .OrderByDescending(r => r.DateRead)
            .ToListAsync(ct);
        return Ok(records.Select(r => r.ToDto()));
    }

    /// <summary>POST /api/books/{id}/reads — log a new reading of a book.</summary>
    [HttpPost("{id:guid}/reads")]
    public async Task<IActionResult> AddRead(Guid id, [FromBody] AddReadRecordRequest request, CancellationToken ct)
    {
        var owned = await _db.UserBooks.AnyAsync(x => x.Id == id && x.UserId == UserId, ct);
        if (!owned) return NotFound();

        var record = new ReadRecord
        {
            UserBookId = id,
            DateRead   = request.DateRead?.ToUniversalTime() ?? DateTime.UtcNow,
            Notes      = request.Notes,
        };
        _db.ReadRecords.Add(record);
        await _db.SaveChangesAsync(ct);
        return Ok(record.ToDto());
    }

    /// <summary>DELETE /api/books/{id}/reads/{recordId} — remove a read record.</summary>
    [HttpDelete("{id:guid}/reads/{recordId:guid}")]
    public async Task<IActionResult> DeleteRead(Guid id, Guid recordId, CancellationToken ct)
    {
        var record = await _db.ReadRecords
            .Include(r => r.UserBook)
            .FirstOrDefaultAsync(r => r.Id == recordId && r.UserBookId == id && r.UserBook.UserId == UserId, ct);
        if (record == null) return NotFound();
        _db.ReadRecords.Remove(record);
        await _db.SaveChangesAsync(ct);
        return NoContent();
    }
}
