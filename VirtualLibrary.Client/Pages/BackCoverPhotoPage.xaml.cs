using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media.Imaging;
using Microsoft.UI.Xaml.Navigation;
using VirtualLibrary.Client.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Client.Pages;

public sealed partial class BackCoverPhotoPage : Page
{
    private readonly IBackCoverCamera _camera = BackCoverCameraFactory.Create();
    private Guid?   _bookId;

    public BackCoverPhotoPage()
    {
        this.InitializeComponent();

        if (!_camera.IsSupported)
        {
            CaptureButton.Content   = "Camera not supported on this platform";
            CaptureButton.IsEnabled = false;
        }
    }

    protected override void OnNavigatedTo(NavigationEventArgs e)
    {
        base.OnNavigatedTo(e);
        if (e.Parameter is UserBookDto book)
        {
            _bookId             = book.Id;
            BookTitleText.Text  = book.Edition.Title;
        }
    }

    private async void OnCapture(object sender, RoutedEventArgs e)
    {
        if (!_camera.IsSupported) return;

        CaptureButton.IsEnabled     = false;
        StatusText.Text             = "Opening camera…";
        PreviewPanel.Visibility     = Visibility.Collapsed;

        try
        {
            var filePath = await _camera.CaptureBackCoverAsync(
                _bookId?.ToString() ?? "unknown");

            if (filePath == null)
            {
                StatusText.Text = "Capture cancelled.";
                return;
            }

            StatusText.Text  = "Saved.";
            FilePathText.Text = filePath;
            PreviewPanel.Visibility = Visibility.Visible;

            try
            {
                PreviewImage.Source = new BitmapImage(new Uri("file://" + filePath));
            }
            catch
            {
                // Preview is best-effort; the file is still saved.
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = $"Error: {ex.Message}";
        }
        finally
        {
            CaptureButton.IsEnabled = _camera.IsSupported;
        }
    }

    private void OnBack(object sender, RoutedEventArgs e)
    {
        if (this.Frame.CanGoBack) this.Frame.GoBack();
        else this.Frame.Navigate(typeof(LibraryPage));
    }

    private static class BackCoverCameraFactory
    {
        public static IBackCoverCamera Create() =>
#if __ANDROID__
            new Platforms.Android.AndroidBackCoverCamera();
#else
            new NullBackCoverCamera();
#endif
    }
}
