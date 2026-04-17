using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;
using Windows.UI;

namespace VirtualLibrary.Client.Pages;

/// <summary>
/// View-model wrapper for a single book spine on the shelf.
/// Exposes computed <see cref="SpineWidth"/> (px) and
/// <see cref="SpineColor"/> (brush) so the DataTemplate can bind
/// without code-behind helpers.
/// </summary>
public sealed class SpineItem
{
    public UserBookDto Book       { get; }
    public double      SpineWidth { get; }
    public Brush       SpineColor { get; }

    public SpineItem(UserBookDto book)
    {
        Book       = book;
        SpineWidth = ComputeSpineWidth(book.Edition.PhysicalDimensions);
        SpineColor = ComputeColor(book.Id);
    }

    // -------------------------------------------------------------------
    // PhysicalDimensions is a free-form string from OpenLibrary, e.g.
    //   "21 x 14 x 1.2 cm"  or  "8.3 x 5.5 x 0.7 inches"
    // We parse the THIRD number (spine/depth) if present. Values are
    // converted to pixels at ~96 dpi (1 cm ≈ 38 px, 1 in ≈ 96 px),
    // then clamped to a readable [16, 60] px range.
    // Falls back to 26 px when parsing fails.
    // -------------------------------------------------------------------
    private static double ComputeSpineWidth(string? dims)
    {
        if (string.IsNullOrWhiteSpace(dims)) return 26;

        var numbers = Regex.Matches(dims, @"[\d]+(?:[.,][\d]+)?");
        if (numbers.Count < 3) return 26;

        if (!double.TryParse(
                numbers[2].Value.Replace(',', '.'),
                System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture,
                out double depth))
            return 26;

        double px = dims.Contains("inch", StringComparison.OrdinalIgnoreCase)
            ? depth * 96
            : depth * 38;

        return Math.Clamp(px, 16, 60);
    }

    // Deterministic hue derived from the book's Guid so each book always
    // gets the same color regardless of load order.
    private static Brush ComputeColor(Guid id)
    {
        var bytes = id.ToByteArray();
        // Use first 4 bytes to spread hue, pick mid-range S and L.
        double hue = ((bytes[0] << 8 | bytes[1]) % 360) / 360.0;
        double sat = 0.45 + (bytes[2] % 30) / 100.0;
        double lum = 0.25 + (bytes[3] % 20) / 100.0;
        var (r, g, b) = HslToRgb(hue, sat, lum);
        return new SolidColorBrush(Color.FromArgb(210, r, g, b));
    }

    private static (byte r, byte g, byte b) HslToRgb(double h, double s, double l)
    {
        double c = (1 - Math.Abs(2 * l - 1)) * s;
        double x = c * (1 - Math.Abs(h * 6 % 2 - 1));
        double m = l - c / 2;
        double r1, g1, b1;
        int sect = (int)(h * 6);
        (r1, g1, b1) = sect switch
        {
            0 => (c, x, 0d), 1 => (x, c, 0d),
            2 => (0d, c, x), 3 => (0d, x, c),
            4 => (x, 0d, c), _ => (c, 0d, x),
        };
        return ((byte)((r1 + m) * 255), (byte)((g1 + m) * 255), (byte)((b1 + m) * 255));
    }
}

public sealed partial class ShelfPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;
    private readonly ObservableCollection<SpineItem> _spines = new();
    private Guid _shelfId;

    public ShelfPage()
    {
        this.InitializeComponent();
        SpinesList.ItemsSource = _spines;
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
            var shelf = await _api.GetDefaultShelfAsync();
            if (shelf == null)
            {
                StatusText.Text = "Could not load shelf.";
                return;
            }

            _shelfId = shelf.Id;
            _spines.Clear();
            foreach (var p in shelf.Placements)
                _spines.Add(new SpineItem(p.Book));

            StatusText.Text = _spines.Count == 0
                ? "No owned books yet — add some from the library."
                : $"{_spines.Count} book(s) — drag to reorder";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Could not load shelf: {ex.Message}";
        }
    }

    /// <summary>
    /// Fires after the user completes a drag-reorder gesture.
    /// The ListView has already updated <see cref="_spines"/> in-place,
    /// so we just persist the new order.
    /// </summary>
    private async void OnDragCompleted(
        ListViewBase sender,
        DragItemsCompletedEventArgs args)
    {
        try
        {
            StatusText.Text = "Saving…";
            await _api.SaveShelfAsync(
                _shelfId,
                _spines.Select(s => s.Book.Id));
            StatusText.Text = $"{_spines.Count} book(s) — drag to reorder";
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Save failed: {ex.Message}";
        }
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await ReloadAsync();

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }
}
