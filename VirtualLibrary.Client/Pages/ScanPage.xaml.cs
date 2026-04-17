using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class ScanPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly IIsbnScanner _scanner = ScannerFactory.Create();

    public ScanPage()
    {
        this.InitializeComponent();

        if (!_scanner.IsSupported)
        {
            CameraButton.Content = "Camera scanning (coming soon)";
            CameraButton.IsEnabled = false;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    private async void OnCamera(object sender, RoutedEventArgs e)
    {
        if (!_scanner.IsSupported)
        {
            StatusText.Text = "Camera scanning isn't available on this platform yet.";
            return;
        }

        try
        {
            CameraButton.IsEnabled = false;
            StatusText.Text = "Starting scanner…";
            var raw = await _scanner.ScanIsbnAsync();
            if (string.IsNullOrWhiteSpace(raw))
            {
                StatusText.Text = "Scan cancelled.";
                return;
            }

            // Push into the textbox and run the normal lookup/add flow so the
            // scanned ISBN is validated exactly like a typed one.
            IsbnBox.Text = raw;
            OnSubmit(this, new RoutedEventArgs());
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Scan failed: {ex.Message}";
        }
        finally
        {
            CameraButton.IsEnabled = _scanner.IsSupported;
        }
    }

    /// <summary>
    /// Resolves the right <see cref="IIsbnScanner"/> for the current platform
    /// without taking a dependency on an IoC container. Keeps the page code
    /// platform-agnostic while still producing an Android-specific scanner
    /// when compiled into the Android head.
    /// </summary>
    private static class ScannerFactory
    {
        public static IIsbnScanner Create() =>
#if USE_PLUGIN_SCANNER_UNO
            // Live camera path: ScannerBootstrap lazily wires Plugin.Scanner.Uno
            // (ServiceCollection + Uno-aware CurrentActivity) and returns the
            // cached IBarcodeScanner for the app lifetime.
            new Platforms.Android.AndroidIsbnScanner(
                Platforms.Android.ScannerBootstrap.Resolve());
#elif __ANDROID__
            // Fallback stub — reached only if USE_PLUGIN_SCANNER_UNO is not set
            // (i.e. during development without the package).
            new Platforms.Android.AndroidIsbnScanner();
#else
            new ManualIsbnScanner();
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
