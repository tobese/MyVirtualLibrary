using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;
using Windows.Storage;
using Windows.Storage.Pickers;

namespace VirtualLibrary.Client.Pages;

public sealed partial class ImportPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    /// <summary>ISBNs parsed from the chosen file, ready to send.</summary>
    private List<string> _isbns = new();

    public ImportPage() => this.InitializeComponent();

    // ── File picker ──────────────────────────────────────────────────────────

    private async void OnPickFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var picker = new FileOpenPicker();
            picker.FileTypeFilter.Add(".txt");
            picker.FileTypeFilter.Add(".csv");
            picker.FileTypeFilter.Add("*");

#if __WASM__
            // Uno WASM requires initialising the picker with the window handle.
            WinRT.Interop.InitializeWithWindow.Initialize(
                picker,
                Windows.UI.Core.CoreWindow.GetForCurrentThread()?.GetType() == null
                    ? IntPtr.Zero
                    : (IntPtr)0);
#endif

            var file = await picker.PickSingleFileAsync();
            if (file == null) return;

            var text = await FileIO.ReadTextAsync(file);
            _isbns = ParseIsbns(text);

            FileNameText.Text = $"{file.Name}  ({_isbns.Count} ISBN{(_isbns.Count == 1 ? "" : "s")} found)";
            ImportButton.IsEnabled = _isbns.Count > 0;

            if (_isbns.Count == 0)
                StatusText.Text = "No ISBNs found in the file. Use one ISBN per line.";
            else
                StatusText.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not read file: {ex.Message}";
        }
    }

    // ── Import ───────────────────────────────────────────────────────────────

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        if (_isbns.Count == 0) return;

        SetBusy(true);
        StatusText.Text     = $"Importing {_isbns.Count} ISBN(s)… this may take a while.";
        SummaryBorder.Visibility = Visibility.Collapsed;
        ResultsList.ItemsSource  = null;

        try
        {
            var status    = StatusCombo.SelectedIndex == 1 ? BookStatus.Read : BookStatus.WantToRead;
            var isOwned   = OwnedCheck.IsChecked == true;

            var response = await _api.ImportBooksAsync(_isbns, status, isOwned);
            if (response == null)
            {
                StatusText.Text = "Import failed: no response from server.";
                return;
            }

            StatusText.Text = "";
            ShowSummary(response.Summary);
            ShowResults(response.Results);
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Import error: {ex.Message}";
        }
        finally
        {
            SetBusy(false);
        }
    }

    // ── Results display ──────────────────────────────────────────────────────

    private void ShowSummary(ImportSummary s)
    {
        SummaryText.Text =
            $"Total: {s.Total}  ·  Added: {s.Added}  ·  Already in library: {s.AlreadyInLibrary}  " +
            $"·  Not found: {s.NotFound}  ·  Errors: {s.Errors}" +
            (s.DataRefreshed > 0 ? $"  ·  Metadata refreshed: {s.DataRefreshed}" : "");
        SummaryBorder.Visibility = Visibility.Visible;
    }

    private void ShowResults(List<ImportRowResult> results)
    {
        // Build display rows as anonymous-style view models so the template can bind.
        var rows = results.Select(r => new ImportRow(r)).ToList();
        ResultsList.ItemsSource = rows;

        // Manually set text/colour on each container after the list renders.
        // (Data-binding to colours/converters would require extra plumbing; this
        //  keeps the XAML simple for now.)
        ResultsList.Loaded += (_, _) => ColouriseRows(results);
    }

    private void ColouriseRows(List<ImportRowResult> results)
    {
        for (int i = 0; i < results.Count; i++)
        {
            var container = ResultsList.ContainerFromIndex(i) as ListViewItem;
            if (container == null) continue;

            var grid = container.ContentTemplateRoot as Grid;
            if (grid == null) continue;

            var r = results[i];

            if (grid.FindName("IsbnText")     is TextBlock isbnTb)   isbnTb.Text   = r.Isbn;
            if (grid.FindName("TitleText")    is TextBlock titleTb)  titleTb.Text  = r.Title ?? r.Message ?? "";
            if (grid.FindName("StatusLabel")  is TextBlock statusTb)
            {
                statusTb.Text       = r.Status.ToString();
                statusTb.Foreground = StatusColour(r.Status);
            }
            if (grid.FindName("RefreshedText") is TextBlock refreshTb)
                refreshTb.Visibility = r.DataRefreshed ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private static SolidColorBrush StatusColour(ImportRowStatus status) => status switch
    {
        ImportRowStatus.Added            => new SolidColorBrush(Colors.Green),
        ImportRowStatus.AlreadyInLibrary => new SolidColorBrush(Colors.Gray),
        ImportRowStatus.NotFound         => new SolidColorBrush(Colors.Orange),
        ImportRowStatus.Error            => new SolidColorBrush(Colors.Red),
        _                                => new SolidColorBrush(Colors.Gray),
    };

    // ── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Parses a plain-text file: one ISBN per line.
    /// Strips blank lines, comment lines (#), and any non-alphanumeric characters
    /// (so "978-0-13-110362-7" and "9780131103627" both work).
    /// </summary>
    private static List<string> ParseIsbns(string text) =>
        text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.Length > 0 && !l.StartsWith('#'))
            .Select(l => new string(l.Where(char.IsLetterOrDigit).ToArray()))
            .Where(s => s.Length is 10 or 13)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private void SetBusy(bool busy)
    {
        PickFileButton.IsEnabled = !busy;
        ImportButton.IsEnabled   = !busy && _isbns.Count > 0;
        StatusCombo.IsEnabled    = !busy;
        OwnedCheck.IsEnabled     = !busy;
    }

    private void OnBack(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(LibraryPage));

    // Lightweight wrapper so the ListView has a typed ItemsSource.
    private record ImportRow(ImportRowResult Result);
}
