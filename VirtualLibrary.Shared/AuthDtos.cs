namespace VirtualLibrary.Shared;

public record ExternalLoginRequest(string Provider, string IdToken);

public record PasswordLoginRequest(string Email, string Password);

public record UserResponse(
    string Id,
    string? Email,
    string? DisplayName,
    string? ExternalProvider,
    UserRole Role,
    UserStatus Status,
    DateTime CreatedAt,
    string? ApprovedBy,
    DateTime? ApprovedAt
);

public record ApproveUserRequest(bool Approved);

public record ChangeRoleRequest(UserRole Role);

public record AuthResponse(string Token, UserResponse User);
