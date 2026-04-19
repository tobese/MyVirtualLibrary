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

/// <summary>
/// PKCE authorization code exchange request.
/// The client obtains the <paramref name="Code"/> from the provider's
/// redirect, sends both <paramref name="Code"/> and the original
/// <paramref name="CodeVerifier"/> to the API, which performs the
/// server-side token exchange and validates the resulting ID token.
/// </summary>
public record TokenExchangeRequest(
    string Provider,
    string Code,
    string CodeVerifier,
    string RedirectUri
);

#if DEBUG
/// <summary>
/// DEV ONLY — issued by the dev-login endpoint and consumed by DevLoginPage.
/// Not compiled into Release builds.
/// </summary>
public record DevLoginRequest(string Persona);
#endif
