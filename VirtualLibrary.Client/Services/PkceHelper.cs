using System.Security.Cryptography;
using System.Text;

namespace VirtualLibrary.Client.Services;

/// <summary>
/// Generates the PKCE <c>code_verifier</c> / <c>code_challenge</c> pair
/// required for the OAuth 2.0 Authorization Code flow with PKCE
/// (RFC 7636, S256 method).
/// </summary>
public static class PkceHelper
{
    /// <summary>
    /// Creates a cryptographically random code verifier (43–128 chars,
    /// base64url-encoded, no padding).
    /// </summary>
    public static string GenerateCodeVerifier()
    {
        var bytes = RandomNumberGenerator.GetBytes(32); // 256 bits
        return Base64UrlEncode(bytes);
    }

    /// <summary>
    /// Derives the S256 code challenge from <paramref name="codeVerifier"/>:
    /// <c>BASE64URL(SHA256(ASCII(verifier)))</c>.
    /// </summary>
    public static string GenerateCodeChallenge(string codeVerifier)
    {
        var hash = SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier));
        return Base64UrlEncode(hash);
    }

    private static string Base64UrlEncode(byte[] bytes)
        => Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
}
