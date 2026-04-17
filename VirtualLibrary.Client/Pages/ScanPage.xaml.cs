using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class ScanPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public ScanPage()
    {
        this.InitializeComponent();

#if __WASM__
        // No native camera scanning on WASM in v1; fall back to manual entry.
        CameraButton.Content = "Camera scanning (coming soon)";
        CameraButton.IsEnabled = false;
#endif
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    private async void OnCamera(object sender, RoutedEventArgs e)
    {
#if __ANDROID__
        try
        {
            StatusText.Text = "Starting scanner…";
            // Placeholder for future Plugin.Scanner.Uno integration:
            //
            //   var options = new BarcodeScannerOptions(BarcodeFormat.Ean13 | BarcodeFormat.Ean8);
            //   var result  = await _barcodeScanner.ScanAsync(options);
            //   IsbnBox.Text = result?.DisplayValue ?? "";
            //   OnSubmit(this, new RoutedEventArgs());
            StatusText.Text = "Scanner plugin not wired yet — enter the ISBN manually for now.";
            await Task.CompletedTask;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan failed: {ex.Message}";
        }
#else
        StatusText.Text = "Camera scanning isn't available on this platform yet.";
        await Task.CompletedTask;
#endif
    }

    private async void OnSubmit(object sender, RoutedEventArgs e)
    {
        var raw  = (IsbnBox.Text ?? "").Trim();
        var isbn = new string(raw.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();

        if (!IsValidIsbn(isbn))
        {
            StatusText.Text = "That doesn't look like a valid ISBN-10 or ISBN-13.";
            PreviewPanel.Visibility = Visibility.Collapsed;
            return;
        }

        try
        {
            StatusText.Text = "Looking up…";
            PreviewPanel.Visibility = Visibility.Collapsed;

            var lookup = await _api.LookupIsbnAsync(isbn);
            if (lookup == null)
            {
                StatusText.Text = "Not found on OpenLibrary.";
                return;
            }

            PreviewTitle.Text      = lookup.Edition.Title;
            PreviewAuthors.Text    = lookup.Edition.Authors.Count > 0
                ? "by " + string.Join(", ", lookup.Edition.Authors.Select(a => a.Name))
                : "";
            PreviewPublisher.Text  = lookup.Edition.Publisher is { Length: > 0 } p
                ? p + (lookup.Edition.PublishDate is { Length: > 0 } d ? $" · {d}" : "")
                : "";
            PreviewPanel.Visibility = Visibility.Visible;

            var status = (StatusCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
            {
                "WantToRead" => BookStatus.WantToRead,
                "Read"       => BookStatus.Read,
                _            => BookStatus.Owned,
            };

            StatusText.Text = "Adding to your library…";
            var added = await _api.AddBookAsync(isbn, status);
            if (added == null)
            {
                StatusText.Text = "Add failed.";
                return;
            }

            StatusText.Text = "Added. ✓";
            IsbnBox.Text = "";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
    }

    /// <summary>ISBN-10 / ISBN-13 format + checksum validation.</summary>
    private static bool IsValidIsbn(string s)
    {
        if (string.IsNullOrEmpty(s)) return false;
        return s.Length switch
        {
            10 => IsValidIsbn10(s),
            13 => IsValidIsbn13(s),
            _  => false,
        };
    }

    private static bool IsValidIsbn10(string s)
    {
        int sum = 0;
        for (int i = 0; i < 9; i++)
        {
            if (!char.IsDigit(s[i])) return false;
            sum += (s[i] - '0') * (10 - i);
        }
        int check = s[9] == 'X' ? 10 : s[9] - '0';
        if (check < 0 || check > 10) return false;
        sum += check;
        return sum % 11 == 0;
    }

    private static bool IsValidIsbn13(string s)
    {
        int sum = 0;
        for (int i = 0; i < 13; i++)
        {
            if (!char.IsDigit(s[i])) return false;
            int d = s[i] - '0';
            sum += (i % 2 == 0) ? d : d * 3;
        }
        return sum % 10 == 0;
    }
}
