namespace VirtualLibrary.Client.Services;

public interface IBackCoverCamera
{
    bool IsSupported { get; }

    /// <summary>
    /// Launches the camera UI, lets the user capture the back cover with ruler guides,
    /// saves the JPEG locally, and returns the absolute file path.
    /// Returns null when the user cancels or the operation fails.
    /// </summary>
    Task<string?> CaptureBackCoverAsync(string bookId, CancellationToken ct = default);
}

/// <summary>Returned on non-Android platforms until camera support is added there.</summary>
public class NullBackCoverCamera : IBackCoverCamera
{
    public bool IsSupported => false;

    public Task<string?> CaptureBackCoverAsync(string bookId, CancellationToken ct = default)
        => Task.FromResult<string?>(null);
}
