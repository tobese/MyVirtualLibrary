using Android.Content;
using Android.Views;
using ACanvas = Android.Graphics.Canvas;
using AColor  = Android.Graphics.Color;
using APaint  = Android.Graphics.Paint;

namespace VirtualLibrary.Client.Platforms.Android;

/// <summary>
/// Transparent overlay that draws centimetre rulers along the left (Y) and
/// bottom (X) edges of the camera viewfinder.  Tick density is derived from
/// the physical screen DPI so the graduations are accurate on the device.
///
/// Major ticks at every 5 cm are labelled; medium ticks at every 2 cm are
/// unlabelled but slightly longer; minor ticks at every 1 cm are short.
/// </summary>
internal sealed partial class RulerOverlayView : View
{
    private const float RulerSizeDp = 48f;

    private readonly APaint _bgPaint;
    private readonly APaint _tickPaint;
    private readonly APaint _textPaint;

    public RulerOverlayView(Context context) : base(context)
    {
        SetBackgroundColor(AColor.Transparent);

        _bgPaint = new APaint { Color = new AColor(0, 0, 0, 160) };
        _bgPaint.SetStyle(APaint.Style.Fill);

        _tickPaint = new APaint { Color = AColor.White, StrokeWidth = 2f };
        _tickPaint.SetStyle(APaint.Style.Stroke);
        _tickPaint.AntiAlias = true;

        _textPaint = new APaint { Color = AColor.White, TextSize = 26f };
        _textPaint.AntiAlias = true;
        _textPaint.TextAlign = APaint.Align.Left;
    }

    protected override void OnDraw(ACanvas? canvas)
    {
        if (canvas == null) return;

        var dm = Resources!.DisplayMetrics!;
        float rulerPx = RulerSizeDp * dm.Density;

        // Semi-transparent ruler backgrounds
        canvas.DrawRect(0, 0, rulerPx, Height, _bgPaint);
        canvas.DrawRect(0, Height - rulerPx, Width, Height, _bgPaint);

        DrawYRuler(canvas, dm.Ydpi, rulerPx);
        DrawXRuler(canvas, dm.Xdpi, rulerPx);
    }

    private void DrawYRuler(ACanvas canvas, float dpi, float rulerPx)
    {
        float pxPerCm = dpi / 2.54f;
        for (int cm = 0; cm * pxPerCm <= Height; cm++)
        {
            float y = cm * pxPerCm;
            bool major  = cm % 5 == 0;
            bool medium = cm % 2 == 0;
            float tickLen = major  ? rulerPx * 0.70f :
                            medium ? rulerPx * 0.45f : rulerPx * 0.25f;

            canvas.DrawLine(rulerPx - tickLen, y, rulerPx, y, _tickPaint);

            if (major && cm > 0)
                canvas.DrawText($"{cm}", 4, y + _textPaint.TextSize * 0.4f, _textPaint);
        }
    }

    private void DrawXRuler(ACanvas canvas, float dpi, float rulerPx)
    {
        float pxPerCm = dpi / 2.54f;
        float topEdge = Height - rulerPx;

        for (int cm = 0; cm * pxPerCm <= Width; cm++)
        {
            float x = cm * pxPerCm;
            bool major  = cm % 5 == 0;
            bool medium = cm % 2 == 0;
            float tickLen = major  ? rulerPx * 0.70f :
                            medium ? rulerPx * 0.45f : rulerPx * 0.25f;

            canvas.DrawLine(x, topEdge, x, topEdge + tickLen, _tickPaint);

            if (major && cm > 0)
                canvas.DrawText($"{cm}", x + 4, Height - 8, _textPaint);
        }
    }
}
