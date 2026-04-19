using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
[Authorize(Policy = "AdminUser")]
public class StatsController : ControllerBase
{
    private readonly AppDbContext _db;

    public StatsController(AppDbContext db)
    {
        _db = db;
    }

    /// <summary>
    /// Returns whole-library collection statistics.
    /// Restricted to Admin and SuperAdmin roles.
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> Get(CancellationToken ct)
    {
        // ── Catalogue counts ──────────────────────────────────────────────────
        var totalEditions = await _db.Editions.CountAsync(ct);
        var totalWorks    = await _db.Works.CountAsync(ct);
        var uniqueAuthors = await _db.Authors.CountAsync(ct);

        // ── User-book aggregates ──────────────────────────────────────────────
        var ubQuery = _db.UserBooks.AsQueryable();

        var totalUserBooks   = await ubQuery.CountAsync(ct);
        var ownedBooks       = await ubQuery.CountAsync(ub => ub.IsOwned, ct);
        var wishlistBooks    = await ubQuery.CountAsync(ub => !ub.IsOwned, ct);
        var readBooks        = await ubQuery.CountAsync(ub => ub.Status == BookStatus.Read, ct);
        var unreadBooks      = await ubQuery.CountAsync(ub => ub.Status == BookStatus.WantToRead, ct);
        var totalReadRecords = await _db.ReadRecords.CountAsync(ct);

        // ── Members ───────────────────────────────────────────────────────────
        var activeMembers = await _db.Users
            .CountAsync(u => u.Status == UserStatus.Active, ct);

        // ── Top authors (by number of catalogue editions) ─────────────────────
        var topAuthors = await _db.EditionAuthors
            .Include(ea => ea.Author)
            .GroupBy(ea => ea.Author.Name)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        // ── Top subjects (by number of works carrying them) ───────────────────
        // Subjects are stored as jsonb; aggregate in-memory after projection.
        var allSubjectLists = await _db.Works
            .Where(w => w.Subjects != null && w.Subjects.Count > 0)
            .Select(w => w.Subjects)
            .ToListAsync(ct);

        var topSubjects = allSubjectLists
            .SelectMany(s => s)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .Select(g => new { Name = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .Select(x => new RankedItemDto(x.Name, x.Count))
            .ToList();

        var uniqueSubjects = allSubjectLists
            .SelectMany(s => s)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Count();

        var stats = new LibraryStatsDto(
            TotalEditions:    totalEditions,
            TotalWorks:       totalWorks,
            UniqueAuthors:    uniqueAuthors,
            UniqueSubjects:   uniqueSubjects,
            TotalUserBooks:   totalUserBooks,
            OwnedBooks:       ownedBooks,
            WishlistBooks:    wishlistBooks,
            ReadBooks:        readBooks,
            UnreadBooks:      unreadBooks,
            TotalReadRecords: totalReadRecords,
            ActiveMembers:    activeMembers,
            TopAuthors:       topAuthors.Select(a => new RankedItemDto(a.Name, a.Count)).ToList(),
            TopSubjects:      topSubjects
        );

        return Ok(stats);
    }
}
