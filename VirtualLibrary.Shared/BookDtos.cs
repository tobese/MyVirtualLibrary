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

public record UserBookDto(
    Guid Id,
    Guid EditionId,
    EditionDto Edition,
    BookStatus Status,
    DateTime DateAdded,
    int? Rating,
    string? Notes
);

public record AddUserBookRequest(string Isbn, BookStatus Status = BookStatus.Owned);

public record UpdateUserBookRequest(BookStatus? Status, int? Rating, string? Notes);

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
