using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class PendingApprovalPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;
    private CancellationTokenSource? _pollCts;

    public PendingApprovalPage()
    {
        this.InitializeComponent();
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        _pollCts = new CancellationTokenSource();
        _ = PollAsync(_pollCts.Token);
    }

    protected override void OnNavigatedFrom(NavigationEventArgs e)
    {
        base.OnNavigatedFrom(e);
        _pollCts?.Cancel();
        _pollCts = null;
    }

    private async Task PollAsync(CancellationToken ct)
    {
        // Poll every 10s. The refresh endpoint re-issues a token with the latest
        // status claim so we pick up approvals without re-login.
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var refreshed = await _api.RefreshAsync();
                if (refreshed != null && refreshed.User.Status == UserStatus.Active)
                {
                    StatusText.Text = "Approved! Loading your library…";
                    this.Frame.Navigate(typeof(LibraryPage));
                    return;
                }

                StatusText.Text = refreshed?.User.Status switch
                {
                    UserStatus.Rejected  => "Your account request was rejected.",
                    UserStatus.Suspended => "Your account has been suspended.",
                    _                    => $"Status: PendingApproval (last checked {DateTime.Now:T})"
                };

                if (refreshed?.User.Status is UserStatus.Rejected or UserStatus.Suspended)
                {
                    ProgressRing.IsActive = false;
                    return;
                }
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Couldn't reach the server: {ex.Message}";
            }

            try { await Task.Delay(TimeSpan.FromSeconds(10), ct); }
            catch (TaskCanceledException) { return; }
        }
    }

    private async void OnCheckNow(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        await Task.Yield();
        _ = PollAsync(_pollCts.Token);
    }

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _pollCts?.Cancel();
        _api.Logout();
        this.Frame.Navigate(typeof(LoginPage));
    }
}
