using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class UserManagementPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public UserManagementPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            StatusText.Text = "Loading…";
            var filter = (StatusFilter.SelectedItem as ComboBoxItem)?.Content?.ToString();
            UserStatus? status = filter switch
            {
                "PendingApproval" => UserStatus.PendingApproval,
                "Active"          => UserStatus.Active,
                "Rejected"        => UserStatus.Rejected,
                "Suspended"       => UserStatus.Suspended,
                _                 => null,
            };
            var users = await _api.GetUsersAsync(status);
            UsersList.ItemsSource = users;
            StatusText.Text = users.Count == 0 ? "No users." : $"{users.Count} user(s)";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Load failed: {ex.Message}";
        }
    }

    private async void OnFilterChanged(object sender, SelectionChangedEventArgs e) => await ReloadAsync();
    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    private async void OnApprove(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string userId) return;
        try
        {
            StatusText.Text = "Approving…";
            await _api.ApproveUserAsync(userId, approved: true);
            await ReloadAsync();
        }
        catch (Exception ex) { StatusText.Text = $"Approve failed: {ex.Message}"; }
    }

    private async void OnSuspend(object sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not string userId) return;
        try
        {
            StatusText.Text = "Suspending…";
            await _api.SuspendUserAsync(userId);
            await ReloadAsync();
        }
        catch (Exception ex) { StatusText.Text = $"Suspend failed: {ex.Message}"; }
    }
}
