using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Google.Apis.Auth;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;

namespace VirtualLibrary.Api.Services;

/// <summary>
/// Claims extracted from a validated external identity token.
/// </summary>
public record ExternalIdentity(
    string ExternalId,
    string Email,
    string? DisplayName
);

/// <summary>
/// Validates an external provider ID token and returns the verified claims.
/// Returns <see langword="null"/> if the token is invalid or the provider is unknown.
/// </summary>
public interface IExternalTokenValidatorFactory
{
    Task<ExternalIdentity?> ValidateAsync(
        string provider,
        string idToken,
        CancellationToken ct = default);
}

/// <summary>
/// Production implementation that validates Google and Apple ID tokens using
/// each provider's published public keys.
///
/// <para><b>Google</b>: uses <c>GoogleJsonWebSignature.ValidateAsync</c> which
/// fetches Google's certificate bundle and verifies the RS256 signature,
/// expiry, issuer, and audience in one call.</para>
///
/// <para><b>Apple</b>: uses Apple's OIDC discovery document
/// (<c>https://appleid.apple.com/.well-known/openid-configuration</c>) to
/// retrieve the signing keys, then validates the ES256 / RS256 JWT with
/// <c>JwtSecurityTokenHandler</c>.  The discovery document is cached
/// automatically by <c>ConfigurationManager</c> and refreshed after 24 h.</para>
/// </summary>
public sealed class ExternalTokenValidatorFactory : IExternalTokenValidatorFactory
{
    private const string AppleDiscoveryUrl =
        "https://appleid.apple.com/.well-known/openid-configuration";

    private readonly string _googleClientId;
    private readonly string _appleClientId;
    private readonly ILogger<ExternalTokenValidatorFactory> _logger;

    // Cached OIDC configuration for Apple (singleton-friendly).
    private static readonly ConfigurationManager<OpenIdConnectConfiguration> _appleConfig = new(
        AppleDiscoveryUrl,
        new OpenIdConnectConfigurationRetriever(),
        new HttpDocumentRetriever { RequireHttps = true });

    public ExternalTokenValidatorFactory(
        IConfiguration configuration,
        ILogger<ExternalTokenValidatorFactory> logger)
    {
        _googleClientId = configuration["Auth:Google:ClientId"] ?? "";
        _appleClientId  = configuration["Auth:Apple:ClientId"]  ?? "";
        _logger = logger;
    }

    public Task<ExternalIdentity?> ValidateAsync(
        string provider,
        string idToken,
        CancellationToken ct = default)
        => provider.ToLowerInvariant() switch
        {
            "google" => ValidateGoogleAsync(idToken, ct),
            "apple"  => ValidateAppleAsync(idToken, ct),
            _        => Task.FromResult<ExternalIdentity?>(null),
        };

    // ----------------------------------------------------------------
    // Google
    // ----------------------------------------------------------------
    private async Task<ExternalIdentity?> ValidateGoogleAsync(
        string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_googleClientId))
        {
            _logger.LogError(
                "Auth:Google:ClientId is not configured. " +
                "Set it via user secrets or an environment variable.");
            return null;
        }

        try
        {
            var settings = new GoogleJsonWebSignature.ValidationSettings
            {
                // Audience must match one of the registered client IDs.
                Audience = new[] { _googleClientId },
            };

            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, settings)
                .WaitAsync(ct);

            return new ExternalIdentity(payload.Subject, payload.Email, payload.Name);
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Google ID token validation failed.");
            return null;
        }
    }

    // ----------------------------------------------------------------
    // Apple
    // ----------------------------------------------------------------
    private async Task<ExternalIdentity?> ValidateAppleAsync(
        string idToken, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(_appleClientId))
        {
            _logger.LogError(
                "Auth:Apple:ClientId is not configured. " +
                "Set it via user secrets or an environment variable.");
            return null;
        }

        try
        {
            // Fetch (or use cached) Apple signing keys via OIDC discovery.
            var oidcConfig = await _appleConfig
                .GetConfigurationAsync(ct)
                .WaitAsync(ct);

            var parameters = new TokenValidationParameters
            {
                ValidIssuer            = "https://appleid.apple.com",
                ValidAudience          = _appleClientId,
                IssuerSigningKeys      = oidcConfig.SigningKeys,
                ValidateLifetime       = true,
                ValidateIssuerSigningKey = true,
                // Apple tokens can omit the email claim for privacy-relay addresses;
                // we handle that gracefully below.
                RequireSignedTokens    = true,
            };

            var handler    = new JwtSecurityTokenHandler();
            var principal  = handler.ValidateToken(idToken, parameters, out _);

            var sub   = principal.FindFirstValue(ClaimTypes.NameIdentifier)
                     ?? principal.FindFirstValue("sub");
            var email = principal.FindFirstValue(ClaimTypes.Email)
                     ?? principal.FindFirstValue("email");
            var name  = principal.FindFirstValue(ClaimTypes.GivenName)
                     ?? principal.FindFirstValue("name");

            if (string.IsNullOrEmpty(sub) || string.IsNullOrEmpty(email))
            {
                _logger.LogWarning(
                    "Apple ID token validated but 'sub' or 'email' claim is missing.");
                return null;
            }

            return new ExternalIdentity(sub, email, name);
        }
        catch (SecurityTokenException ex)
        {
            _logger.LogWarning(ex, "Apple ID token validation failed.");
            return null;
        }
    }
}
