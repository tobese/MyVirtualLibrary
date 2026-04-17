# Virtual Library — Entity-Relationship Diagram
This diagram is the source of truth for the relational schema managed by `AppDbContext`
in `VirtualLibrary.Api/Data/AppDbContext.cs`. Render it with any Mermaid-aware viewer
(e.g. GitHub, the [Mermaid Live Editor](https://mermaid.live), or VS Code's Markdown preview).
```mermaid
erDiagram
    AppUser ||--o{ UserBook       : owns
    AppUser ||--o{ Shelf          : owns
    Work    ||--o{ Edition        : "has editions"
    Edition ||--o{ UserBook       : "tracked by"
    Edition ||--o{ Cover          : "has covers"
    Edition ||--o{ EditionAuthor  : "lists"
    Author  ||--o{ EditionAuthor  : "writes"
    Shelf   ||--o{ ShelfPlacement : "contains"
    UserBook ||--o{ ShelfPlacement : "placed at"

    AppUser {
        string   Id PK
        string   Email
        string   UserName
        string   PasswordHash
        string   DisplayName
        string   ExternalProvider
        string   ExternalId
        string   Role
        string   Status
        DateTime CreatedAt
        string   ApprovedBy
        DateTime ApprovedAt
    }

    Work {
        Guid     Id PK
        string   OpenLibraryId UK
        string   Title
        string   Description
        int      FirstPublishYear
        jsonb    Subjects
        DateTime CreatedAt
    }

    Edition {
        Guid     Id PK
        string   OpenLibraryId UK
        string   Isbn10
        string   Isbn13
        string   Title
        string   Publisher
        string   PublishDate
        int      NumberOfPages
        string   Language
        string   PhysicalDimensions
        string   Weight
        DateTime CachedAt
        Guid     WorkId FK
    }

    Author {
        Guid   Id PK
        string OpenLibraryId UK
        string Name
        string BirthDate
        string Bio
        string PhotoUrl
    }

    EditionAuthor {
        Guid EditionId PK,FK
        Guid AuthorId  PK,FK
    }

    Cover {
        Guid   Id PK
        long   CoverId
        Guid   EditionId FK
        string SmallUrl
        string MediumUrl
        string LargeUrl
        string CachedPath
    }

    UserBook {
        Guid     Id PK
        string   UserId FK
        Guid     EditionId FK
        string   Status
        DateTime DateAdded
        int      Rating
        string   Notes
    }

    Shelf {
        Guid   Id PK
        string OwnerUserId FK
        string Name
        int    Rows
        int    SlotsPerRow
    }

    ShelfPlacement {
        Guid Id PK
        Guid ShelfId FK
        Guid UserBookId FK
        int  X
        int  Y
        int  Slot
    }
```
## Notes
* `AppUser` inherits from ASP.NET Core Identity's `IdentityUser` — additional identity
  tables (`AspNetRoles`, `AspNetUserClaims`, `AspNetUserLogins`, `AspNetUserTokens`,
  `AspNetUserRoles`) exist in the database but are omitted here for readability.
* `UserBook` is unique on `(UserId, EditionId)`.
* `Work.Subjects` is stored as PostgreSQL `jsonb` via an EF Core value conversion.
* `AppUser.Role` and `AppUser.Status` are persisted as strings (`HasConversion<string>()`).
