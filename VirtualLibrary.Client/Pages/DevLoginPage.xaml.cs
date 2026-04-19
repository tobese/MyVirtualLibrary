using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class DevLoginPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public DevLoginPage() => this.InitializeComponent();

    private async void OnPersonaClicked(object sender, RoutedEventArgs e)
    {
#if DEBUG
        var persona = (sender as Button)?.Tag as string;
        if (persona is null) return;

        SetAllButtonsEnabled(false);
        StatusText.Text = $"Signing in as {persona}…";

        try
        {
            var res = await _api.DevLoginAsync(persona);
            if (res == null) { StatusText.Text = "Dev login returned null."; return; }
            NavigateByStatus(res.User.Status);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            SetAllButtonsEnabled(true);
        }
#else
        // Release build — this page should never be reachable, but defend anyway.
        StatusText.Text = "Dev login is not available in this build.";
        await Task.CompletedTask;
#endif
    }

    private void OnBackClicked(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(LoginPage));

    private void NavigateByStatus(UserStatus status)
    {
        switch (status)
        {
            case UserStatus.Active:
                Frame.Navigate(typeof(LibraryPage));
                break;
            case UserStatus.PendingApproval:
                Frame.Navigate(typeof(PendingApprovalPage));
                break;
            default:
                StatusText.Text = $"Logged in but status is '{status}'. Navigate manually.";
                break;
        }
    }

    private void SetAllButtonsEnabled(bool enabled)
    {
        SuperAdminButton.IsEnabled = enabled;
        AdminButton.IsEnabled      = enabled;
        MemberButton.IsEnabled     = enabled;
        PendingButton.IsEnabled    = enabled;
        SuspendedButton.IsEnabled  = enabled;
    }
}
