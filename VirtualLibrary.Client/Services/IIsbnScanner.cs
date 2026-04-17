namespace VirtualLibrary.Client.Services;

/// <summary>
/// Abstraction over a device-local ISBN barcode scanner. Decoupled from any
/// particular native library so the ScanPage can focus on UX and the scanner
/// backend can evolve independently.
///
/// <para>
/// On platforms without a camera scanner (e.g. WebAssembly today), the default
/// implementation simply returns <see langword="null"/> to signal "no camera
/// scan available — fall back to manual entry".
/// </para>
/// </summary>
public interface IIsbnScanner
{
    /// <summary>
    /// Returns <c>true</c> when the current platform/runtime supports opening
    /// a camera-based scanner UI. The ScanPage uses this to enable/disable
    /// the "Open camera" button.
    /// </summary>
    bool IsSupported { get; }

    /// <summary>
    /// Starts the platform's barcode scanner UI and returns the scanned ISBN
    /// as an unnormalised string (digits only, possibly with an 'X' check
    /// digit for ISBN-10). Returns <see langword="null"/> if the user
    /// cancels or no scanner is available.
    /// </summary>
    Task<string?> ScanIsbnAsync(CancellationToken ct = default);
}

/// <summary>
/// No-op scanner used on platforms where camera barcode scanning is not (yet)
/// wired up. Always returns <see langword="null"/>; ScanPage degrades to
/// manual ISBN entry with checksum validation.
/// </summary>
public sealed class ManualIsbnScanner : IIsbnScanner
{
    public bool IsSupported => false;

    public Task<string?> ScanIsbnAsync(CancellationToken ct = default) =>
        Task.FromResult<string?>(null);
}
