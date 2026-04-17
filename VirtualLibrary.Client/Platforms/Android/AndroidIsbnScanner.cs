// Android-only IIsbnScanner. Compiled into the net10.0-android head.
//
// Plugin.Scanner.Uno 0.0.1 requires Uno.WinUI >= 6.5.64. Uno.Sdk 6.5.31
// (pinned in global.json) ships Uno.WinUI 6.5.153, so the constraint is
// satisfied. The USE_PLUGIN_SCANNER_UNO flag is now defined unconditionally
// for the Android TFM (see VirtualLibrary.Client.csproj) and the live
// camera path below is the active one.
//
// IBarcodeScanner is resolved via ScannerBootstrap (ServiceCollection +
// Plugin.Scanner.Uno.Android.CurrentActivity) and injected by ScannerFactory
// in ScanPage.xaml.cs.
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
    private readonly Plugin.Scanner.Core.Scanners.IBarcodeScanner _barcodes;

    public AndroidIsbnScanner(Plugin.Scanner.Core.Scanners.IBarcodeScanner barcodes)
    {
        _barcodes = barcodes;
    }

    public bool IsSupported => true;

    public async Task<string?> ScanIsbnAsync(CancellationToken ct = default)
    {
        // EAN-13 covers all ISBN-13 barcodes; EAN-8 is included for safety.
        var options = new Plugin.Scanner.Options.BarcodeScanOptions
        {
            Formats = Plugin.Scanner.Core.Models.Enums.BarcodeFormat.Ean13
                    | Plugin.Scanner.Core.Models.Enums.BarcodeFormat.Ean8,
        };

        try
        {
            var barcode = await _barcodes.ScanAsync(options, ct).ConfigureAwait(true);
            return barcode?.Text;
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
