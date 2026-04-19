namespace VirtualLibrary.Shared;

/// <summary>A name + count pair used for ranked lists (top authors, top subjects).</summary>
public record RankedItemDto(string Name, int Count);

/// <summary>
/// Whole-library collection statistics returned by GET /api/stats.
/// Only accessible to Admin and SuperAdmin roles.
/// </summary>
public record LibraryStatsDto(
    // ── Catalogue ─────────────────────────────────────────────────────────────
    /// <summary>Distinct editions (ISBNs) in the catalogue.</summary>
    int TotalEditions,
    /// <summary>Distinct works (abstract book concepts) in the catalogue.</summary>
    int TotalWorks,
    /// <summary>Distinct authors in the catalogue.</summary>
    int UniqueAuthors,
    /// <summary>Distinct subjects across all works.</summary>
    int UniqueSubjects,

    // ── User-book relationships ────────────────────────────────────────────────
    /// <summary>Total user↔book relationships across all members.</summary>
    int TotalUserBooks,
    /// <summary>User-book entries marked as owned.</summary>
    int OwnedBooks,
    /// <summary>User-book entries on the wish-list (not owned).</summary>
    int WishlistBooks,
    /// <summary>User-book entries with status Read.</summary>
    int ReadBooks,
    /// <summary>User-book entries with status Want To Read.</summary>
    int UnreadBooks,
    /// <summary>Total individual read-records logged (re-reads counted separately).</summary>
    int TotalReadRecords,

    // ── Members ───────────────────────────────────────────────────────────────
    /// <summary>Members with Active status.</summary>
    int ActiveMembers,

    // ── Top lists ─────────────────────────────────────────────────────────────
    /// <summary>Top 10 authors by number of editions in the catalogue.</summary>
    List<RankedItemDto> TopAuthors,
    /// <summary>Top 10 subjects by number of works that carry them.</summary>
    List<RankedItemDto> TopSubjects
);
