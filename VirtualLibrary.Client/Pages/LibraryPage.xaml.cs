using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;
using Windows.System;

namespace VirtualLibrary.Client.Pages;

public sealed partial class LibraryPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public LibraryPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);

        // Show admin-only buttons for Admin / SuperAdmin.
        var role = _api.CurrentUser?.Role;
        var isAdmin = role == UserRole.Admin || role == UserRole.SuperAdmin;
        AdminButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;
        StatsButton.Visibility = isAdmin ? Visibility.Visible : Visibility.Collapsed;

        await ReloadAsync();
    }

    private async Task ReloadAsync()
    {
        try
        {
            StatusText.Text = "Loading…";
            var books = await _api.GetBooksAsync();
            BooksList.ItemsSource = books;
            StatusText.Text = books.Count == 0
                ? "No books yet — enter an ISBN above to add one."
                : "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load library: {ex.Message}";
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();

    private async void OnIsbnKeyDown(object sender, KeyRoutedEventArgs e)
    {
        if (e.Key == VirtualKey.Enter)
        {
            e.Handled = true;
            await AddBookAsync();
        }
    }

    private async void OnAddBook(object sender, RoutedEventArgs e) => await AddBookAsync();

    private async Task AddBookAsync()
    {
        var isbn = (IsbnBox.Text ?? "").Trim();
        if (string.IsNullOrEmpty(isbn))
        {
            StatusText.Text = "Enter an ISBN first.";
            return;
        }

        var status = StatusCombo.SelectedIndex == 1 ? BookStatus.Read : BookStatus.WantToRead;
        var isOwned = OwnedCheck.IsChecked == true;

        try
        {
            StatusText.Text = $"Adding {isbn}…";
            var added = await _api.AddBookAsync(isbn, status, isOwned);
            if (added == null)
            {
                StatusText.Text = "ISBN not found.";
                return;
            }

            IsbnBox.Text = "";
            await ReloadAsync();
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Add failed: {ex.Message}";
        }
    }

    private void OnBookClicked(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is UserBookDto book)
            this.Frame.Navigate(typeof(BookDetailPage), book.Id);
    }

    private void OnOpenUserManagement(object sender, RoutedEventArgs e)
        => this.Frame.Navigate(typeof(UserManagementPage));

    private void OnOpenScan(object sender, RoutedEventArgs e)
        => this.Frame.Navigate(typeof(ScanPage));

    private void OnOpenShelf(object sender, RoutedEventArgs e)
        => this.Frame.Navigate(typeof(ShelfPage));

    private void OnOpenImport(object sender, RoutedEventArgs e)
        => this.Frame.Navigate(typeof(ImportPage));

    private void OnOpenStats(object sender, RoutedEventArgs e)
        => this.Frame.Navigate(typeof(StatsPage));

    private void OnSignOut(object sender, RoutedEventArgs e)
    {
        _api.Logout();
        this.Frame.Navigate(typeof(LoginPage));
    }
}
