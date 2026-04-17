using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.IdentityModel.Tokens;

namespace VirtualLibrary.Api.Services;

/// <summary>
/// Exchanges a PKCE authorization code for an ID token using the selected
/// provider's token endpoint.
///
/// <list type="bullet">
///   <item>
///     <b>Google</b>: public-client code exchange — no <c>client_secret</c> needed
///     for PKCE flows.  Token endpoint: <c>https://oauth2.googleapis.com/token</c>.
///   </item>
///   <item>
///     <b>Apple</b>: private-key client secret — a short-lived JWT signed with the
///     ES256 key from <c>Auth:Apple:PrivateKey</c> is generated on each request.
///     Token endpoint: <c>https://appleid.apple.com/auth/token</c>.
///   </item>
/// </list>
/// </summary>
public interface ITokenExchangeService
{
    /// <summary>
    /// Returns the raw <c>id_token</c> JWT from the provider, or
    /// <see langword="null"/> if the exchange fails.
    /// </summary>
    Task<string?> ExchangeCodeForIdTokenAsync(
        string provider,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken ct = default);
}

public sealed class TokenExchangeService : ITokenExchangeService
{
    private readonly IHttpClientFactory _httpFactory;
    private readonly IConfiguration _config;
    private readonly ILogger<TokenExchangeService> _logger;

    public TokenExchangeService(
        IHttpClientFactory httpFactory,
        IConfiguration config,
        ILogger<TokenExchangeService> logger)
    {
        _httpFactory = httpFactory;
        _config = config;
        _logger = logger;
    }

    public Task<string?> ExchangeCodeForIdTokenAsync(
        string provider,
        string code,
        string codeVerifier,
        string redirectUri,
        CancellationToken ct = default)
        => provider.ToLowerInvariant() switch
        {
            "google" => ExchangeGoogleAsync(code, codeVerifier, redirectUri, ct),
            "apple"  => ExchangeAppleAsync(code, codeVerifier, redirectUri, ct),
            _        => Task.FromResult<string?>(null),
        };

    // ----------------------------------------------------------------
    // Google — public client PKCE (no client_secret required)
    // ----------------------------------------------------------------
    private async Task<string?> ExchangeGoogleAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var clientId = _config["Auth:Google:ClientId"];
        if (string.IsNullOrWhiteSpace(clientId))
        {
            _logger.LogError("Auth:Google:ClientId is not configured.");
            return null;
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code",
        });

        return await PostTokenAsync("https://oauth2.googleapis.com/token", form, ct);
    }

    // ----------------------------------------------------------------
    // Apple — private-key client_secret (ES256-signed JWT, short-lived)
    // ----------------------------------------------------------------
    private async Task<string?> ExchangeAppleAsync(
        string code, string codeVerifier, string redirectUri, CancellationToken ct)
    {
        var clientId  = _config["Auth:Apple:ClientId"];
        var teamId    = _config["Auth:Apple:TeamId"];
        var keyId     = _config["Auth:Apple:KeyId"];
        var privateKey = _config["Auth:Apple:PrivateKey"];

        if (string.IsNullOrWhiteSpace(clientId)  ||
            string.IsNullOrWhiteSpace(teamId)     ||
            string.IsNullOrWhiteSpace(keyId)      ||
            string.IsNullOrWhiteSpace(privateKey))
        {
            _logger.LogError(
                "One or more Auth:Apple:* config values are not set. " +
                "Required: ClientId, TeamId, KeyId, PrivateKey.");
            return null;
        }

        string clientSecret;
        try
        {
            clientSecret = GenerateAppleClientSecret(
                clientId, teamId, keyId, privateKey);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate Apple client_secret JWT.");
            return null;
        }

        var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["code"]          = code,
            ["client_id"]     = clientId,
            ["client_secret"] = clientSecret,
            ["code_verifier"] = codeVerifier,
            ["redirect_uri"]  = redirectUri,
            ["grant_type"]    = "authorization_code",
        });

        return await PostTokenAsync("https://appleid.apple.com/auth/token", form, ct);
    }

    // ----------------------------------------------------------------
    // Shared HTTP helper
    // ----------------------------------------------------------------
    private async Task<string?> PostTokenAsync(
        string tokenEndpoint, HttpContent form, CancellationToken ct)
    {
        using var http = _httpFactory.CreateClient();
        var response = await http.PostAsync(tokenEndpoint, form, ct);

        var body = await response.Content.ReadAsStringAsync(ct);
        if (!response.IsSuccessStatusCode)
        {
            _logger.LogWarning(
                "Token exchange failed ({Status}): {Body}",
                (int)response.StatusCode, body);
            return null;
        }

        var json = JsonDocument.Parse(body);
        if (json.RootElement.TryGetProperty("id_token", out var idTokenEl))
            return idTokenEl.GetString();

        _logger.LogWarning("Token exchange response did not contain 'id_token'.");
        return null;
    }

    // ----------------------------------------------------------------
    // Apple client_secret — a short-lived ES256 JWT signed with the
    // developer's .p8 private key.
    // https://developer.apple.com/documentation/accountorganizationaldatasharing/creating-a-client-secret
    // ----------------------------------------------------------------
    private static string GenerateAppleClientSecret(
        string clientId, string teamId, string keyId, string privateKeyPem)
    {
        // Accept either raw base64 or a full PEM block.
        var base64 = privateKeyPem
            .Replace("-----BEGIN PRIVATE KEY-----", "")
            .Replace("-----END PRIVATE KEY-----", "")
            .Replace("-----BEGIN EC PRIVATE KEY-----", "")
            .Replace("-----END EC PRIVATE KEY-----", "")
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        using var ecdsa = ECDsa.Create();
        ecdsa.ImportPkcs8PrivateKey(Convert.FromBase64String(base64), out _);

        var securityKey = new ECDsaSecurityKey(ecdsa) { KeyId = keyId };
        var credentials = new SigningCredentials(
            securityKey, SecurityAlgorithms.EcdsaSha256);

        var now = DateTimeOffset.UtcNow;
        var token = new JwtSecurityToken(
            issuer:   teamId,
            audience: "https://appleid.apple.com",
            claims: new[]
            {
                new Claim("sub", clientId),
                // Apple requires an explicit iat claim (issued-at, Unix epoch seconds).
                new Claim("iat",
                    now.ToUnixTimeSeconds().ToString(),
                    ClaimValueTypes.Integer64),
            },
            notBefore: now.UtcDateTime,
            expires:   now.AddMinutes(5).UtcDateTime,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
