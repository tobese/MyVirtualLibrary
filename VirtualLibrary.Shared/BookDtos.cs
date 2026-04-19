namespace VirtualLibrary.Shared;

public record AuthorDto(
    string? OpenLibraryId,
    string Name,
    string? Bio,
    string? PhotoUrl
);

public record CoverDto(
    long? CoverId,
    string? SmallUrl,
    string? MediumUrl,
    string? LargeUrl
);

public record EditionDto(
    Guid Id,
    string? OpenLibraryId,
    string? Isbn10,
    string? Isbn13,
    string Title,
    string? Publisher,
    string? PublishDate,
    int? NumberOfPages,
    string? Language,
    string? PhysicalDimensions,
    string? Weight,
    Guid? WorkId,
    List<AuthorDto> Authors,
    List<CoverDto> Covers
);

public record WorkDto(
    Guid Id,
    string? OpenLibraryId,
    string Title,
    string? Description,
    int? FirstPublishYear,
    List<string> Subjects
);

public record ReadRecordDto(
    Guid Id,
    DateTime DateRead,
    string? Notes
);

public record UserBookDto(
    Guid Id,
    Guid EditionId,
    EditionDto Edition,
    BookStatus Status,
    bool IsOwned,
    DateTime DateAdded,
    int? Rating,
    string? Notes,
    List<ReadRecordDto> ReadRecords
);

/// <summary>
/// Adds a book to the library. Defaults to owned + want-to-read, the most
/// common case when scanning or typing an ISBN.
/// </summary>
public record AddUserBookRequest(
    string Isbn,
    BookStatus Status = BookStatus.WantToRead,
    bool IsOwned = true);

public record UpdateUserBookRequest(
    BookStatus? Status,
    bool? IsOwned,
    int? Rating,
    string? Notes);

/// <summary>
/// Records a single reading of a book. DateRead defaults to today when null.
/// </summary>
public record AddReadRecordRequest(DateTime? DateRead, string? Notes);

public record IsbnLookupResponse(EditionDto Edition, WorkDto? Work);

// ---- Virtual shelf ----

/// <summary>
/// A single placement slot on a shelf, ordered by <see cref="Slot"/>.
/// </summary>
public record ShelfPlacementDto(
    Guid Id,
    Guid UserBookId,
    int Slot,
    UserBookDto Book
);

/// <summary>
/// A shelf with its full ordered placement list. Books owned by the user
/// but not yet placed are appended at the end by the API.
/// </summary>
public record ShelfDto(
    Guid Id,
    string Name,
    List<ShelfPlacementDto> Placements
);

/// <summary>
/// Replaces all placements on a shelf. The order of
/// <see cref="UserBookIds"/> defines the new slot sequence (0-based).
/// </summary>
public record SaveShelfPlacementsRequest(List<Guid> UserBookIds);

// ---- Bulk import ----

public enum ImportRowStatus
{
    /// <summary>Book added to the user's library.</summary>
    Added,
    /// <summary>Book was already in the user's library (no change).</summary>
    AlreadyInLibrary,
    /// <summary>ISBN not found on OpenLibrary.</summary>
    NotFound,
    /// <summary>An unexpected error occurred for this row.</summary>
    Error,
}

/// <summary>
/// Request body for POST /api/import.
/// ISBNs are plain strings (ISBN-10 or ISBN-13); blank lines and
/// lines starting with '#' are stripped by the API.
/// </summary>
public record BulkImportRequest(
    List<string> Isbns,
    BookStatus DefaultStatus = BookStatus.WantToRead,
    bool DefaultIsOwned = true);

/// <summary>Per-row result returned by the bulk import endpoint.</summary>
public record ImportRowResult(
    string Isbn,
    ImportRowStatus Status,
    /// <summary>Title from the OpenLibrary record, if found.</summary>
    string? Title,
    /// <summary>
    /// True when pre-existing OpenLibrary metadata (Edition / Work / Authors)
    /// was updated because OpenLibrary reported a newer last_modified timestamp.
    /// </summary>
    bool DataRefreshed,
    /// <summary>Human-readable detail, mainly populated for errors and skips.</summary>
    string? Message);

public record ImportSummary(
    int Total,
    int Added,
    int AlreadyInLibrary,
    int DataRefreshed,
    int NotFound,
    int Errors);

public record BulkImportResponse(
    List<ImportRowResult> Results,
    ImportSummary Summary);
