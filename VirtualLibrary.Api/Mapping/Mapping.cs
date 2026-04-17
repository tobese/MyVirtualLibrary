using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Mapping;

public static class Mapping
{
    public static UserResponse ToResponse(this AppUser user) => new(
        user.Id,
        user.Email,
        user.DisplayName,
        user.ExternalProvider,
        user.Role,
        user.Status,
        user.CreatedAt,
        user.ApprovedBy,
        user.ApprovedAt
    );

    public static AuthorDto ToDto(this Author a) =>
        new(a.OpenLibraryId, a.Name, a.Bio, a.PhotoUrl);

    public static CoverDto ToDto(this Cover c) =>
        new(c.CoverId, c.SmallUrl, c.MediumUrl, c.LargeUrl);

    public static WorkDto? ToDto(this Work? w) => w == null ? null : new WorkDto(
        w.Id, w.OpenLibraryId, w.Title, w.Description, w.FirstPublishYear, w.Subjects ?? new List<string>());

    public static EditionDto ToDto(this Edition e) => new(
        e.Id,
        e.OpenLibraryId,
        e.Isbn10,
        e.Isbn13,
        e.Title,
        e.Publisher,
        e.PublishDate,
        e.NumberOfPages,
        e.Language,
        e.PhysicalDimensions,
        e.Weight,
        e.WorkId,
        (e.EditionAuthors ?? new List<EditionAuthor>())
            .Where(ea => ea.Author != null)
            .Select(ea => ea.Author.ToDto())
            .ToList(),
        (e.Covers ?? new List<Cover>()).Select(c => c.ToDto()).ToList()
    );

    public static UserBookDto ToDto(this UserBook ub) => new(
        ub.Id,
        ub.EditionId,
        ub.Edition.ToDto(),
        ub.Status,
        ub.DateAdded,
        ub.Rating,
        ub.Notes
    );
}
