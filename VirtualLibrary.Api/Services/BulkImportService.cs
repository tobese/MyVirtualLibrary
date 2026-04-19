using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Services;

public interface IBulkImportService
{
    /// <summary>
    /// Processes a list of ISBNs for <paramref name="userId"/>:
    /// fetches / refreshes OpenLibrary metadata for each and adds missing
    /// entries to the user's library.
    /// </summary>
    Task<BulkImportResponse> ImportAsync(
        string userId,
        IReadOnlyList<string> isbns,
        BookStatus defaultStatus,
        bool defaultIsOwned,
        CancellationToken ct = default);
}

public class BulkImportService : IBulkImportService
{
    private readonly IOpenLibraryClient         _ol;
    private readonly AppDbContext               _db;
    private readonly ILogger<BulkImportService> _log;

    public BulkImportService(
        IOpenLibraryClient ol, AppDbContext db,
        ILogger<BulkImportService> log)
    {
        _ol  = ol;
        _db  = db;
        _log = log;
    }

    public async Task<BulkImportResponse> ImportAsync(
        string userId,
        IReadOnlyList<string> isbns,
        BookStatus defaultStatus,
        bool defaultIsOwned,
        CancellationToken ct = default)
    {
        var results = new List<ImportRowResult>(isbns.Count);

        foreach (var rawIsbn in isbns)
        {
            if (ct.IsCancellationRequested) break;

            var isbn = rawIsbn.Trim();
            _log.LogInformation("Bulk import: processing ISBN {Isbn}", isbn);

            ImportRowResult row;
            try
            {
                row = await ProcessIsbnAsync(userId, isbn, defaultStatus, defaultIsOwned, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Bulk import: unhandled error for ISBN {Isbn}", isbn);
                row = new ImportRowResult(isbn, ImportRowStatus.Error, null, false, ex.Message);
            }

            results.Add(row);
            _log.LogInformation(
                "Bulk import: {Isbn} → {Status} (dataRefreshed={DataRefreshed})",
                isbn, row.Status, row.DataRefreshed);
        }

        return new BulkImportResponse(results, BuildSummary(results));
    }

    // ── Per-row processing ────────────────────────────────────────────────────

    private async Task<ImportRowResult> ProcessIsbnAsync(
        string userId, string isbn,
        BookStatus defaultStatus, bool defaultIsOwned,
        CancellationToken ct)
    {
        var (edition, dataRefreshed) = await _ol.FetchAndSyncAsync(isbn, ct);

        if (edition == null)
            return new ImportRowResult(isbn, ImportRowStatus.NotFound, null, false,
                "ISBN not found on OpenLibrary.");

        var existing = await _db.UserBooks
            .FirstOrDefaultAsync(ub => ub.UserId == userId && ub.EditionId == edition.Id, ct);

        if (existing != null)
        {
            var msg = dataRefreshed
                ? "Already in your library. OpenLibrary metadata was refreshed."
                : "Already in your library.";
            return new ImportRowResult(isbn, ImportRowStatus.AlreadyInLibrary,
                edition.Title, dataRefreshed, msg);
        }

        _db.UserBooks.Add(new UserBook
        {
            UserId    = userId,
            EditionId = edition.Id,
            Status    = defaultStatus,
            IsOwned   = defaultIsOwned,
        });
        await _db.SaveChangesAsync(ct);

        return new ImportRowResult(isbn, ImportRowStatus.Added,
            edition.Title, dataRefreshed, null);
    }

    // ── Summary ───────────────────────────────────────────────────────────────

    private static ImportSummary BuildSummary(List<ImportRowResult> results) => new(
        Total:            results.Count,
        Added:            results.Count(r => r.Status == ImportRowStatus.Added),
        AlreadyInLibrary: results.Count(r => r.Status == ImportRowStatus.AlreadyInLibrary),
        DataRefreshed:    results.Count(r => r.DataRefreshed),
        NotFound:         results.Count(r => r.Status == ImportRowStatus.NotFound),
        Errors:           results.Count(r => r.Status == ImportRowStatus.Error));
}
