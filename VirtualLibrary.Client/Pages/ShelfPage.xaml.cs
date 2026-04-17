using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class ShelfPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public ShelfPage()
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
            var books = await _api.GetBooksAsync(BookStatus.Owned);
            SpinesList.ItemsSource = books;
            StatusText.Text = books.Count == 0
                ? "No owned books yet — add some from the library."
                : $"{books.Count} book(s) on the shelf";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load shelf: {ex.Message}";
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }
}
