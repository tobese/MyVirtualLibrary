namespace VirtualLibrary.Shared;

public record LanguageCountDto(string Language, int Count);

public record RecentEditionDto(
    Guid   Id,
    string Title,
    string? Isbn13,
    string? Publisher,
    string? PublishDate,
    List<string> Authors,
    string? CoverUrl
);

public record CatalogSummaryDto(
    int TotalEditions,
    int TotalWorks,
    int TotalAuthors,
    int UniqueSubjects,
    int EditionsWithCovers,
    List<RankedItemDto>   TopAuthors,
    List<RankedItemDto>   TopSubjects,
    List<LanguageCountDto> Languages,
    List<RecentEditionDto> RecentlyAdded
);

public record AuthorSummaryDto(
    Guid    Id,
    string  Name,
    string? Bio,
    string? PhotoUrl,
    int     EditionCount
);

public record AuthorListDto(
    List<AuthorSummaryDto> Authors,
    int Total,
    int Page,
    int PageSize
);
