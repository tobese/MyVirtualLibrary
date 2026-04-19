using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Models;

/// <summary>
/// Abstract book concept. Groups editions together.
/// </summary>
public class Work
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OpenLibraryId { get; set; }
    public string Title { get; set; } = "";
    public string? Description { get; set; }
    public int? FirstPublishYear { get; set; }
    public List<string> Subjects { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    /// <summary>last_modified as reported by OpenLibrary. Used to skip re-fetching unchanged records.</summary>
    public DateTime? OlLastModified { get; set; }

    public List<Edition> Editions { get; set; } = new();
}

/// <summary>
/// Specific printing / ISBN-identified copy of a Work.
/// </summary>
public class Edition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OpenLibraryId { get; set; }
    public string? Isbn10 { get; set; }
    public string? Isbn13 { get; set; }
    public string Title { get; set; } = "";
    public string? Publisher { get; set; }
    public string? PublishDate { get; set; }
    public int? NumberOfPages { get; set; }
    public string? Language { get; set; }
    public string? PhysicalDimensions { get; set; }
    public string? Weight { get; set; }
    public DateTime CachedAt { get; set; } = DateTime.UtcNow;
    /// <summary>last_modified as reported by OpenLibrary. Used to skip re-fetching unchanged records.</summary>
    public DateTime? OlLastModified { get; set; }

    public Guid? WorkId { get; set; }
    public Work? Work { get; set; }

    public List<EditionAuthor> EditionAuthors { get; set; } = new();
    public List<Cover> Covers { get; set; } = new();
}

public class Author
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string? OpenLibraryId { get; set; }
    public string Name { get; set; } = "";
    public string? BirthDate { get; set; }
    public string? Bio { get; set; }
    public string? PhotoUrl { get; set; }
    /// <summary>last_modified as reported by OpenLibrary. Used to skip re-fetching unchanged records.</summary>
    public DateTime? OlLastModified { get; set; }

    public List<EditionAuthor> EditionAuthors { get; set; } = new();
}

public class EditionAuthor
{
    public Guid EditionId { get; set; }
    public Edition Edition { get; set; } = null!;
    public Guid AuthorId { get; set; }
    public Author Author { get; set; } = null!;
}

public class Cover
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public long? CoverId { get; set; }
    public Guid EditionId { get; set; }
    public Edition Edition { get; set; } = null!;
    public string? SmallUrl { get; set; }
    public string? MediumUrl { get; set; }
    public string? LargeUrl { get; set; }
    public string? CachedPath { get; set; }
}

/// <summary>
/// A user's relationship to a particular edition. Unique on (UserId, EditionId).
/// Ownership (<see cref="IsOwned"/>) and reading state (<see cref="Status"/>)
/// are tracked independently so a book can be both owned and in any reading state.
/// </summary>
public class UserBook
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string UserId { get; set; } = "";
    public AppUser User { get; set; } = null!;
    public Guid EditionId { get; set; }
    public Edition Edition { get; set; } = null!;
    public BookStatus Status { get; set; } = BookStatus.WantToRead;
    /// <summary>True when the user physically owns a copy of this edition.</summary>
    public bool IsOwned { get; set; } = false;
    public DateTime DateAdded { get; set; } = DateTime.UtcNow;
    public int? Rating { get; set; }
    public string? Notes { get; set; }

    public List<ReadRecord> ReadRecords { get; set; } = new();
}

/// <summary>
/// A single recorded reading of a <see cref="UserBook"/>.
/// Multiple records allow tracking re-reads over time.
/// </summary>
public class ReadRecord
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserBookId { get; set; }
    public UserBook UserBook { get; set; } = null!;
    /// <summary>Date the reading was completed (or logged).</summary>
    public DateTime DateRead { get; set; } = DateTime.UtcNow;
    public string? Notes { get; set; }
}

public class Shelf
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string OwnerUserId { get; set; } = "";
    public AppUser Owner { get; set; } = null!;
    public string Name { get; set; } = "";
    public int Rows { get; set; }
    public int SlotsPerRow { get; set; }

    public List<ShelfPlacement> Placements { get; set; } = new();
}

public class ShelfPlacement
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid ShelfId { get; set; }
    public Shelf Shelf { get; set; } = null!;
    public Guid UserBookId { get; set; }
    public UserBook UserBook { get; set; } = null!;
    public int X { get; set; }
    public int Y { get; set; }
    public int Slot { get; set; }
}
