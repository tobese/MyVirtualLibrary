namespace VirtualLibrary.Shared;

public record BestsellerEntryDto(
    int Rank,
    string Title,
    string Author,
    string? Description,
    string? Isbn13,
    string? Publisher,
    string? CoverUrl,
    int WeeksOnList
);

public record BestsellerListDto(
    string ListName,
    string ListNameEncoded,
    string PublishedDate,
    List<BestsellerEntryDto> Books
);

public record TrendingWorkDto(
    string OlKey,
    string Title,
    List<string> Authors,
    string? CoverUrl,
    Guid? LocalWorkId
);

public record TrendingResultDto(
    string Period,
    List<TrendingWorkDto> Works
);
