// Android-only. Compiled into the net10.0-android head only.
//
// Lazily creates a minimal IServiceProvider that registers Plugin.Scanner.Uno's
// Uno-aware CurrentActivity implementation and the platform barcode scanner,
// then caches the resolved IBarcodeScanner for the app lifetime.
// This avoids pulling the full Uno.Extensions hosting/navigation stack into an
// app that uses direct Frame.Navigate navigation.

using System;
using Microsoft.Extensions.DependencyInjection;
using Plugin.Scanner.Core.Scanners;
using Plugin.Scanner.Hosting;

namespace VirtualLibrary.Client.Platforms.Android;

internal static class ScannerBootstrap
{
    // Lazy<T> gives free thread-safety; the scanner is resolved at most once.
    private static readonly Lazy<IBarcodeScanner> _lazy = new(Create, isThreadSafe: true);

    /// <summary>
    /// Returns the shared <see cref="IBarcodeScanner"/> instance, creating it on
    /// first call. Must be called after the Uno activity has been started (i.e.
    /// not at static-initialisation time).
    /// </summary>
    public static IBarcodeScanner Resolve() => _lazy.Value;

    private static IBarcodeScanner Create()
    {
        var services = new ServiceCollection();
        // Plugin.Scanner.Uno.Android.CurrentActivity is the ready-made Uno
        // implementation of Plugin.Scanner.Android.ICurrentActivity; it reads the
        // active activity from the Uno runtime so no explicit Activity reference
        // needs to be wired here.
        services.AddCurrentActivity<Plugin.Scanner.Uno.Android.CurrentActivity>();
        services.AddScanner();
        return services.BuildServiceProvider().GetRequiredService<IBarcodeScanner>();
    }
}
