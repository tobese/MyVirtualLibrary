// Mac Catalyst implementation of IIsbnScanner. Compiled into the
// net10.0-maccatalyst head only (see the maccatalyst PropertyGroup in
// VirtualLibrary.Client.csproj, which also defines USE_AVFOUNDATION_SCANNER).
//
// Design mirrors AndroidIsbnScanner:
//   * IsSupported is *dynamic* — a Mac mini with no camera should disable the
//     camera button the same way WebAssembly does, rather than launching an
//     empty capture session.
//   * ScanIsbnAsync returns Task<string?>; null means "user cancelled, camera
//     unavailable, or permission denied" and the ScanPage falls back to its
//     normal manual-entry/lookup path.
//   * The only ISBN barcode format in the wild is EAN-13 (ISBN-13 prefixed
//     978/979). EAN-8 is included defensively for older reprints.
//
// AVFoundation supports these symbologies on Catalyst identically to iOS:
// AVMetadataObjectTypeEAN13Code / EAN8Code are honoured by the built-in
// FaceTime HD camera as well as any external USB/Continuity camera the user
// has attached.
//
// The ViewController is presented modally from the key window's root VC so
// it works regardless of how Uno hosts the Frame (UINavigationController,
// plain UIViewController, etc.). A single TaskCompletionSource backs the
// Task<string?> so callers don't have to reason about threading.

#if __MACCATALYST__

using System;
using System.Threading;
using System.Threading.Tasks;
using AVFoundation;
using CoreFoundation;
using CoreGraphics;
using Foundation;
using UIKit;
using VirtualLibrary.Client.Services;

namespace VirtualLibrary.Client.Platforms.MacCatalyst;

/// <summary>
/// AVFoundation-backed <see cref="IIsbnScanner"/>. Opens a modal
/// <see cref="UIViewController"/> hosting a live camera preview and returns
/// the first valid EAN-13 / EAN-8 payload read.
/// </summary>
public sealed class MacCatalystIsbnScanner : IIsbnScanner
{
    // AVCaptureDevice.DefaultDeviceWithMediaType(Video) returns null on Macs
    // with no camera attached (headless Mac mini, camera disabled via MDM,
    // etc.). Evaluating this lazily keeps construction cheap and avoids
    // triggering the permission prompt before the user actually taps the
    // camera button.
    public bool IsSupported =>
        AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video) is not null;

    public async Task<string?> ScanIsbnAsync(CancellationToken ct = default)
    {
        if (!IsSupported) return null;

        // Permission: on Catalyst this prompts with the NSCameraUsageDescription
        // string from Info.plist. Users who deny (or have denied previously)
        // get a null back so the ScanPage can show the fallback message.
        var status = AVCaptureDevice.GetAuthorizationStatus(AVAuthorizationMediaType.Video);
        if (status == AVAuthorizationStatus.NotDetermined)
        {
            var granted = await AVCaptureDevice.RequestAccessForMediaTypeAsync(AVAuthorizationMediaType.Video);
            if (!granted) return null;
        }
        else if (status != AVAuthorizationStatus.Authorized)
        {
            return null;
        }

        var rootVc = FindRootViewController();
        if (rootVc is null) return null;

        var tcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);

        // Honour CancellationToken by dismissing the scanner if the caller
        // bails (e.g. the page is navigated away from mid-scan).
        using var ctRegistration = ct.Register(() => tcs.TrySetResult(null));

        var scannerVc = new BarcodeScanViewController(result =>
        {
            tcs.TrySetResult(result);
        });

        await rootVc.PresentViewControllerAsync(scannerVc, animated: true);
        var scanned = await tcs.Task.ConfigureAwait(true);

        // BarcodeScanViewController dismisses itself on a successful scan,
        // but if we were cancelled via CancellationToken it's still on screen.
        if (scannerVc.PresentingViewController is not null)
        {
            await scannerVc.DismissViewControllerAsync(animated: true);
        }

        return scanned;
    }

    /// <summary>
    /// Walks the connected scene graph to find the topmost presented view
    /// controller. Catalyst apps can have multiple UIWindowScenes (e.g. from
    /// Window → New Window) so we deliberately pick the foreground-active one.
    /// </summary>
    private static UIViewController? FindRootViewController()
    {
        foreach (var scene in UIApplication.SharedApplication.ConnectedScenes)
        {
            if (scene is UIWindowScene { ActivationState: UISceneActivationState.ForegroundActive } windowScene)
            {
                foreach (var window in windowScene.Windows)
                {
                    if (window.IsKeyWindow && window.RootViewController is { } root)
                    {
                        var top = root;
                        while (top.PresentedViewController is { } presented)
                        {
                            top = presented;
                        }
                        return top;
                    }
                }
            }
        }
        return null;
    }

    /// <summary>
    /// Minimal AVFoundation barcode reader. Lives in this file because it's
    /// only used by <see cref="MacCatalystIsbnScanner"/> and keeps the head's
    /// surface area small.
    /// </summary>
    private sealed class BarcodeScanViewController : UIViewController
    {
        private readonly Action<string?> _onComplete;
        private AVCaptureSession? _session;
        private AVCaptureVideoPreviewLayer? _previewLayer;
        private UIButton? _cancelButton;
        private UILabel? _hintLabel;
        private bool _hasCompleted;

        public BarcodeScanViewController(Action<string?> onComplete)
        {
            _onComplete = onComplete;
            // Catalyst on macOS renders this as a floating window sheet;
            // FormSheet keeps it tidy without going full-screen.
            ModalPresentationStyle = UIModalPresentationStyle.FormSheet;
            PreferredContentSize = new CGSize(640, 480);
        }

        public override void ViewDidLoad()
        {
            base.ViewDidLoad();
            View!.BackgroundColor = UIColor.Black;

            if (!TryConfigureSession(out var session))
            {
                // Nothing more we can do — bail cleanly.
                Complete(result: null);
                return;
            }

            _session = session;
            _previewLayer = new AVCaptureVideoPreviewLayer(_session)
            {
                VideoGravity = AVLayerVideoGravity.ResizeAspectFill,
                Frame = View.Bounds,
            };
            View.Layer.AddSublayer(_previewLayer);

            _hintLabel = new UILabel
            {
                Text = "Point the camera at an ISBN barcode",
                TextColor = UIColor.White,
                TextAlignment = UITextAlignment.Center,
                Font = UIFont.PreferredFootnote,
                TranslatesAutoresizingMaskIntoConstraints = false,
            };
            View.AddSubview(_hintLabel);

            _cancelButton = UIButton.FromType(UIButtonType.System);
            _cancelButton.SetTitle("Cancel", UIControlState.Normal);
            _cancelButton.SetTitleColor(UIColor.White, UIControlState.Normal);
            _cancelButton.TranslatesAutoresizingMaskIntoConstraints = false;
            _cancelButton.TouchUpInside += (_, _) => Complete(result: null);
            View.AddSubview(_cancelButton);

            NSLayoutConstraint.ActivateConstraints(new[]
            {
                _hintLabel.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                _hintLabel.BottomAnchor.ConstraintEqualTo(_cancelButton.TopAnchor, -12),

                _cancelButton.CenterXAnchor.ConstraintEqualTo(View.CenterXAnchor),
                _cancelButton.BottomAnchor.ConstraintEqualTo(View.SafeAreaLayoutGuide.BottomAnchor, -16),
            });
        }

        public override void ViewDidLayoutSubviews()
        {
            base.ViewDidLayoutSubviews();
            if (_previewLayer is not null)
            {
                _previewLayer.Frame = View!.Bounds;
            }
        }

        public override void ViewDidAppear(bool animated)
        {
            base.ViewDidAppear(animated);
            // StartRunning blocks — hop to a background queue so we don't stall
            // the window's presentation animation.
            if (_session is { Running: false } s)
            {
                DispatchQueue.DefaultGlobalQueue.DispatchAsync(s.StartRunning);
            }
        }

        public override void ViewWillDisappear(bool animated)
        {
            base.ViewWillDisappear(animated);
            if (_session is { Running: true } s)
            {
                DispatchQueue.DefaultGlobalQueue.DispatchAsync(s.StopRunning);
            }
        }

        private bool TryConfigureSession(out AVCaptureSession session)
        {
            session = new AVCaptureSession { SessionPreset = AVCaptureSession.PresetHigh };

            var device = AVCaptureDevice.GetDefaultDevice(AVMediaTypes.Video);
            if (device is null) return false;

            var input = AVCaptureDeviceInput.FromDevice(device, out var inputError);
            if (input is null || inputError is not null || !session.CanAddInput(input))
            {
                return false;
            }
            session.AddInput(input);

            var metadataOutput = new AVCaptureMetadataOutput();
            if (!session.CanAddOutput(metadataOutput))
            {
                return false;
            }
            session.AddOutput(metadataOutput);

            metadataOutput.SetDelegate(new MetadataDelegate(this), DispatchQueue.MainQueue);
            // Must be set *after* the delegate is attached, otherwise the
            // types array is rejected as "output not added to session".
            metadataOutput.MetadataObjectTypes =
                AVMetadataObjectType.EAN13Code | AVMetadataObjectType.EAN8Code;

            return true;
        }

        private void Complete(string? result)
        {
            if (_hasCompleted) return;
            _hasCompleted = true;

            // Stop the session first so the camera LED turns off before the
            // dismiss animation (Apple HIG: be a good neighbour with cameras).
            if (_session is { Running: true } s)
            {
                s.StopRunning();
            }

            // Callback may synchronously tear down the VC via TCS; dismiss
            // defensively only if still presented.
            _onComplete(result);
            if (PresentingViewController is not null)
            {
                DismissViewController(animated: true, completionHandler: null);
            }
        }

        private sealed class MetadataDelegate : AVCaptureMetadataOutputObjectsDelegate
        {
            private readonly BarcodeScanViewController _owner;

            public MetadataDelegate(BarcodeScanViewController owner) => _owner = owner;

            public override void DidOutputMetadataObjects(
                AVCaptureMetadataOutput captureOutput,
                AVMetadataObject[] metadataObjects,
                AVCaptureConnection connection)
            {
                foreach (var obj in metadataObjects)
                {
                    if (obj is AVMetadataMachineReadableCodeObject code &&
                        !string.IsNullOrEmpty(code.StringValue))
                    {
                        _owner.Complete(code.StringValue);
                        return;
                    }
                }
            }
        }
    }
}

#endif
