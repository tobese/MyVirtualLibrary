using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class BookDetailPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;
    private UserBookDto? _book;

    public BookDetailPage()
    {
        this.InitializeComponent();
    }

    protected override async void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is not Guid id) { OnBack(this, new RoutedEventArgs()); return; }

        try
        {
            _book = await _api.GetBookAsync(id);
            if (_book == null) { StatusText.Text = "Book not found."; return; }

            TitleText.Text = _book.Edition.Title;
            AuthorsText.Text = _book.Edition.Authors.Count > 0
                ? "by " + string.Join(", ", _book.Edition.Authors.Select(a => a.Name))
                : "";
            PublisherText.Text = _book.Edition.Publisher is { Length: > 0 } p
                ? $"Publisher: {p}" + (_book.Edition.PublishDate is { Length: > 0 } d ? $" · {d}" : "")
                : "";
            IsbnText.Text = "ISBN: " + (_book.Edition.Isbn13 ?? _book.Edition.Isbn10 ?? "—");
            DimensionsText.Text = _book.Edition.PhysicalDimensions is { Length: > 0 } dim
                ? $"Dimensions: {dim}"
                : "";

            StatusCombo.SelectedIndex = (int)_book.Status;
            RatingSlider.Value = _book.Rating ?? 0;
            NotesBox.Text = _book.Notes ?? "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;

        var req = new UpdateUserBookRequest(
            Status: (BookStatus)StatusCombo.SelectedIndex,
            Rating: (int)RatingSlider.Value,
            Notes:  NotesBox.Text);

        try
        {
            StatusText.Text = "Saving…";
            await _api.UpdateBookAsync(_book.Id, req);
            StatusText.Text = "Saved.";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;

        var dialog = new ContentDialog
        {
            Title = "Delete this book?",
            Content = $"Remove \"{_book.Edition.Title}\" from your library? The edition data stays cached.",
            PrimaryButtonText = "Delete",
            CloseButtonText = "Cancel",
            XamlRoot = this.XamlRoot
        };
        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            await _api.DeleteBookAsync(_book.Id);
            OnBack(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Delete failed: {ex.Message}";
        }
    }
}
