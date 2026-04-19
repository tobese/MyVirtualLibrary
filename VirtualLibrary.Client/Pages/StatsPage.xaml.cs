using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class StatsPage : Page
{
    private readonly ApiClient _api = ApiClient.Instance;

    public StatsPage()
    {
        this.InitializeComponent();
        // Define the StatCard style in code since we can't use App.xaml resources
        // without a full resource merge. A thin helper builds each card instead.
    }

    protected override async void OnNavigatedTo(
        Microsoft.UI.Xaml.Navigation.NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        await LoadAsync();
    }

    private async void OnRefresh(object sender, RoutedEventArgs e) => await LoadAsync();

    private void OnBack(object sender, RoutedEventArgs e)
        => Frame.Navigate(typeof(LibraryPage));

    // ── Load ─────────────────────────────────────────────────────────────────

    private async Task LoadAsync()
    {
        StatusText.Text       = "Loading…";
        StatusText.Visibility = Visibility.Visible;
        ContentPanel.Visibility = Visibility.Collapsed;
        RefreshButton.IsEnabled = false;

        try
        {
            var stats = await _api.GetStatsAsync();
            if (stats == null)
            {
                StatusText.Text = "Could not load statistics.";
                return;
            }

            Populate(stats);
            LastUpdatedText.Text    = $"Last updated {DateTime.Now:HH:mm:ss}";
            ContentPanel.Visibility = Visibility.Visible;
            StatusText.Visibility   = Visibility.Collapsed;
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            RefreshButton.IsEnabled = true;
        }
    }

    // ── Populate ─────────────────────────────────────────────────────────────

    private void Populate(LibraryStatsDto s)
    {
        // Catalogue cards
        FillCard(EditionsCard,    s.TotalEditions, "Editions");
        FillCard(WorksCard,       s.TotalWorks,    "Works");
        FillCard(AuthorsCard,     s.UniqueAuthors, "Authors");
        FillCard(SubjectsCard,    s.UniqueSubjects,"Subjects");

        // Collection cards
        FillCard(UserBooksCard,   s.TotalUserBooks,"Total entries");
        FillCard(OwnedCard,       s.OwnedBooks,    "Owned");
        FillCard(WishlistCard,    s.WishlistBooks, "Wish-list");

        // Reading cards
        FillCard(ReadCard,        s.ReadBooks,     "Read");
        FillCard(UnreadCard,      s.UnreadBooks,   "Want to read");
        FillCard(ReadRecordsCard, s.TotalReadRecords, "Read records");
        FillCard(MembersCard,     s.ActiveMembers, "Active members");

        // Top lists
        PopulateRankedList(AuthorsList,  s.TopAuthors);
        PopulateRankedList(SubjectsList, s.TopSubjects);
    }

    // ── Card builder ─────────────────────────────────────────────────────────

    /// <summary>
    /// Fills a stat-card Border with a large number and a small label.
    /// The Border's Style is set here rather than via XAML resources so we
    /// avoid needing a merged ResourceDictionary.
    /// </summary>
    private static void FillCard(Border card, int value, string label)
    {
        card.Background   = new SolidColorBrush(
            Application.Current.RequestedTheme == ApplicationTheme.Dark
                ? Windows.UI.Color.FromArgb(255, 36, 36, 36)
                : Windows.UI.Color.FromArgb(255, 245, 245, 245));
        card.CornerRadius = new CornerRadius(10);
        card.Padding      = new Thickness(16, 14, 16, 14);
        card.MinWidth     = 100;

        var panel = new StackPanel { Spacing = 4 };
        panel.Children.Add(new TextBlock
        {
            Text       = value.ToString("N0"),
            FontSize   = 32,
            FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        });
        panel.Children.Add(new TextBlock
        {
            Text      = label,
            FontSize  = 12,
            Opacity   = 0.6,
            HorizontalAlignment = HorizontalAlignment.Center,
        });

        card.Child = panel;
    }

    // ── Ranked list builder ───────────────────────────────────────────────────

    private static void PopulateRankedList(ListView list, List<RankedItemDto> items)
    {
        // Build a simple view-model list; template binds via FindName in Loaded.
        list.ItemsSource = items;
        list.Loaded += (_, _) => ColouriseRankedList(list, items);
    }

    private static void ColouriseRankedList(ListView list, List<RankedItemDto> items)
    {
        for (int i = 0; i < items.Count; i++)
        {
            var container = list.ContainerFromIndex(i) as ListViewItem;
            if (container?.ContentTemplateRoot is not Grid grid) continue;

            if (grid.FindName("NameText")  is TextBlock nameTb)  nameTb.Text  = items[i].Name;
            if (grid.FindName("CountText") is TextBlock countTb) countTb.Text = items[i].Count.ToString("N0");
        }
    }
}
