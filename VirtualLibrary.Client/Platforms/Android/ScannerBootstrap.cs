// Android-only. Compiled into the net10.0-android head only.
//
// Lazily creates a minimal IServiceProvider that registers Plugin.Scanner.Uno's
// Uno-aware CurrentActivity implementation and the platform barcode scanner,
// then caches the resolved IBarcodeScanner for the app lifetime.
// This avoids pulling the full Uno.Extensions hosting/navigation stack into an
// app that uses direct Frame.Navigate navigation.

using Microsoft.Extensions.DependencyInjection;
using Plugin.Scanner.Core.Scanners;
using Plugin.Scanner.Hosting;

namespace VirtualLibrary.Client.Platforms.Android;

internal static class ScannerBootstrap
{
    private static readonly Lazy<IBarcodeScanner> _lazy = new(Create, isThreadSafe: true);

    public static IBarcodeScanner Resolve() => _lazy.Value;

    private static IBarcodeScanner Create()
    {
        var services = new ServiceCollection();
        // Register a thin ICurrentActivity wrapper that reads the active Activity
        // from Uno's ContextHelper. Plugin.Scanner.Uno.Android.CurrentActivity is
        // internal, so we provide our own equivalent rather than referencing it.
        services.AddCurrentActivity<UnoCurrentActivity>();
        services.AddScanner();
        return services.BuildServiceProvider().GetRequiredService<IBarcodeScanner>();
    }

    /// <summary>
    /// Implements <see cref="Plugin.Scanner.Android.ICurrentActivity"/> by delegating
    /// to <see cref="Uno.UI.ContextHelper.Current"/>, which always returns the
    /// foreground Uno activity on Android.
    /// </summary>
    private sealed class UnoCurrentActivity : Plugin.Scanner.Android.ICurrentActivity
    {
        public global::Android.App.Activity Activity =>
            (global::Android.App.Activity)Uno.UI.ContextHelper.Current;
    }
}
