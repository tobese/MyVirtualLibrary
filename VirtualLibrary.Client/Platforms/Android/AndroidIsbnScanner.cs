// Android-only IIsbnScanner. Compiled into the net10.0-android head.
//
// Status: this file is a *scaffold* for integrating Plugin.Scanner.Uno (0.0.1).
// The package's transitive dependencies require Uno.WinUI 6.5.64, which is
// newer than the Uno.WinUI pulled in by Uno.Sdk 6.5.31 (pinned in global.json).
// Until Uno.Sdk ships a matching stable (or Plugin.Scanner.Uno relaxes its
// pins), this implementation falls through to manual entry — identical to
// the desktop default — but the recipe to switch it on is inlined below in
// a single #if block so the change is a one-file, one-PackageReference swap.
//
// Tracking: GitHub issue #2 ("Phase 2: Android scanner …").

using VirtualLibrary.Client.Services;

namespace VirtualLibrary.Client.Platforms.Android;

/// <summary>
/// Android implementation of <see cref="IIsbnScanner"/>. Camera-based
/// scanning is wired but gated behind <c>USE_PLUGIN_SCANNER_UNO</c> until
/// the dependency alignment above is resolved.
/// </summary>
public sealed class AndroidIsbnScanner : IIsbnScanner
{
#if USE_PLUGIN_SCANNER_UNO
    private readonly Plugin.Scanner.Core.Barcode.IBarcodeScanner _barcodes;

    public AndroidIsbnScanner(Plugin.Scanner.Core.Barcode.IBarcodeScanner barcodes)
    {
        _barcodes = barcodes;
    }

    public bool IsSupported => true;

    public async Task<string?> ScanIsbnAsync(CancellationToken ct = default)
    {
        // EAN-13 covers all ISBN-13 barcodes; EAN-8 is included for safety.
        var options = new Plugin.Scanner.Core.Barcode.BarcodeScanOptions
        {
            Formats = Plugin.Scanner.Core.Barcode.BarcodeFormat.Ean13
                    | Plugin.Scanner.Core.Barcode.BarcodeFormat.Ean8,
        };

        try
        {
            var barcode = await _barcodes.ScanAsync(options, ct).ConfigureAwait(true);
            return barcode?.RawValue;
        }
        catch (Plugin.Scanner.Core.Exceptions.ScanException)
        {
            return null; // User cancelled or camera unavailable; degrade gracefully.
        }
    }
#else
    // Placeholder until Plugin.Scanner.Uno's Uno.WinUI pin matches our SDK.
    // Keeps the UX honest: camera button stays disabled, manual entry works.
    public bool IsSupported => false;

    public Task<string?> ScanIsbnAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
#endif
}
