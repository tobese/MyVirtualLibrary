using Android.Content;
using VirtualLibrary.Client.Services;

namespace VirtualLibrary.Client.Platforms.Android;

public class AndroidBackCoverCamera : IBackCoverCamera
{
    public bool IsSupported => true;

    public async Task<string?> CaptureBackCoverAsync(
        string bookId, CancellationToken ct = default)
    {
        var tcs = new TaskCompletionSource<string?>(
            TaskCreationOptions.RunContinuationsAsynchronously);

        CameraWithRulersActivity.PendingCapture = tcs;

        var context = Uno.UI.ContextHelper.Current;
        var intent  = new Intent(context, typeof(CameraWithRulersActivity));
        intent.PutExtra("bookId", bookId);
        ((global::Android.App.Activity)context).StartActivity(intent);

        using (ct.Register(() =>
        {
            CameraWithRulersActivity.PendingCapture?.TrySetCanceled();
            CameraWithRulersActivity.PendingCapture = null;
        }))
        {
            return await tcs.Task.ConfigureAwait(false);
        }
    }
}
