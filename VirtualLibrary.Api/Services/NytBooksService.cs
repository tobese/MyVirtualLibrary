using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Services;

public interface INytBooksService
{
    Task<BestsellerListDto?> GetBestsellerListAsync(string listName, CancellationToken ct = default);
    Task<IReadOnlyList<string>> GetAvailableListsAsync(CancellationToken ct = default);
}

public class NytBooksService : INytBooksService
{
    private const string BaseUrl = "https://api.nytimes.com/svc/books/v3";

    private readonly HttpClient _http;
    private readonly IMemoryCache _cache;
    private readonly string? _apiKey;
    private readonly ILogger<NytBooksService> _log;

    public NytBooksService(
        HttpClient http,
        IMemoryCache cache,
        IConfiguration configuration,
        ILogger<NytBooksService> log)
    {
        _http = http;
        _cache = cache;
        _apiKey = configuration["NytBooks:ApiKey"];
        _log = log;
    }

    public async Task<BestsellerListDto?> GetBestsellerListAsync(string listName, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
        {
            _log.LogWarning("NYT Books API key not configured — set NytBooks:ApiKey in configuration");
            return null;
        }

        var cacheKey = $"nyt:bestsellers:{listName}";
        if (_cache.TryGetValue<BestsellerListDto>(cacheKey, out var cached) && cached != null)
            return cached;

        var url = $"{BaseUrl}/lists/current/{Uri.EscapeDataString(listName)}.json?api-key={_apiKey}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (resp.StatusCode == System.Net.HttpStatusCode.NotFound)
                return null;
            if (!resp.IsSuccessStatusCode)
            {
                _log.LogWarning("NYT Books API GET {List} → {Status}", listName, resp.StatusCode);
                return null;
            }

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var results = doc.RootElement.GetProperty("results");
            var dto = new BestsellerListDto(
                ListName: results.TryGetProperty("list_name", out var ln) ? ln.GetString() ?? listName : listName,
                ListNameEncoded: results.TryGetProperty("list_name_encoded", out var lne) ? lne.GetString() ?? listName : listName,
                PublishedDate: results.TryGetProperty("published_date", out var pd) ? pd.GetString() ?? "" : "",
                Books: ParseBooks(results)
            );

            _cache.Set(cacheKey, dto, TimeSpan.FromHours(24));
            return dto;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "NYT Books API GET {List} threw", listName);
            return null;
        }
    }

    public async Task<IReadOnlyList<string>> GetAvailableListsAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_apiKey))
            return Array.Empty<string>();

        const string cacheKey = "nyt:list-names";
        if (_cache.TryGetValue<IReadOnlyList<string>>(cacheKey, out var cached) && cached != null)
            return cached;

        var url = $"{BaseUrl}/lists/names.json?api-key={_apiKey}";
        try
        {
            using var resp = await _http.GetAsync(url, ct);
            if (!resp.IsSuccessStatusCode) return Array.Empty<string>();

            using var stream = await resp.Content.ReadAsStreamAsync(ct);
            using var doc = await JsonDocument.ParseAsync(stream, cancellationToken: ct);

            var names = doc.RootElement.GetProperty("results")
                .EnumerateArray()
                .Select(e => e.TryGetProperty("list_name_encoded", out var n) ? n.GetString() : null)
                .OfType<string>()
                .ToList();

            _cache.Set(cacheKey, (IReadOnlyList<string>)names, TimeSpan.FromHours(24));
            return names;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "NYT Books list names threw");
            return Array.Empty<string>();
        }
    }

    private static List<BestsellerEntryDto> ParseBooks(JsonElement results)
    {
        if (!results.TryGetProperty("books", out var books) || books.ValueKind != JsonValueKind.Array)
            return new();

        return books.EnumerateArray().Select(b => new BestsellerEntryDto(
            Rank: b.TryGetProperty("rank", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0,
            Title: b.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "",
            Author: b.TryGetProperty("author", out var a) ? a.GetString() ?? "" : "",
            Description: b.TryGetProperty("description", out var d) ? d.GetString() : null,
            Isbn13: b.TryGetProperty("primary_isbn13", out var isbn) ? isbn.GetString() : null,
            Publisher: b.TryGetProperty("publisher", out var pub) ? pub.GetString() : null,
            CoverUrl: b.TryGetProperty("book_image", out var img) ? img.GetString() : null,
            WeeksOnList: b.TryGetProperty("weeks_on_list", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetInt32() : 0
        )).ToList();
    }
}
