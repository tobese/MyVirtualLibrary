using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "ActiveUser")]
public class CatalogController : ControllerBase
{
    private readonly AppDbContext _db;

    public CatalogController(AppDbContext db) => _db = db;

    /// <summary>
    /// Catalog-wide summary: counts, top authors, top subjects, language
    /// breakdown, and the 10 most recently cached editions.
    /// Accessible to all active users (no admin role required).
    /// </summary>
    [HttpGet]
    public async Task<IActionResult> GetSummary(CancellationToken ct)
    {
        var totalEditions      = await _db.Editions.CountAsync(ct);
        var totalWorks         = await _db.Works.CountAsync(ct);
        var totalAuthors       = await _db.Authors.CountAsync(ct);
        var editionsWithCovers = await _db.Covers.Select(c => c.EditionId).Distinct().CountAsync(ct);

        // Top 10 authors by edition count
        var topAuthors = await _db.EditionAuthors
            .GroupBy(ea => new { ea.Author.Name, ea.AuthorId })
            .Select(g => new { g.Key.Name, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(10)
            .ToListAsync(ct);

        // Subjects are stored as jsonb — aggregate in-memory
        var allSubjectLists = await _db.Works
            .Where(w => w.Subjects != null && w.Subjects.Count > 0)
            .Select(w => w.Subjects)
            .ToListAsync(ct);

        var subjectGroups = allSubjectLists
            .SelectMany(s => s)
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .OrderByDescending(g => g.Count())
            .ToList();

        var topSubjects   = subjectGroups.Take(10).Select(g => new RankedItemDto(g.Key, g.Count())).ToList();
        var uniqueSubjects = subjectGroups.Count;

        // Language breakdown (editions with a non-null language)
        var languages = await _db.Editions
            .Where(e => e.Language != null)
            .GroupBy(e => e.Language!)
            .Select(g => new { Language = g.Key, Count = g.Count() })
            .OrderByDescending(x => x.Count)
            .Take(15)
            .ToListAsync(ct);

        // 10 most recently cached editions with their first author + cover
        var recent = await _db.Editions
            .OrderByDescending(e => e.CachedAt)
            .Take(10)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Isbn13,
                e.Publisher,
                e.PublishDate,
                Authors = e.EditionAuthors
                    .OrderBy(ea => ea.AuthorId)
                    .Select(ea => ea.Author.Name)
                    .ToList(),
                CoverUrl = e.Covers
                    .Where(c => c.MediumUrl != null)
                    .Select(c => c.MediumUrl)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        var dto = new CatalogSummaryDto(
            TotalEditions:      totalEditions,
            TotalWorks:         totalWorks,
            TotalAuthors:       totalAuthors,
            UniqueSubjects:     uniqueSubjects,
            EditionsWithCovers: editionsWithCovers,
            TopAuthors:         topAuthors.Select(a => new RankedItemDto(a.Name, a.Count)).ToList(),
            TopSubjects:        topSubjects,
            Languages:          languages.Select(l => new LanguageCountDto(l.Language, l.Count)).ToList(),
            RecentlyAdded:      recent.Select(e => new RecentEditionDto(
                                    Id:          e.Id,
                                    Title:       e.Title,
                                    Isbn13:      e.Isbn13,
                                    Publisher:   e.Publisher,
                                    PublishDate: e.PublishDate,
                                    Authors:     e.Authors,
                                    CoverUrl:    e.CoverUrl
                                )).ToList()
        );

        return Ok(dto);
    }

    /// <summary>
    /// Paginated list of all authors in the catalog, ordered by edition count descending.
    /// </summary>
    [HttpGet("authors")]
    public async Task<IActionResult> GetAuthors(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q    = null,
        CancellationToken ct     = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.Authors.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(a => a.Name.ToLower().Contains(q.ToLower()));

        var total = await query.CountAsync(ct);

        var authors = await query
            .Select(a => new
            {
                a.Id,
                a.Name,
                a.Bio,
                a.PhotoUrl,
                EditionCount = a.EditionAuthors.Count,
            })
            .OrderByDescending(a => a.EditionCount)
            .ThenBy(a => a.Name)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(ct);

        return Ok(new AuthorListDto(
            Authors:  authors.Select(a => new AuthorSummaryDto(a.Id, a.Name, a.Bio, a.PhotoUrl, a.EditionCount)).ToList(),
            Total:    total,
            Page:     page,
            PageSize: pageSize
        ));
    }

    /// <summary>
    /// Paginated list of editions in the catalog, ordered by most recently cached.
    /// </summary>
    [HttpGet("books")]
    public async Task<IActionResult> GetBooks(
        [FromQuery] int page     = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? q    = null,
        [FromQuery] string? lang = null,
        CancellationToken ct     = default)
    {
        pageSize = Math.Clamp(pageSize, 1, 100);
        page     = Math.Max(1, page);

        var query = _db.Editions.AsQueryable();
        if (!string.IsNullOrWhiteSpace(q))
            query = query.Where(e => e.Title.ToLower().Contains(q.ToLower())
                                  || (e.Isbn13 != null && e.Isbn13.Contains(q)));
        if (!string.IsNullOrWhiteSpace(lang))
            query = query.Where(e => e.Language == lang);

        var total = await query.CountAsync(ct);

        var books = await query
            .OrderByDescending(e => e.CachedAt)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(e => new
            {
                e.Id,
                e.Title,
                e.Isbn13,
                e.Publisher,
                e.PublishDate,
                e.NumberOfPages,
                e.Language,
                e.CachedAt,
                Authors = e.EditionAuthors
                    .OrderBy(ea => ea.AuthorId)
                    .Select(ea => ea.Author.Name)
                    .ToList(),
                CoverUrl = e.Covers
                    .Where(c => c.MediumUrl != null)
                    .Select(c => c.MediumUrl)
                    .FirstOrDefault(),
            })
            .ToListAsync(ct);

        return Ok(new
        {
            books = books.Select(e => new RecentEditionDto(
                Id:          e.Id,
                Title:       e.Title,
                Isbn13:      e.Isbn13,
                Publisher:   e.Publisher,
                PublishDate: e.PublishDate,
                Authors:     e.Authors,
                CoverUrl:    e.CoverUrl
            )),
            total,
            page,
            pageSize,
        });
    }
}
