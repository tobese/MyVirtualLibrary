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
