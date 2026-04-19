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

            OwnedCheck.IsChecked      = _book.IsOwned;
            StatusCombo.SelectedIndex = (int)_book.Status; // WantToRead=0, Read=1
            RatingSlider.Value        = _book.Rating ?? 0;
            NotesBox.Text             = _book.Notes ?? "";

            RenderReadRecords(_book.ReadRecords);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to load: {ex.Message}";
        }
    }

    // ── Read history rendering ─────────────────────────────────────────────

    private void RenderReadRecords(List<ReadRecordDto> records)
    {
        ReadsPanel.Children.Clear();

        if (records.Count == 0)
        {
            ReadsPanel.Children.Add(new TextBlock
            {
                Text    = "No readings logged yet.",
                Opacity = 0.5,
            });
            return;
        }

        foreach (var r in records)
        {
            var grid = new Grid { ColumnSpacing = 8 };
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var label = r.DateRead.ToLocalTime().ToString("yyyy-MM-dd");
            if (r.Notes is { Length: > 0 } n) label += $"  –  {n}";

            var text = new TextBlock
            {
                Text              = label,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming      = TextTrimming.CharacterEllipsis,
            };

            var del = new Button { Content = "✕", Tag = r.Id };
            del.Click += OnDeleteRead;
            Grid.SetColumn(del, 1);

            grid.Children.Add(text);
            grid.Children.Add(del);
            ReadsPanel.Children.Add(grid);
        }
    }

    // ── Add a reading ──────────────────────────────────────────────────────

    private async void OnAddRead(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;

        // Simple dialog: just a DatePicker and optional notes.
        var datePicker = new DatePicker { Date = DateTimeOffset.Now };
        var notesBox   = new TextBox   { PlaceholderText = "Optional notes", Margin = new Thickness(0, 8, 0, 0) };

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock { Text = "Date read", FontWeight = Microsoft.UI.Text.FontWeights.SemiBold });
        panel.Children.Add(datePicker);
        panel.Children.Add(notesBox);

        var dialog = new ContentDialog
        {
            Title              = "Log a reading",
            Content            = panel,
            PrimaryButtonText  = "Save",
            CloseButtonText    = "Cancel",
            XamlRoot           = this.XamlRoot,
        };

        if (await dialog.ShowAsync() != ContentDialogResult.Primary) return;

        try
        {
            var dateRead = datePicker.Date.DateTime;
            var notes    = notesBox.Text.Trim() is { Length: > 0 } s ? s : null;
            var record   = await _api.AddReadRecordAsync(_book.Id, dateRead, notes);
            if (record != null)
            {
                _book = _book with { ReadRecords = new List<ReadRecordDto> { record }.Concat(_book.ReadRecords).ToList() };
                RenderReadRecords(_book.ReadRecords);
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to log reading: {ex.Message}";
        }
    }

    // ── Delete a reading ───────────────────────────────────────────────────

    private async void OnDeleteRead(object sender, RoutedEventArgs e)
    {
        if (_book == null || sender is not Button btn || btn.Tag is not Guid recordId) return;

        try
        {
            await _api.DeleteReadRecordAsync(_book.Id, recordId);
            _book = _book with { ReadRecords = _book.ReadRecords.Where(r => r.Id != recordId).ToList() };
            RenderReadRecords(_book.ReadRecords);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Failed to delete reading: {ex.Message}";
        }
    }

    // ── Navigation ─────────────────────────────────────────────────────────

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    // ── Save ───────────────────────────────────────────────────────────────

    private async void OnSave(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;

        var req = new UpdateUserBookRequest(
            Status:  (BookStatus)StatusCombo.SelectedIndex, // 0=WantToRead, 1=Read
            IsOwned: OwnedCheck.IsChecked == true,
            Rating:  (int)RatingSlider.Value,
            Notes:   NotesBox.Text);

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

    // ── Delete ─────────────────────────────────────────────────────────────

    private async void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_book == null) return;

        var dialog = new ContentDialog
        {
            Title             = "Delete this book?",
            Content           = $"Remove \"{_book.Edition.Title}\" from your library? The edition data stays cached.",
            PrimaryButtonText = "Delete",
            CloseButtonText   = "Cancel",
            XamlRoot          = this.XamlRoot,
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
