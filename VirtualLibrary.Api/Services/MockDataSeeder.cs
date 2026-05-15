using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Services;

#if DEBUG
/// <summary>
/// Seeds a realistic set of mock library users and populates their personal
/// libraries with real books fetched from the OpenLibrary Search API.
///
/// Strategy
/// ─────────
/// 1. Create 10 realistic member accounts (idempotent — skips existing emails).
/// 2. Resolve ISBNs for 6 curated topics:
///    a. If <c>seed-data/mock-isbn-cache.json</c> exists in the content root,
///       load from it — no network call needed.
///    b. Otherwise query OpenLibrary /search.json, collect up to 10 ISBN-13s
///       per topic, and write the result to the cache file for future runs.
/// 3. Assign books to users:
///    • "Popular" pool — first 3 ISBNs per topic → every user receives these.
///    • "Niche" pool   — remaining ISBNs per topic → each user gets 2 random
///      topics' worth, producing natural variety and per-user taste.
/// 4. Import each user's ISBN list via <see cref="IBulkImportService"/>, which
///    in turn drives <see cref="OpenLibraryClient"/> to fetch the full Edition /
///    Work / Author graph and persist it — exactly as a real bulk-import would.
///
/// Only compiled in Debug builds; never included in Release.
/// </summary>
public class MockDataSeeder
{
    // ── Static data ───────────────────────────────────────────────────────────

    private static readonly (string Email, string DisplayName)[] MockUsers =
    [
        ("emma.lindqvist@example.com",   "Emma Lindqvist"),
        ("oliver.berg@example.com",      "Oliver Berg"),
        ("sophia.karlsson@example.com",  "Sophia Karlsson"),
        ("liam.johansson@example.com",   "Liam Johansson"),
        ("maja.eriksson@example.com",    "Maja Eriksson"),
        ("noah.andersen@example.com",    "Noah Andersen"),
        ("elsa.nilsson@example.com",     "Elsa Nilsson"),
        ("william.larsson@example.com",  "William Larsson"),
        ("alice.svensson@example.com",   "Alice Svensson"),
        ("lucas.gustafsson@example.com", "Lucas Gustafsson"),
    ];

    /// <summary>
    /// Topics sent to the OpenLibrary full-text search.
    /// Chosen to produce distinct, well-represented result sets.
    /// </summary>
    private static readonly string[] Topics =
    [
        "fantasy epic",
        "science fiction classic",
        "mystery thriller",
        "world history",
        "biography memoir",
        "literary fiction",
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented          = true,
        PropertyNamingPolicy   = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    // ── Cache file location ───────────────────────────────────────────────────

    /// <summary>Absolute path to the local ISBN cache file.</summary>
    private readonly string _cacheFilePath;

    // ── Constructor + DI ──────────────────────────────────────────────────────

    private readonly UserManager<AppUser>    _userManager;
    private readonly IBulkImportService      _bulkImport;
    private readonly IHttpClientFactory      _httpFactory;
    private readonly ILogger<MockDataSeeder> _log;

    /// <summary>Fixed seed so repeated runs produce the same book distribution.</summary>
    private readonly Random _rng = new(42);

    public MockDataSeeder(
        UserManager<AppUser>    userManager,
        IBulkImportService      bulkImport,
        IHttpClientFactory      httpFactory,
        IWebHostEnvironment     env,
        ILogger<MockDataSeeder> log)
    {
        _userManager   = userManager;
        _bulkImport    = bulkImport;
        _httpFactory   = httpFactory;
        _log           = log;
        _cacheFilePath = Path.Combine(env.ContentRootPath, "seed-data", "mock-isbn-cache.json");
    }

    // ── Entry point ───────────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full seed pipeline. Idempotent: if all mock users already exist
    /// the method returns immediately without touching the database or the network.
    /// </summary>
    public async Task SeedAsync(CancellationToken ct = default)
    {
        _log.LogInformation("MockDataSeeder: starting");

        var users = await EnsureMockUsersAsync(ct);
        if (users.Count == 0)
        {
            _log.LogInformation("MockDataSeeder: all mock users already exist — skipping");
            return;
        }

        var isbnsByTopic = await ResolveIsbnsByTopicAsync(ct);
        if (isbnsByTopic.Count == 0)
        {
            _log.LogWarning("MockDataSeeder: no ISBNs available — skipping book assignment");
            return;
        }

        await AssignBooksToUsersAsync(users, isbnsByTopic, ct);

        _log.LogInformation("MockDataSeeder: finished");
    }

    // ── Step 1: create users ──────────────────────────────────────────────────

    private async Task<List<AppUser>> EnsureMockUsersAsync(CancellationToken ct)
    {
        var created = new List<AppUser>();

        foreach (var (email, name) in MockUsers)
        {
            ct.ThrowIfCancellationRequested();

            if (await _userManager.FindByEmailAsync(email) is not null)
                continue;

            var user = new AppUser
            {
                UserName         = email,
                Email            = email,
                DisplayName      = name,
                Role             = UserRole.User,
                Status           = UserStatus.Active,
                ExternalProvider = "MockSeed",
                ExternalId       = $"mock-{email}",
            };

            // Password is never used — mock users log in via the dev-login panel.
            var result = await _userManager.CreateAsync(user, "MockOnly!0");
            if (result.Succeeded)
            {
                created.Add(user);
                _log.LogInformation("MockDataSeeder: created user {Name} <{Email}>", name, email);
            }
            else
            {
                _log.LogWarning(
                    "MockDataSeeder: could not create {Email}: {Errors}",
                    email, string.Join("; ", result.Errors.Select(e => e.Description)));
            }
        }

        return created;
    }

    // ── Step 2: resolve ISBNs — local cache first, OpenLibrary fallback ───────

    /// <summary>
    /// Returns ISBNs per topic, loading from the local cache file when available
    /// and falling back to OpenLibrary search otherwise. If the network path is
    /// taken the result is written to disk so subsequent runs avoid the API calls.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> ResolveIsbnsByTopicAsync(CancellationToken ct)
    {
        var cached = await TryLoadIsbnCacheAsync(ct);
        if (cached is not null)
        {
            _log.LogInformation(
                "MockDataSeeder: loaded ISBN cache from {Path} (generated {At:u}, {Count} topics)",
                _cacheFilePath, cached.GeneratedAt, cached.IsbnsByTopic.Count);
            return cached.IsbnsByTopic;
        }

        _log.LogInformation(
            "MockDataSeeder: no local cache found — querying OpenLibrary Search API");

        var fresh = await FetchIsbnsByTopicAsync(ct);

        if (fresh.Count > 0)
            await SaveIsbnCacheAsync(fresh, ct);

        return fresh;
    }

    // ── Cache I/O ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to read and deserialise <see cref="_cacheFilePath"/>.
    /// Returns <c>null</c> if the file is absent or cannot be parsed.
    /// </summary>
    private async Task<MockIsbnCache?> TryLoadIsbnCacheAsync(CancellationToken ct)
    {
        if (!File.Exists(_cacheFilePath))
            return null;

        try
        {
            await using var stream = File.OpenRead(_cacheFilePath);
            return await JsonSerializer.DeserializeAsync<MockIsbnCache>(stream, JsonOptions, ct);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "MockDataSeeder: could not read cache file {Path}", _cacheFilePath);
            return null;
        }
    }

    /// <summary>
    /// Serialises <paramref name="isbnsByTopic"/> to <see cref="_cacheFilePath"/>,
    /// creating the <c>seed-data</c> directory if necessary.
    /// </summary>
    private async Task SaveIsbnCacheAsync(
        Dictionary<string, List<string>> isbnsByTopic, CancellationToken ct)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_cacheFilePath)!);

            var payload = new MockIsbnCache
            {
                GeneratedAt  = DateTime.UtcNow,
                IsbnsByTopic = isbnsByTopic,
            };

            await using var stream = File.Create(_cacheFilePath);
            await JsonSerializer.SerializeAsync(stream, payload, JsonOptions, ct);

            _log.LogInformation(
                "MockDataSeeder: ISBN cache written to {Path}", _cacheFilePath);
        }
        catch (Exception ex)
        {
            // Non-fatal — seeding continues without the cache
            _log.LogWarning(ex, "MockDataSeeder: could not write cache file {Path}", _cacheFilePath);
        }
    }

    // ── OpenLibrary Search API ────────────────────────────────────────────────

    /// <summary>
    /// Queries <c>https://openlibrary.org/search.json?q={topic}&amp;fields=isbn&amp;limit=15</c>
    /// for each topic and extracts up to 10 ISBN-13s (falling back to ISBN-10).
    /// Returns only topics that yielded at least one result.
    /// </summary>
    private async Task<Dictionary<string, List<string>>> FetchIsbnsByTopicAsync(CancellationToken ct)
    {
        var result = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        using var http = _httpFactory.CreateClient("MockDataSeeder");

        foreach (var topic in Topics)
        {
            ct.ThrowIfCancellationRequested();

            var url = "https://openlibrary.org/search.json"
                    + $"?q={Uri.EscapeDataString(topic)}&fields=isbn&limit=15";

            _log.LogInformation("MockDataSeeder: searching OpenLibrary for '{Topic}'", topic);

            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, url);
                req.Headers.UserAgent.ParseAdd(
                    "VirtualLibrary/1.0 (https://github.com/tobese/MyVirtualLibrary)");

                using var resp = await http.SendAsync(req, ct);
                if (!resp.IsSuccessStatusCode)
                {
                    _log.LogWarning(
                        "MockDataSeeder: OL search for '{Topic}' returned {Status}",
                        topic, (int)resp.StatusCode);
                    continue;
                }

                var json  = await resp.Content.ReadAsStringAsync(ct);
                var isbns = ParseSearchResponse(json);

                if (isbns.Count > 0)
                {
                    result[topic] = isbns;
                    _log.LogInformation(
                        "MockDataSeeder: '{Topic}' → {Count} ISBNs: {Isbns}",
                        topic, isbns.Count, string.Join(", ", isbns));
                }
                else
                {
                    _log.LogWarning("MockDataSeeder: no ISBNs found for topic '{Topic}'", topic);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "MockDataSeeder: error searching OL for '{Topic}'", topic);
            }

            // Polite inter-request pause (OpenLibrary asks for ≤ 1 req/s on bulk)
            await Task.Delay(600, ct);
        }

        return result;
    }

    /// <summary>
    /// Parses the <c>docs[].isbn</c> arrays from an OpenLibrary /search.json response.
    /// Returns up to 10 unique ISBNs, preferring 13-digit over 10-digit.
    /// </summary>
    private static List<string> ParseSearchResponse(string json)
    {
        var collected = new List<string>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("docs", out var docs)) return collected;

            foreach (var entry in docs.EnumerateArray())
            {
                if (!entry.TryGetProperty("isbn", out var isbnArr)
                    || isbnArr.ValueKind != JsonValueKind.Array)
                    continue;

                string? isbn13 = null;
                string? isbn10 = null;

                foreach (var el in isbnArr.EnumerateArray())
                {
                    var s = el.GetString();
                    if (s is null) continue;
                    if (s.Length == 13) isbn13 ??= s;
                    else if (s.Length == 10) isbn10 ??= s;
                    if (isbn13 is not null) break;
                }

                var chosen = isbn13 ?? isbn10;
                if (chosen is not null && !collected.Contains(chosen))
                    collected.Add(chosen);

                if (collected.Count >= 10) break;
            }
        }
        catch { /* malformed JSON — return whatever was collected */ }

        return collected;
    }

    // ── Step 3: assign books to users ─────────────────────────────────────────

    /// <summary>
    /// Builds a per-user ISBN list and delegates to <see cref="IBulkImportService"/>
    /// to fetch, persist and link each book.
    ///
    /// Distribution logic:
    /// • Popular pool  — first 3 ISBNs per topic → assigned to every user.
    /// • Niche pool    — remaining ISBNs per topic → each user gets a random 2 topics.
    ///
    /// This means several books appear in every library (realistic for bestsellers)
    /// while others are unique to 1–2 users (realistic for personal taste).
    /// </summary>
    private async Task AssignBooksToUsersAsync(
        List<AppUser> users,
        Dictionary<string, List<string>> isbnsByTopic,
        CancellationToken ct)
    {
        var allTopics = isbnsByTopic.Keys.ToList();

        // Books that every user receives — creates visible overlap across accounts
        var popularPool = isbnsByTopic.Values
            .SelectMany(isbns => isbns.Take(3))
            .Distinct()
            .ToList();

        _log.LogInformation(
            "MockDataSeeder: popular pool = {Count} ISBNs shared across all users",
            popularPool.Count);

        foreach (var user in users)
        {
            ct.ThrowIfCancellationRequested();

            var userIsbns = new List<string>(popularPool);

            // 2 randomly chosen topics supply the "long tail" for this user
            var nicheTopics = allTopics.OrderBy(_ => _rng.Next()).Take(2);
            foreach (var topic in nicheTopics)
                userIsbns.AddRange(isbnsByTopic[topic].Skip(3));

            userIsbns = userIsbns.Distinct().ToList();

            var status  = _rng.Next(2) == 0 ? BookStatus.Read : BookStatus.WantToRead;
            var isOwned = _rng.Next(2) == 0;

            _log.LogInformation(
                "MockDataSeeder: importing {Count} books for {Name} (status={Status}, owned={IsOwned})",
                userIsbns.Count, user.DisplayName, status, isOwned);

            try
            {
                var report = await _bulkImport.ImportAsync(user.Id, userIsbns, status, isOwned, ct);
                _log.LogInformation(
                    "MockDataSeeder: {Name} — added={Added}, skipped={Skipped}, notFound={NotFound}, errors={Errors}",
                    user.DisplayName,
                    report.Summary.Added,
                    report.Summary.AlreadyInLibrary,
                    report.Summary.NotFound,
                    report.Summary.Errors);
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "MockDataSeeder: import failed for user {Email}", user.Email);
            }
        }
    }
}

// ── Cache file model ──────────────────────────────────────────────────────────

/// <summary>
/// Represents the contents of <c>seed-data/mock-isbn-cache.json</c>.
/// The file is written after a successful OpenLibrary search and read on
/// subsequent runs to avoid redundant network calls.
/// </summary>
public record MockIsbnCache
{
    /// <summary>UTC timestamp of when this cache was generated.</summary>
    public DateTime GeneratedAt { get; init; }

    /// <summary>Map of topic label → list of ISBNs returned by the OL search.</summary>
    public Dictionary<string, List<string>> IsbnsByTopic { get; init; } = new();
}
#endif
