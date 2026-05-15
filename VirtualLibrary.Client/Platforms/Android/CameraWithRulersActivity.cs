using Android.App;
using Android.Content.PM;
using Android.Graphics;
using Android.Hardware.Camera2;
using Android.OS;
using Android.Views;
using Android.Widget;
using Java.Lang;
using AImage     = Android.Media.Image;
using ImageReader = Android.Media.ImageReader;

namespace VirtualLibrary.Client.Platforms.Android;

/// <summary>
/// Full-screen camera activity that overlays centimetre rulers on the
/// viewfinder so you can photograph the back of a book with scale
/// reference — useful for developing the back-cover crop algorithm.
///
/// Saves a JPEG to the app's external Pictures directory (no storage
/// permission needed on API 29+), then completes <see cref="PendingCapture"/>
/// with the absolute file path.
/// </summary>
[Activity(
    Theme = "@android:style/Theme.DeviceDefault.NoActionBar",
    ScreenOrientation = ScreenOrientation.Portrait,
    ConfigurationChanges = ConfigChanges.Orientation | ConfigChanges.ScreenSize)]
public class CameraWithRulersActivity : Activity, TextureView.ISurfaceTextureListener
{
    private const int RequestCameraPermission = 1001;

    /// <summary>
    /// Set by <see cref="AndroidBackCoverCamera"/> before starting this
    /// activity. Completed with the saved file path (or null on cancel/error).
    /// </summary>
    internal static TaskCompletionSource<string?>? PendingCapture;

    private TextureView?        _textureView;
    private CameraDevice?       _cameraDevice;
    private CameraCaptureSession? _session;
    private CaptureRequest.Builder? _previewRequest;
    private ImageReader?        _imageReader;
    private HandlerThread?      _bgThread;
    private Handler?            _bgHandler;
    private string?             _bookId;
    private bool                _capturing;

    // ── Lifecycle ────────────────────────────────────────────────────────────

    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        _bookId = Intent?.GetStringExtra("bookId");

        var frame = new FrameLayout(this);

        _textureView = new TextureView(this);
        _textureView.SurfaceTextureListener = this;
        frame.AddView(_textureView, MatchParent());

        var ruler = new RulerOverlayView(this);
        ruler.Clickable = false;
        frame.AddView(ruler, MatchParent());

        var captureBtn = new global::Android.Widget.Button(this) { Text = "Capture" };
        captureBtn.Click += (_, _) => OnCaptureClicked();
        var btnLp = new FrameLayout.LayoutParams(
            ViewGroup.LayoutParams.WrapContent,
            ViewGroup.LayoutParams.WrapContent)
        {
            Gravity      = GravityFlags.Bottom | GravityFlags.CenterHorizontal,
            BottomMargin = Dp(40),
        };
        frame.AddView(captureBtn, btnLp);

        SetContentView(frame);
    }

    protected override void OnResume()
    {
        base.OnResume();
        StartBackgroundThread();
        if (_textureView?.IsAvailable == true)
            OpenCamera();
    }

    protected override void OnPause()
    {
        StopCamera();
        StopBackgroundThread();
        base.OnPause();
    }

    protected override void OnDestroy()
    {
        if (IsFinishing)
        {
            // User pressed Back without capturing — treat as cancel.
            PendingCapture?.TrySetResult(null);
            PendingCapture = null;
        }
        base.OnDestroy();
    }

    // ── ISurfaceTextureListener ──────────────────────────────────────────────

    public void OnSurfaceTextureAvailable(SurfaceTexture surface, int width, int height)
        => OpenCamera();

    public bool OnSurfaceTextureDestroyed(SurfaceTexture surface)
    {
        StopCamera();
        return true;
    }

    public void OnSurfaceTextureSizeChanged(SurfaceTexture surface, int w, int h) { }
    public void OnSurfaceTextureUpdated(SurfaceTexture surface)                   { }

    // ── Camera setup ─────────────────────────────────────────────────────────

    private void OpenCamera()
    {
        if (CheckSelfPermission(global::Android.Manifest.Permission.Camera)
            != Permission.Granted)
        {
            RequestPermissions(
                new[] { global::Android.Manifest.Permission.Camera },
                RequestCameraPermission);
            return;
        }

        var manager = GetSystemService(CameraService) as CameraManager;
        if (manager == null) { Cancel(); return; }

        string? backId = null;
        foreach (var id in manager.GetCameraIdList())
        {
            var ch = manager.GetCameraCharacteristics(id);
            if (ch.Get(CameraCharacteristics.LensFacing) is Integer facing
                && facing.IntValue() == (int)LensFacing.Back)
            {
                backId = id;
                break;
            }
        }
        if (backId == null) { Cancel(); return; }

        // ImageReader: 1920×1080 JPEG, max 2 buffered images
        _imageReader = ImageReader.NewInstance(
            1920, 1080, ImageFormatType.Jpeg, 2);
        _imageReader.SetOnImageAvailableListener(
            new ImageListener(this), _bgHandler);

        manager.OpenCamera(backId, new CameraOpenCallback(this), _bgHandler);
    }

    internal void OnCameraOpened(CameraDevice camera)
    {
        _cameraDevice = camera;

        var surface = new Surface(_textureView!.SurfaceTexture!);
        var builder = camera.CreateCaptureRequest(CameraTemplate.Preview);
        builder.AddTarget(surface);
        _previewRequest = builder;

        camera.CreateCaptureSession(
            new List<Surface> { surface, _imageReader!.Surface },
            new SessionCallback(this, builder),
            _bgHandler);
    }

    internal void OnSessionReady(CameraCaptureSession session)
    {
        _session = session;
        session.SetRepeatingRequest(_previewRequest!.Build(), null, _bgHandler);
    }

    // ── Capture ──────────────────────────────────────────────────────────────

    private void OnCaptureClicked()
    {
        if (_capturing || _cameraDevice == null || _session == null || _imageReader == null)
            return;

        _capturing = true;

        var builder = _cameraDevice.CreateCaptureRequest(CameraTemplate.StillCapture);
        builder.AddTarget(_imageReader.Surface);
        _session.Capture(builder.Build(), null, _bgHandler);
    }

    internal void SaveAndFinish(AImage image)
    {
        try
        {
            var dir = ApplicationContext!.GetExternalFilesDir(
                global::Android.OS.Environment.DirectoryPictures);
            dir?.Mkdirs();

            var ts       = DateTime.UtcNow.ToString("yyyyMMdd_HHmmss");
            var fileName = $"backcover_{_bookId ?? "unknown"}_{ts}.jpg";
            var path     = System.IO.Path.Combine(dir!.AbsolutePath, fileName);

            var buffer = image.GetPlanes()[0].Buffer!;
            var bytes  = new byte[buffer.Remaining()];
            buffer.Get(bytes);
            image.Close();

            System.IO.File.WriteAllBytes(path, bytes);

            RunOnUiThread(() =>
            {
                PendingCapture?.TrySetResult(path);
                PendingCapture = null;
                Finish();
            });
        }
        catch
        {
            image.Close();
            RunOnUiThread(() => Cancel());
        }
    }

    // ── Permission result ────────────────────────────────────────────────────

    public override void OnRequestPermissionsResult(
        int requestCode, string[] permissions, Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == RequestCameraPermission
            && grantResults.Length > 0
            && grantResults[0] == Permission.Granted)
        {
            OpenCamera();
        }
        else
        {
            Cancel();
        }
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    private void Cancel()
    {
        PendingCapture?.TrySetResult(null);
        PendingCapture = null;
        Finish();
    }

    private void StartBackgroundThread()
    {
        _bgThread = new HandlerThread("CameraBackground");
        _bgThread.Start();
        _bgHandler = new Handler(_bgThread.Looper!);
    }

    private void StopBackgroundThread()
    {
        _bgThread?.QuitSafely();
        _bgThread?.Join();
        _bgThread  = null;
        _bgHandler = null;
    }

    private void StopCamera()
    {
        try { _session?.StopRepeating(); } catch { /* ignored on teardown */ }
        _session?.Close();
        _session = null;
        _cameraDevice?.Close();
        _cameraDevice = null;
        _imageReader?.Close();
        _imageReader = null;
    }

    private static FrameLayout.LayoutParams MatchParent() =>
        new(ViewGroup.LayoutParams.MatchParent, ViewGroup.LayoutParams.MatchParent);

    private int Dp(int dp) =>
        (int)(dp * (Resources?.DisplayMetrics?.Density ?? 1f) + 0.5f);

    // ── Nested callbacks ─────────────────────────────────────────────────────

    private sealed class CameraOpenCallback : CameraDevice.StateCallback
    {
        private readonly CameraWithRulersActivity _a;
        public CameraOpenCallback(CameraWithRulersActivity a) => _a = a;
        public override void OnOpened(CameraDevice camera)       => _a.OnCameraOpened(camera);
        public override void OnDisconnected(CameraDevice camera) => camera.Close();
        public override void OnError(CameraDevice camera, CameraError error) => camera.Close();
    }

    private sealed class SessionCallback : CameraCaptureSession.StateCallback
    {
        private readonly CameraWithRulersActivity _a;
        private readonly CaptureRequest.Builder   _builder;
        public SessionCallback(CameraWithRulersActivity a, CaptureRequest.Builder b)
            => (_a, _builder) = (a, b);
        public override void OnConfigured(CameraCaptureSession session) =>
            _a.OnSessionReady(session);
        public override void OnConfigureFailed(CameraCaptureSession session) =>
            _a.Cancel();
    }

    private sealed class ImageListener : Java.Lang.Object, ImageReader.IOnImageAvailableListener
    {
        private readonly CameraWithRulersActivity _a;
        public ImageListener(CameraWithRulersActivity a) => _a = a;
        public void OnImageAvailable(ImageReader? reader)
        {
            var image = reader?.AcquireLatestImage();
            if (image != null) _a.SaveAndFinish(image);
        }
    }
}
