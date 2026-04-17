namespace VirtualLibrary.Client.Services;

/// <summary>
/// Public OAuth client identifiers used by <see cref="Pages.LoginPage"/> to
/// build the authorization URL for each provider.
///
/// <para><b>These values are NOT secrets</b> — they appear in browser URLs and
/// can safely be committed.  Private keys and client secrets belong in
/// <c>appsettings.json</c> / user secrets / environment variables on the
/// <em>server</em>, never here.</para>
///
/// <para><b>How to obtain the values:</b></para>
/// <list type="bullet">
///   <item>
///     <b>Google WASM</b>: Google Cloud Console → APIs &amp; Services →
///     Credentials → Create OAuth 2.0 Client ID (type: Web application).
///     Add <c>http://localhost:5000</c> (dev) and your production origin to
///     Authorized JavaScript origins and Authorized redirect URIs.
///   </item>
///   <item>
///     <b>Google Android</b>: Google Cloud Console → same project →
///     Create OAuth 2.0 Client ID (type: Android).  You need your app's
///     SHA-1 fingerprint and package name (<c>com.virtuallibrary.client</c>).
///     On Android the sign-in flow uses the web client ID as audience, so
///     set <c>GoogleAndroidClientId</c> to the <em>Web application</em> client ID
///     (the Android client ID is used internally by Google Play Services).
///   </item>
///   <item>
///     <b>Apple</b>: Apple Developer → Certificates, IDs &amp; Profiles →
///     Identifiers → Services IDs.  Create a Services ID, enable Sign In with
///     Apple, and add your domain + redirect URIs.
///   </item>
/// </list>
/// </summary>
public static class OAuthConfig
{
#if __WASM__
    // ── WASM (browser) ────────────────────────────────────────────────
    // Fill in the "Web application" OAuth 2.0 Client ID from Google Cloud Console.
    // Example: "123456789-abcdefghij.apps.googleusercontent.com"
    public const string GoogleClientId = "";

    // Fill in your Apple Services ID (the one you register in the Apple Developer portal).
    // Example: "com.yourcompany.virtualibrary.web"
    public const string AppleClientId = "";

#elif __ANDROID__
    // ── Android ───────────────────────────────────────────────────────
    // For Android native sign-in via WebAuthenticationBroker / Chrome Custom Tabs,
    // use the Web application client ID as the OIDC audience (Google Play Services
    // handles the Android-specific client ID transparently).
    public const string GoogleClientId = "";

    // Apple Sign In on Android uses the web Services ID.
    public const string AppleClientId = "";

#else
    // ── Desktop / other ───────────────────────────────────────────────
    public const string GoogleClientId = "";
    public const string AppleClientId  = "";
#endif
}
