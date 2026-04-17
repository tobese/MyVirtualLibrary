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

public enum BookStatus
{
    WantToRead,
    Owned,
    Read
}
