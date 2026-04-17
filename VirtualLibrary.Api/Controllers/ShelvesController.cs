using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class ShelvesController : ControllerBase
{
    private readonly AppDbContext _db;

    public ShelvesController(AppDbContext db) => _db = db;

    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)
        ?? throw new InvalidOperationException("No user id");

    // ----------------------------------------------------------------
    // GET /api/shelves/default
    // Returns (or lazily creates) the user's single default shelf.
    // Unplaced owned books are appended at the end, keeping the order
    // stable across calls.
    // ----------------------------------------------------------------
    [HttpGet("default")]
    public async Task<IActionResult> GetDefault(CancellationToken ct)
    {
        // Ensure the user has a default shelf.
        var shelf = await _db.Shelves
            .Include(s => s.Placements)
                .ThenInclude(p => p.UserBook)
                    .ThenInclude(ub => ub.Edition)
                        .ThenInclude(e => e.EditionAuthors)
                            .ThenInclude(ea => ea.Author)
            .Include(s => s.Placements)
                .ThenInclude(p => p.UserBook)
                    .ThenInclude(ub => ub.Edition)
                        .ThenInclude(e => e.Covers)
            .Include(s => s.Placements)
                .ThenInclude(p => p.UserBook)
                    .ThenInclude(ub => ub.Edition)
                        .ThenInclude(e => e.Work)
            .FirstOrDefaultAsync(s => s.OwnerUserId == UserId, ct);

        if (shelf == null)
        {
            shelf = new Shelf
            {
                OwnerUserId = UserId,
                Name = "My Shelf",
                Rows = 1,
                SlotsPerRow = 200,
                Placements = new List<ShelfPlacement>(),
            };
            _db.Shelves.Add(shelf);
            await _db.SaveChangesAsync(ct);
        }

        // Merge: any owned book not yet in a placement gets appended.
        var ownedBooks = await _db.UserBooks
            .Where(ub => ub.UserId == UserId && ub.Status == BookStatus.Owned)
            .Include(ub => ub.Edition).ThenInclude(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(ub => ub.Edition).ThenInclude(e => e.Covers)
            .Include(ub => ub.Edition).ThenInclude(e => e.Work)
            .ToListAsync(ct);

        var placedIds = shelf.Placements.Select(p => p.UserBookId).ToHashSet();
        var nextSlot  = shelf.Placements.Any() ? shelf.Placements.Max(p => p.Slot) + 1 : 0;
        bool dirty    = false;

        foreach (var ub in ownedBooks.Where(b => !placedIds.Contains(b.Id)))
        {
            var placement = new ShelfPlacement
            {
                ShelfId    = shelf.Id,
                UserBookId = ub.Id,
                UserBook   = ub,
                Slot       = nextSlot++,
            };
            _db.ShelfPlacements.Add(placement);
            shelf.Placements.Add(placement);
            dirty = true;
        }

        if (dirty) await _db.SaveChangesAsync(ct);

        return Ok(shelf.ToDto());
    }

    // ----------------------------------------------------------------
    // PUT /api/shelves/{id}/placements
    // Replaces all placements for the shelf with an ordered list of
    // UserBook IDs (index == new Slot value).  Only UserBooks owned by
    // the caller are accepted; others are silently ignored.
    // ----------------------------------------------------------------
    [HttpPut("{id:guid}/placements")]
    public async Task<IActionResult> SavePlacements(
        Guid id,
        [FromBody] SaveShelfPlacementsRequest request,
        CancellationToken ct)
    {
        var shelf = await _db.Shelves
            .Include(s => s.Placements)
            .FirstOrDefaultAsync(s => s.Id == id && s.OwnerUserId == UserId, ct);

        if (shelf == null) return NotFound();

        // Validate that every referenced UserBook belongs to this user.
        var ownedIds = await _db.UserBooks
            .Where(ub => ub.UserId == UserId && ub.Status == BookStatus.Owned)
            .Select(ub => ub.Id)
            .ToHashSetAsync(ct);

        // Remove all existing placements.
        _db.ShelfPlacements.RemoveRange(shelf.Placements);

        // Re-create from the caller's order, skipping any invalid ids.
        var newPlacements = request.UserBookIds
            .Select((ubId, slot) => new { ubId, slot })
            .Where(x => ownedIds.Contains(x.ubId))
            .Select(x => new ShelfPlacement
            {
                ShelfId    = shelf.Id,
                UserBookId = x.ubId,
                Slot       = x.slot,
            })
            .ToList();

        _db.ShelfPlacements.AddRange(newPlacements);
        await _db.SaveChangesAsync(ct);

        return NoContent();
    }
}
