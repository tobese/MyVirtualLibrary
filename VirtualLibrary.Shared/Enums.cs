namespace VirtualLibrary.Shared;

public enum UserRole
{
    User,
    Admin,
    SuperAdmin
}

public enum UserStatus
{
    PendingApproval,
    Active,
    Rejected,
    Suspended
}

/// <summary>
/// Reading state of a book. Ownership is tracked separately via
/// <c>UserBook.IsOwned</c> so a book can be both owned and in any
/// reading state at the same time.
/// </summary>
public enum BookStatus
{
    WantToRead,
    Read
}
