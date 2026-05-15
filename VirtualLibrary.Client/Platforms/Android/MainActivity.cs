using Android.App;
using Android.Content.PM;
using Android.Views;

namespace VirtualLibrary.Client.Platforms.Android;

// Thin subclass of the Uno-provided ApplicationActivity that adds the
// LAUNCHER intent filter. The base class carries ConfigChanges and
// WindowSoftInputMode; repeating them here keeps the manifest entries
// identical for both classes.
[Activity(
    MainLauncher = true,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize | ConfigChanges.UiMode,
    WindowSoftInputMode = SoftInput.AdjustPan | SoftInput.StateHidden)]
public class MainActivity : Microsoft.UI.Xaml.ApplicationActivity
{
}
