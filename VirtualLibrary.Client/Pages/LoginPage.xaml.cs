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

            var authorizeUrl = provider switch
            {
                "Google" => $"https://accounts.google.com/o/oauth2/v2/auth?client_id=YOUR_GOOGLE_CLIENT_ID&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=id_token&scope=openid%20email%20profile&nonce={Guid.NewGuid()}",
                "Apple" => $"https://appleid.apple.com/auth/authorize?client_id=YOUR_APPLE_SERVICE_ID&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=id_token&scope=name%20email&nonce={Guid.NewGuid()}",
                _ => throw new ArgumentException($"Unknown provider: {provider}")
            };

            var result = await Windows.Security.Authentication.Web.WebAuthenticationBroker
                .AuthenticateAsync(
                    Windows.Security.Authentication.Web.WebAuthenticationOptions.None,
                    new Uri(authorizeUrl));

            if (result.ResponseStatus != Windows.Security.Authentication.Web.WebAuthenticationStatus.Success)
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
            var fragment = new Uri(responseData).Fragment.TrimStart('#');
            var queryParams = System.Web.HttpUtility.ParseQueryString(fragment);
            var idToken = queryParams["id_token"];

            if (string.IsNullOrEmpty(idToken))
            {
                StatusText.Text = "No identity token received.";
                return;
            }

            var authResponse = await _api.ExternalLoginAsync(provider, idToken);
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
