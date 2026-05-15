using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Services;

public interface IOpenLibraryClient
{
    /// <summary>
    /// Fast path: returns existing DB record if present, otherwise fetches from
    /// OpenLibrary and persists. Used by scan/manual ISBN entry.
    /// </summary>
    Task<Edition?> LookupByIsbnAsync(string isbn, CancellationToken ct = default);

    /// <summary>
    /// Import path: always fetches from OpenLibrary and compares
    /// <c>last_modified</c> timestamps to decide whether to update existing records.
    /// Returns the edition and a flag indicating whether any metadata was refreshed.
    /// </summary>
    Task<(Edition? Edition, bool DataRefreshed)> FetchAndSyncAsync(string isbn, CancellationToken ct = default);

    /// <summary>
    /// Returns the current trending works from OpenLibrary for <paramref name="period"/>
    /// ("daily" or "weekly"). Results are cached for 1 hour.
    /// Where a trending work already exists in the local database, <see cref="TrendingWorkDto.LocalWorkId"/> is populated.
    /// </summary>
    Task<TrendingResultDto> GetTrendingAsync(string period, CancellationToken ct = default);
}

/// <summary>
/// OpenLibrary wrapper. Fetches Edition / Work / Author records via
/// /isbn/{isbn}.json, /works/{key}.json and /authors/{key}.json.
///
/// Rate-limiting: a semaphore + 350 ms inter-request delay respects the
/// baseline OpenLibrary quota (1 req/s).
///
/// Caching layers:
///  1. PostgreSQL (Editions table) — permanent, checked first on LookupByIsbnAsync.
///  2. IMemoryCache — 24 h, fallback before hitting the network on LookupByIsbnAsync.
///  3. FetchAndSyncAsync always hits the network so it can compare last_modified.
/// </summary>
public class OpenLibraryClient : IOpenLibraryClient
{
    private const string BaseUrl   = "https://openlibrary.org";
    private const string CoverBase = "https://covers.openlibrary.org";

    private readonly HttpClient              _http;
    private readonly IMemoryCache            _cache;
    private readonly AppDbContext            _db;
    private readonly ILogger<OpenLibraryClient> _log;
    private readonly SemaphoreSlim           _rateLimit = new(1, 1);

    public OpenLibraryClient(
        HttpClient http, IMemoryCache cache, AppDbContext db,
        ILogger<OpenLibraryClient> log)
    {
        _http  = http;
        _cache = cache;
        _db    = db;
        _log   = log;
    }

    // ── Fast-path lookup (scan / manual entry) ────────────────────────────────

    public async Task<Edition?> LookupByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        isbn = Normalize(isbn);
        if (string.IsNullOrWhiteSpace(isbn)) return null;

        // 1. DB
        var existing = await FindInDbAsync(isbn, ct);
        if (existing != null) return existing;

        // 2. In-memory cache
        if (_cache.TryGetValue<Edition>($"isbn:{isbn}", out var cached) && cached != null)
            return cached;

        // 3. OpenLibrary
        var editionJson = await GetJsonAsync($"{BaseUrl}/isbn/{isbn}.json", ct);
        if (editionJson == null) return null;

        var edition = await BuildEditionAsync(isbn, editionJson, ct);
        if (edition == null) return null;

        _db.Editions.Add(edition);
        await _db.SaveChangesAsync(ct);

        _cache.Set($"isbn:{isbn}", edition, TimeSpan.FromHours(24));
        return edition;
    }

    // ── Import path: always fetch + conditional update ────────────────────────

    public async Task<(Edition? Edition, bool DataRefreshed)> FetchAndSyncAsync(
        string isbn, CancellationToken ct = default)
    {
        isbn = Normalize(isbn);
        if (string.IsNullOrWhiteSpace(isbn)) return (null, false);

        var editionJson = await GetJsonAsync($"{BaseUrl}/isbn/{isbn}.json", ct);
        if (editionJson == null) return (null, false);

        var olEditionModified = ExtractLastModified(editionJson.RootElement);

        var existing = await FindInDbAsync(isbn, ct);

        // ── Brand-new record ──────────────────────────────────────────────────
        if (existing == null)
        {
            var edition = await BuildEditionAsync(isbn, editionJson, ct);
            if (edition == null) return (null, false);

            _db.Editions.Add(edition);
            await _db.SaveChangesAsync(ct);
            _cache.Set($"isbn:{isbn}", edition, TimeSpan.FromHours(24));
            return (edition, false); // "refreshed" means we updated *existing* data
        }

        // ── Already in DB: check whether OL data is newer ────────────────────
        bool dataRefreshed = false;

        if (IsNewer(olEditionModified, existing.OlLastModified))
        {
            UpdateEditionFields(existing, editionJson.RootElement, olEditionModified);

            // Re-sync Work if the edition has one
            var workKey = ExtractFirstRef(editionJson.RootElement, "works");
            if (workKey != null)
                await SyncWorkAsync(existing, workKey, ct);

            // Re-sync Authors
            if (editionJson.RootElement.TryGetProperty("authors", out var authorsEl)
                && authorsEl.ValueKind == JsonValueKind.Array)
            {
                await SyncAuthorsAsync(existing, authorsEl, ct);
            }

            existing.CachedAt = DateTime.UtcNow;
            await _db.SaveChangesAsync(ct);
            _cache.Set($"isbn:{isbn}", existing, TimeSpan.FromHours(24));
            dataRefreshed = true;
        }

        return (existing, dataRefreshed);
    }

    // ── Trending ──────────────────────────────────────────────────────────────

    // OL subjects used as a fallback when the /trending endpoint is unavailable.
    // Two subject groups give a balanced fiction / non-fiction mix.
    private static readonly string[] TrendingFallbackSubjects =
        ["fiction", "nonfiction", "mystery", "science", "biography"];

    public async Task<TrendingResultDto> GetTrendingAsync(string period, CancellationToken ct = default)
    {
        period = period is "daily" or "weekly" ? period : "weekly";
        var cacheKey = $"ol:trending:{period}";
        if (_cache.TryGetValue<TrendingResultDto>(cacheKey, out var cached) && cached != null)
            return cached;

        // Try the native trending endpoint first; fall back to search if it returns non-JSON.
        var json = await GetJsonAsync($"{BaseUrl}/trending/{period}.json", ct);
        List<JsonElement> rawWorks;

        if (json != null && json.RootElement.TryGetProperty("works", out var worksEl)
                         && worksEl.ValueKind == JsonValueKind.Array)
        {
            rawWorks = worksEl.EnumerateArray().Take(20).ToList();
        }
        else
        {
            // Fallback: pull recently-published works via the search API, rotating
            // through subjects so we get a varied mix across fiction and non-fiction.
            _log.LogInformation(
                "OL trending/{Period} unavailable — falling back to search API", period);
            rawWorks = await FetchTrendingViaSearchAsync(period, ct);
        }

        return await BuildTrendingResultAsync(period, rawWorks, ct);
    }

    private async Task<List<JsonElement>> FetchTrendingViaSearchAsync(string period, CancellationToken ct)
    {
        // "daily" → 1 subject (quick, ~10 works); "weekly" → all subjects (~50 total, deduplicated to 20).
        var subjects = period == "daily"
            ? TrendingFallbackSubjects.Take(2).ToArray()
            : TrendingFallbackSubjects;

        var seen     = new HashSet<string>(StringComparer.Ordinal);
        var elements = new List<JsonElement>();

        foreach (var subject in subjects)
        {
            if (elements.Count >= 20) break;
            var url = $"{BaseUrl}/search.json?subject={Uri.EscapeDataString(subject)}" +
                      $"&sort=new&fields=key,title,author_name,cover_i&limit=10";
            var json = await GetJsonAsync(url, ct);
            if (json == null) continue;
            if (!json.RootElement.TryGetProperty("docs", out var docs) ||
                docs.ValueKind != JsonValueKind.Array)
                continue;

            foreach (var doc in docs.EnumerateArray())
            {
                if (elements.Count >= 20) break;
                var key = doc.TryGetProperty("key", out var k) ? k.GetString() : null;
                if (key != null && seen.Add(key))
                    elements.Add(doc);
            }
        }

        return elements;
    }

    private async Task<TrendingResultDto> BuildTrendingResultAsync(
        string period, List<JsonElement> rawWorks, CancellationToken ct)
    {
        var olIds = rawWorks
            .Select(w => w.TryGetProperty("key", out var k) ? Last(k.GetString()) : null)
            .OfType<string>()
            .ToList();

        var localByOlId = olIds.Count > 0
            ? (await _db.Works
                .Where(w => w.OpenLibraryId != null && olIds.Contains(w.OpenLibraryId))
                .Select(w => new { w.Id, w.OpenLibraryId })
                .ToListAsync(ct))
              .ToDictionary(w => w.OpenLibraryId!, w => w.Id)
            : new Dictionary<string, Guid>();

        var works = new List<TrendingWorkDto>(rawWorks.Count);
        foreach (var w in rawWorks)
        {
            var key = w.TryGetProperty("key", out var k) ? k.GetString() : null;
            if (key == null) continue;

            var olId    = Last(key);
            var title   = w.TryGetProperty("title",       out var t)  ? t.GetString()  ?? "" : "";
            var authors = w.TryGetProperty("author_name", out var an) && an.ValueKind == JsonValueKind.Array
                ? an.EnumerateArray().Select(a => a.GetString() ?? "").Where(s => s.Length > 0).ToList()
                : new List<string>();
            string? coverUrl = null;
            if (w.TryGetProperty("cover_i", out var ci) && ci.ValueKind == JsonValueKind.Number)
                coverUrl = $"{CoverBase}/b/id/{ci.GetInt64()}-M.jpg";

            Guid? localId = olId != null && localByOlId.TryGetValue(olId, out var lwId) ? lwId : null;
            works.Add(new TrendingWorkDto(OlKey: key, Title: title, Authors: authors,
                                          CoverUrl: coverUrl, LocalWorkId: localId));
        }

        var result = new TrendingResultDto(period, works);
        _cache.Set($"ol:trending:{period}", result, TimeSpan.FromHours(1));
        return result;
    }

    // ── Build helpers ─────────────────────────────────────────────────────────

    /// <summary>
    /// Constructs a fresh <see cref="Edition"/> (and associated Work / Authors / Covers)
    /// from a fetched OpenLibrary edition JSON document.
    /// Does NOT call SaveChanges — the caller is responsible.
    /// </summary>
    private async Task<Edition?> BuildEditionAsync(
        string isbn, JsonDocument editionJson, CancellationToken ct)
    {
        var root = editionJson.RootElement;

        var edition = new Edition
        {
            OpenLibraryId      = root.TryGetProperty("key", out var k)   ? Last(k.GetString()) : null,
            Isbn10             = isbn.Length == 10 ? isbn : null,
            Isbn13             = isbn.Length == 13 ? isbn : null,
            Title              = root.TryGetProperty("title", out var t)  ? t.GetString() ?? "" : "",
            Publisher          = root.TryGetProperty("publishers", out var pubs)
                                 && pubs.ValueKind == JsonValueKind.Array
                                 && pubs.GetArrayLength() > 0 ? pubs[0].GetString() : null,
            PublishDate        = root.TryGetProperty("publish_date", out var pd) ? pd.GetString() : null,
            NumberOfPages      = root.TryGetProperty("number_of_pages", out var np)
                                 && np.ValueKind == JsonValueKind.Number ? np.GetInt32() : null,
            Language           = ExtractFirstRef(root, "languages"),
            PhysicalDimensions = root.TryGetProperty("physical_dimensions", out var dim) ? dim.GetString() : null,
            Weight             = root.TryGetProperty("weight", out var w) ? w.GetString() : null,
            OlLastModified     = ExtractLastModified(root),
        };

        BuildCovers(edition, root);

        // Work
        var workKey = ExtractFirstRef(root, "works");
        if (workKey != null)
            await AttachWorkAsync(edition, workKey, ct);

        // Authors
        if (root.TryGetProperty("authors", out var authorsEl)
            && authorsEl.ValueKind == JsonValueKind.Array)
        {
            await AttachAuthorsAsync(edition, authorsEl, ct);
        }

        return edition;
    }

    private static void BuildCovers(Edition edition, JsonElement root)
    {
        if (root.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in covers.EnumerateArray())
            {
                if (c.ValueKind != JsonValueKind.Number) continue;
                var coverId = c.GetInt64();
                edition.Covers.Add(new Cover
                {
                    CoverId   = coverId,
                    SmallUrl  = $"{CoverBase}/b/id/{coverId}-S.jpg",
                    MediumUrl = $"{CoverBase}/b/id/{coverId}-M.jpg",
                    LargeUrl  = $"{CoverBase}/b/id/{coverId}-L.jpg",
                });
            }
        }

        if (edition.Covers.Count == 0)
        {
            var i = edition.Isbn13 ?? edition.Isbn10;
            if (!string.IsNullOrEmpty(i))
            {
                edition.Covers.Add(new Cover
                {
                    SmallUrl  = $"{CoverBase}/b/isbn/{i}-S.jpg",
                    MediumUrl = $"{CoverBase}/b/isbn/{i}-M.jpg",
                    LargeUrl  = $"{CoverBase}/b/isbn/{i}-L.jpg",
                });
            }
        }
    }

    private async Task AttachWorkAsync(Edition edition, string workKey, CancellationToken ct)
    {
        var existingWork = await _db.Works.FirstOrDefaultAsync(w => w.OpenLibraryId == workKey, ct);
        if (existingWork != null)
        {
            edition.Work   = existingWork;
            edition.WorkId = existingWork.Id;
            return;
        }

        var workJson = await GetJsonAsync($"{BaseUrl}/works/{workKey}.json", ct);
        if (workJson == null) return;

        var work = BuildWork(workKey, workJson.RootElement);
        _db.Works.Add(work);
        edition.Work = work;
    }

    private async Task AttachAuthorsAsync(Edition edition, JsonElement authorsEl, CancellationToken ct)
    {
        foreach (var a in authorsEl.EnumerateArray())
        {
            var authorKey = a.TryGetProperty("key", out var ak) ? Last(ak.GetString()) : null;
            if (authorKey == null) continue;

            var author = await _db.Authors.FirstOrDefaultAsync(x => x.OpenLibraryId == authorKey, ct);
            if (author == null)
            {
                var authorJson = await GetJsonAsync($"{BaseUrl}/authors/{authorKey}.json", ct);
                author = BuildAuthor(authorKey, authorJson?.RootElement);
                _db.Authors.Add(author);
            }

            edition.EditionAuthors.Add(new EditionAuthor { Author = author });
        }
    }

    // ── Sync helpers (used by FetchAndSyncAsync for existing records) ─────────

    private static void UpdateEditionFields(
        Edition edition, JsonElement root, DateTime? olLastModified)
    {
        edition.Title = root.TryGetProperty("title", out var t) ? t.GetString() ?? edition.Title : edition.Title;
        edition.Publisher = root.TryGetProperty("publishers", out var pubs)
                            && pubs.ValueKind == JsonValueKind.Array
                            && pubs.GetArrayLength() > 0 ? pubs[0].GetString() : edition.Publisher;
        edition.PublishDate        = root.TryGetProperty("publish_date", out var pd) ? pd.GetString() : edition.PublishDate;
        edition.NumberOfPages      = root.TryGetProperty("number_of_pages", out var np)
                                     && np.ValueKind == JsonValueKind.Number ? np.GetInt32() : edition.NumberOfPages;
        edition.Language           = ExtractFirstRef(root, "languages") ?? edition.Language;
        edition.PhysicalDimensions = root.TryGetProperty("physical_dimensions", out var dim) ? dim.GetString() : edition.PhysicalDimensions;
        edition.Weight             = root.TryGetProperty("weight", out var w) ? w.GetString() : edition.Weight;
        edition.OlLastModified     = olLastModified;
    }

    private async Task SyncWorkAsync(Edition edition, string workKey, CancellationToken ct)
    {
        var workJson = await GetJsonAsync($"{BaseUrl}/works/{workKey}.json", ct);
        if (workJson == null) return;

        var olWorkModified = ExtractLastModified(workJson.RootElement);

        if (edition.Work != null && edition.Work.OpenLibraryId == workKey)
        {
            if (IsNewer(olWorkModified, edition.Work.OlLastModified))
            {
                edition.Work.Title           = workJson.RootElement.TryGetProperty("title", out var wt) ? wt.GetString() ?? edition.Work.Title : edition.Work.Title;
                edition.Work.Description     = ExtractDescription(workJson.RootElement) ?? edition.Work.Description;
                edition.Work.FirstPublishYear = workJson.RootElement.TryGetProperty("first_publish_date", out var fpd)
                                               && int.TryParse(fpd.GetString()?.Split('-')[0], out var y) ? y : edition.Work.FirstPublishYear;
                edition.Work.Subjects        = workJson.RootElement.TryGetProperty("subjects", out var subj)
                                               && subj.ValueKind == JsonValueKind.Array
                                               ? subj.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                                               : edition.Work.Subjects;
                edition.Work.OlLastModified  = olWorkModified;
            }
        }
        else
        {
            // Work reference changed or not loaded — re-attach
            var existingWork = await _db.Works.FirstOrDefaultAsync(w => w.OpenLibraryId == workKey, ct);
            if (existingWork == null)
            {
                existingWork = BuildWork(workKey, workJson.RootElement);
                _db.Works.Add(existingWork);
            }
            edition.Work   = existingWork;
            edition.WorkId = existingWork.Id;
        }
    }

    private async Task SyncAuthorsAsync(Edition edition, JsonElement authorsEl, CancellationToken ct)
    {
        foreach (var a in authorsEl.EnumerateArray())
        {
            var authorKey = a.TryGetProperty("key", out var ak) ? Last(ak.GetString()) : null;
            if (authorKey == null) continue;

            var existingLink  = edition.EditionAuthors.FirstOrDefault(ea => ea.Author?.OpenLibraryId == authorKey);
            var existingAuthor = existingLink?.Author
                              ?? await _db.Authors.FirstOrDefaultAsync(x => x.OpenLibraryId == authorKey, ct);

            if (existingAuthor == null)
            {
                var authorJson = await GetJsonAsync($"{BaseUrl}/authors/{authorKey}.json", ct);
                existingAuthor = BuildAuthor(authorKey, authorJson?.RootElement);
                _db.Authors.Add(existingAuthor);
                edition.EditionAuthors.Add(new EditionAuthor { Author = existingAuthor });
            }
            else if (existingLink == null)
            {
                // Author exists globally but not linked to this edition
                edition.EditionAuthors.Add(new EditionAuthor { Author = existingAuthor });
            }
            else
            {
                // Author already linked — update if OL has newer data
                var authorJson = await GetJsonAsync($"{BaseUrl}/authors/{authorKey}.json", ct);
                if (authorJson == null) continue;
                var olAuthorModified = ExtractLastModified(authorJson.RootElement);

                if (IsNewer(olAuthorModified, existingAuthor.OlLastModified))
                {
                    existingAuthor.Name           = authorJson.RootElement.TryGetProperty("name", out var an) ? an.GetString() ?? existingAuthor.Name : existingAuthor.Name;
                    existingAuthor.BirthDate      = authorJson.RootElement.TryGetProperty("birth_date", out var bd) ? bd.GetString() : existingAuthor.BirthDate;
                    existingAuthor.Bio            = ExtractDescription(authorJson.RootElement) ?? existingAuthor.Bio;
                    existingAuthor.OlLastModified = olAuthorModified;
                }
            }
        }
    }

    // ── Pure build helpers (no DB, no I/O) ───────────────────────────────────

    private static Work BuildWork(string workKey, JsonElement root) => new()
    {
        OpenLibraryId    = workKey,
        Title            = root.TryGetProperty("title", out var wt) ? wt.GetString() ?? "" : "",
        Description      = ExtractDescription(root),
        FirstPublishYear = root.TryGetProperty("first_publish_date", out var fpd)
                           && int.TryParse(fpd.GetString()?.Split('-')[0], out var y) ? y : null,
        Subjects         = root.TryGetProperty("subjects", out var subj) && subj.ValueKind == JsonValueKind.Array
                           ? subj.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                           : new List<string>(),
        OlLastModified   = ExtractLastModified(root),
    };

    private static Author BuildAuthor(string authorKey, JsonElement? root) => new()
    {
        OpenLibraryId  = authorKey,
        Name           = root?.TryGetProperty("name", out var an) == true ? an.GetString() ?? "Unknown" : "Unknown",
        BirthDate      = root?.TryGetProperty("birth_date", out var bd) == true ? bd.GetString() : null,
        Bio            = root.HasValue ? ExtractDescription(root.Value) : null,
        OlLastModified = root.HasValue ? ExtractLastModified(root.Value) : null,
    };

    // ── Network ───────────────────────────────────────────────────────────────

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        await _rateLimit.WaitAsync(ct);
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd(
                "VirtualLibrary/1.0 (https://github.com/tobese/MyVirtualLibrary)");
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("OpenLibrary GET {Url} → {Status}", url, resp.StatusCode);
                return null;
            }
            var ct2 = resp.Content.Headers.ContentType?.MediaType ?? "";
            if (!ct2.Contains("json", StringComparison.OrdinalIgnoreCase))
            {
                _log.LogWarning("OpenLibrary GET {Url} returned non-JSON content-type: {CT}", url, ct2);
                return null;
            }
            var json = await resp.Content.ReadAsStringAsync(ct);
            return JsonDocument.Parse(json);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "OpenLibrary GET {Url} threw", url);
            return null;
        }
        finally
        {
            await Task.Delay(350, ct);
            _rateLimit.Release();
        }
    }

    // ── DB helpers ────────────────────────────────────────────────────────────

    private async Task<Edition?> FindInDbAsync(string isbn, CancellationToken ct) =>
        await _db.Editions
            .Include(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(e => e.Covers)
            .Include(e => e.Work)
            .FirstOrDefaultAsync(e => e.Isbn13 == isbn || e.Isbn10 == isbn, ct);

    // ── Static parsing helpers ────────────────────────────────────────────────

    private static string Normalize(string isbn) =>
        new string((isbn ?? "").Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

    private static string? Last(string? olKey)
    {
        if (string.IsNullOrEmpty(olKey)) return null;
        var idx = olKey.LastIndexOf('/');
        return idx >= 0 ? olKey[(idx + 1)..] : olKey;
    }

    private static string? ExtractFirstRef(JsonElement root, string propertyName)
    {
        if (!root.TryGetProperty(propertyName, out var arr)) return null;
        if (arr.ValueKind != JsonValueKind.Array || arr.GetArrayLength() == 0) return null;
        var first = arr[0];
        if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("key", out var k))
            return Last(k.GetString());
        if (first.ValueKind == JsonValueKind.String)
            return Last(first.GetString());
        return null;
    }

    private static string? ExtractDescription(JsonElement root)
    {
        if (!root.TryGetProperty("description", out var d)) return null;
        return d.ValueKind switch
        {
            JsonValueKind.String => d.GetString(),
            JsonValueKind.Object when d.TryGetProperty("value", out var v) => v.GetString(),
            _ => null
        };
    }

    /// <summary>
    /// Parses OpenLibrary's <c>last_modified</c> field shape:
    /// <c>{"type": "/type/datetime", "value": "2021-09-20T23:21:57.382721"}</c>
    /// </summary>
    private static DateTime? ExtractLastModified(JsonElement root)
    {
        if (!root.TryGetProperty("last_modified", out var lm)) return null;
        string? raw = lm.ValueKind switch
        {
            JsonValueKind.Object when lm.TryGetProperty("value", out var v) => v.GetString(),
            JsonValueKind.String => lm.GetString(),
            _ => null
        };
        if (raw == null) return null;
        return DateTime.TryParse(raw, out var dt)
            ? DateTime.SpecifyKind(dt, DateTimeKind.Utc)
            : null;
    }

    /// <summary>Returns true when <paramref name="incoming"/> is strictly newer than <paramref name="stored"/>.</summary>
    private static bool IsNewer(DateTime? incoming, DateTime? stored) =>
        incoming.HasValue && (!stored.HasValue || incoming.Value > stored.Value);
}
