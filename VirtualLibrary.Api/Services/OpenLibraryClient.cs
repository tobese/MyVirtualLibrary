using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Models;

namespace VirtualLibrary.Api.Services;

public interface IOpenLibraryClient
{
    Task<Edition?> LookupByIsbnAsync(string isbn, CancellationToken ct = default);
}

/// <summary>
/// Very small OpenLibrary wrapper. Fetches a single ISBN via the
/// /isbn/{isbn}.json endpoint and upserts Work / Edition / Author records.
///
/// Caches responses in-memory for 24h to respect OpenLibrary rate limits
/// (1 req/s baseline, 3 req/s with a descriptive User-Agent header).
/// </summary>
public class OpenLibraryClient : IOpenLibraryClient
{
    private const string BaseUrl = "https://openlibrary.org";
    private const string CoverBase = "https://covers.openlibrary.org";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly AppDbContext _db;
    private readonly ILogger<OpenLibraryClient> _log;
    private readonly SemaphoreSlim _rateLimit = new(1, 1);

    public OpenLibraryClient(HttpClient http, IMemoryCache cache, AppDbContext db, ILogger<OpenLibraryClient> log)
    {
        _http = http;
        _cache = cache;
        _db = db;
        _log = log;
    }

    public async Task<Edition?> LookupByIsbnAsync(string isbn, CancellationToken ct = default)
    {
        isbn = Normalize(isbn);
        if (string.IsNullOrWhiteSpace(isbn)) return null;

        // Already in DB? Reuse aggressively; the row is a cached representation.
        var existing = await _db.Editions
            .Include(e => e.EditionAuthors).ThenInclude(ea => ea.Author)
            .Include(e => e.Covers)
            .Include(e => e.Work)
            .FirstOrDefaultAsync(e => e.Isbn13 == isbn || e.Isbn10 == isbn, ct);
        if (existing != null) return existing;

        if (_cache.TryGetValue<Edition>($"isbn:{isbn}", out var cached) && cached != null)
            return cached;

        var editionJson = await GetJsonAsync($"{BaseUrl}/isbn/{isbn}.json", ct);
        if (editionJson == null) return null;

        var edition = new Edition
        {
            OpenLibraryId = editionJson.RootElement.TryGetProperty("key", out var k) ? Last(k.GetString()) : null,
            Isbn10 = isbn.Length == 10 ? isbn : null,
            Isbn13 = isbn.Length == 13 ? isbn : null,
            Title = editionJson.RootElement.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Publisher = editionJson.RootElement.TryGetProperty("publishers", out var pubs) && pubs.ValueKind == JsonValueKind.Array && pubs.GetArrayLength() > 0
                ? pubs[0].GetString() : null,
            PublishDate = editionJson.RootElement.TryGetProperty("publish_date", out var pd) ? pd.GetString() : null,
            NumberOfPages = editionJson.RootElement.TryGetProperty("number_of_pages", out var np) && np.ValueKind == JsonValueKind.Number ? np.GetInt32() : null,
            Language = ExtractFirstRef(editionJson.RootElement, "languages"),
            PhysicalDimensions = editionJson.RootElement.TryGetProperty("physical_dimensions", out var dim) ? dim.GetString() : null,
            Weight = editionJson.RootElement.TryGetProperty("weight", out var w) ? w.GetString() : null,
        };

        // Covers
        if (editionJson.RootElement.TryGetProperty("covers", out var covers) && covers.ValueKind == JsonValueKind.Array)
        {
            foreach (var c in covers.EnumerateArray())
            {
                if (c.ValueKind == JsonValueKind.Number)
                {
                    var coverId = c.GetInt64();
                    edition.Covers.Add(new Cover
                    {
                        CoverId = coverId,
                        SmallUrl = $"{CoverBase}/b/id/{coverId}-S.jpg",
                        MediumUrl = $"{CoverBase}/b/id/{coverId}-M.jpg",
                        LargeUrl = $"{CoverBase}/b/id/{coverId}-L.jpg",
                    });
                }
            }
        }
        if (edition.Covers.Count == 0 && !string.IsNullOrEmpty(edition.Isbn13 ?? edition.Isbn10))
        {
            var i = edition.Isbn13 ?? edition.Isbn10;
            edition.Covers.Add(new Cover
            {
                SmallUrl = $"{CoverBase}/b/isbn/{i}-S.jpg",
                MediumUrl = $"{CoverBase}/b/isbn/{i}-M.jpg",
                LargeUrl = $"{CoverBase}/b/isbn/{i}-L.jpg",
            });
        }

        // Work
        var workKey = ExtractFirstRef(editionJson.RootElement, "works");
        if (workKey != null)
        {
            var existingWork = await _db.Works.FirstOrDefaultAsync(w => w.OpenLibraryId == workKey, ct);
            if (existingWork != null)
            {
                edition.Work = existingWork;
                edition.WorkId = existingWork.Id;
            }
            else
            {
                var workJson = await GetJsonAsync($"{BaseUrl}/works/{workKey}.json", ct);
                if (workJson != null)
                {
                    var work = new Work
                    {
                        OpenLibraryId = workKey,
                        Title = workJson.RootElement.TryGetProperty("title", out var wt) ? wt.GetString() ?? "" : "",
                        Description = ExtractDescription(workJson.RootElement),
                        FirstPublishYear = workJson.RootElement.TryGetProperty("first_publish_date", out var fpd) && int.TryParse(fpd.GetString()?.Split('-')[0], out var y) ? y : null,
                        Subjects = workJson.RootElement.TryGetProperty("subjects", out var subj) && subj.ValueKind == JsonValueKind.Array
                            ? subj.EnumerateArray().Select(s => s.GetString() ?? "").Where(s => !string.IsNullOrEmpty(s)).ToList()
                            : new List<string>(),
                    };
                    _db.Works.Add(work);
                    edition.Work = work;
                }
            }
        }

        // Authors
        if (editionJson.RootElement.TryGetProperty("authors", out var authors) && authors.ValueKind == JsonValueKind.Array)
        {
            foreach (var a in authors.EnumerateArray())
            {
                var authorKey = a.TryGetProperty("key", out var ak) ? Last(ak.GetString()) : null;
                if (authorKey == null) continue;

                var existingAuthor = await _db.Authors.FirstOrDefaultAsync(x => x.OpenLibraryId == authorKey, ct);
                if (existingAuthor == null)
                {
                    var authorJson = await GetJsonAsync($"{BaseUrl}/authors/{authorKey}.json", ct);
                    existingAuthor = new Author
                    {
                        OpenLibraryId = authorKey,
                        Name = authorJson?.RootElement.TryGetProperty("name", out var an) == true ? an.GetString() ?? "Unknown" : "Unknown",
                        BirthDate = authorJson?.RootElement.TryGetProperty("birth_date", out var bd) == true ? bd.GetString() : null,
                        Bio = authorJson != null ? ExtractDescription(authorJson.RootElement) : null,
                    };
                    _db.Authors.Add(existingAuthor);
                }
                edition.EditionAuthors.Add(new EditionAuthor { Author = existingAuthor });
            }
        }

        _db.Editions.Add(edition);
        await _db.SaveChangesAsync(ct);

        _cache.Set($"isbn:{isbn}", edition, TimeSpan.FromHours(24));
        return edition;
    }

    private async Task<JsonDocument?> GetJsonAsync(string url, CancellationToken ct)
    {
        await _rateLimit.WaitAsync(ct);
        try
        {
            // Cheap, polite rate limit: wait at least 350ms between outgoing calls.
            // We also rely on per-ISBN DB + IMemoryCache caches for real hot paths.
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.UserAgent.ParseAdd("VirtualLibrary/1.0 (https://github.com/tobese/MyVirtualLibrary)");
            var resp = await _http.SendAsync(req, ct);
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("OpenLibrary GET {Url} failed: {Status}", url, resp.StatusCode);
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
}
