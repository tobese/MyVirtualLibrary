using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class LoginPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public LoginPage()
    {
        this.InitializeComponent();
    }

    private async void OnGoogleSignIn(object sender, RoutedEventArgs e)
    {
        await SignInWithProviderAsync("Google");
    }

    private async void OnAppleSignIn(object sender, RoutedEventArgs e)
    {
        await SignInWithProviderAsync("Apple");
    }

    private async void OnPasswordSignIn(object sender, RoutedEventArgs e)
    {
        try
        {
            StatusText.Text = "Signing in...";
            var res = await _api.PasswordLoginAsync(EmailBox.Text, PasswordBox.Password);
            if (res == null) { StatusText.Text = "Login failed."; return; }
            NavigateByStatus(res.User.Status);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    private async Task SignInWithProviderAsync(string provider)
    {
        try
        {
            GoogleSignInButton.IsEnabled = false;
            AppleSignInButton.IsEnabled = false;
            StatusText.Text = $"Signing in with {provider}...";

            var redirectUri = Windows.Security.Authentication.Web.WebAuthenticationBroker
                .GetCurrentApplicationCallbackUri().OriginalString;

            // Generate PKCE pair (RFC 7636 S256).
            var codeVerifier  = PkceHelper.GenerateCodeVerifier();
            var codeChallenge = PkceHelper.GenerateCodeChallenge(codeVerifier);

            var authorizeUrl = provider switch
            {
                "Google" => BuildGoogleUrl(redirectUri, codeChallenge),
                "Apple"  => BuildAppleUrl(redirectUri, codeChallenge),
                _ => throw new ArgumentException($"Unknown provider: {provider}")
            };

            var result = await Windows.Security.Authentication.Web.WebAuthenticationBroker
                .AuthenticateAsync(
                    Windows.Security.Authentication.Web.WebAuthenticationOptions.None,
                    new Uri(authorizeUrl));

            if (result.ResponseStatus !=
                Windows.Security.Authentication.Web.WebAuthenticationStatus.Success)
            {
                StatusText.Text = "Sign-in cancelled.";
                return;
            }

            var responseData = result.ResponseData;
            if (string.IsNullOrEmpty(responseData))
            {
                StatusText.Text = "No response data received.";
                return;
            }

            // Authorization code flow: the code arrives in the query string, not the fragment.
            var responseUri  = new Uri(responseData);
            var queryString  = responseUri.Query.TrimStart('?');
            var queryParams  = System.Web.HttpUtility.ParseQueryString(queryString);
            var code         = queryParams["code"];

            if (string.IsNullOrEmpty(code))
            {
                StatusText.Text = "No authorization code received.";
                return;
            }

            // Hand the code + verifier to the API for server-side token exchange.
            StatusText.Text = "Exchanging authorization code...";
            var authResponse = await _api.ExchangeCodeAsync(
                provider, code, codeVerifier, redirectUri);

            if (authResponse == null)
            {
                StatusText.Text = "Login failed.";
                return;
            }

            NavigateByStatus(authResponse.User.Status);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            GoogleSignInButton.IsEnabled = true;
            AppleSignInButton.IsEnabled = true;
        }
    }

    // ----------------------------------------------------------------
    // OAuth URL builders
    // ----------------------------------------------------------------

    /// <summary>
    /// Builds the Google PKCE authorization code URL (S256 challenge).
    /// Throws <see cref="InvalidOperationException"/> with setup instructions
    /// if the client ID has not been populated in <see cref="OAuthConfig"/>.
    /// </summary>
    private static string BuildGoogleUrl(string redirectUri, string codeChallenge)
    {
        if (string.IsNullOrWhiteSpace(OAuthConfig.GoogleClientId))
            throw new InvalidOperationException(
                "OAuthConfig.GoogleClientId is empty. " +
                "Follow the setup instructions in " +
                "VirtualLibrary.Client/Services/OAuthConfig.cs " +
                "to obtain a Google OAuth 2.0 client ID and paste it there.");

        // RFC 7636 PKCE — authorization code flow (replaces the deprecated implicit grant).
        return $"https://accounts.google.com/o/oauth2/v2/auth" +
               $"?client_id={Uri.EscapeDataString(OAuthConfig.GoogleClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope=openid%20email%20profile" +
               $"&code_challenge={codeChallenge}" +
               $"&code_challenge_method=S256";
    }

    /// <summary>
    /// Builds the Apple PKCE authorization code URL (S256 challenge).
    /// Throws <see cref="InvalidOperationException"/> with setup instructions
    /// if the client ID has not been populated in <see cref="OAuthConfig"/>.
    /// </summary>
    private static string BuildAppleUrl(string redirectUri, string codeChallenge)
    {
        if (string.IsNullOrWhiteSpace(OAuthConfig.AppleClientId))
            throw new InvalidOperationException(
                "OAuthConfig.AppleClientId is empty. " +
                "Follow the setup instructions in " +
                "VirtualLibrary.Client/Services/OAuthConfig.cs " +
                "to obtain an Apple Services ID and paste it there.");

        // Apple supports PKCE for web flows (code_challenge_method=S256).
        // Note: Apple's redirect_uri must be HTTPS in production.
        return $"https://appleid.apple.com/auth/authorize" +
               $"?client_id={Uri.EscapeDataString(OAuthConfig.AppleClientId)}" +
               $"&redirect_uri={Uri.EscapeDataString(redirectUri)}" +
               $"&response_type=code" +
               $"&scope=name%20email" +
               $"&code_challenge={codeChallenge}" +
               $"&code_challenge_method=S256";
    }

#if DEBUG
    private void OnDevLogin(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(DevLoginPage));
#else
    // Release: collapse the button so it is never visible even if XAML renders it.
    protected override void OnNavigatedTo(Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (DevLoginButton != null)
            DevLoginButton.Visibility = Visibility.Collapsed;
    }
#endif

    private void NavigateByStatus(UserStatus status)
    {
        var frame = this.Frame;
        switch (status)
        {
            case UserStatus.Active:
                frame.Navigate(typeof(LibraryPage));
                break;
            case UserStatus.PendingApproval:
                frame.Navigate(typeof(PendingApprovalPage));
                break;
            default:
                StatusText.Text = $"Account status: {status}. Contact an administrator.";
                break;
        }
    }
}
