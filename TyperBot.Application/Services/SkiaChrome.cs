using SkiaSharp;

namespace TyperBot.Application.Services;

/// <summary>
/// Wspólne, lekkie ozdobniki PNG: margines, zaokrąglenie, delikatny cień (bez blur), obramowania.
/// </summary>
public static class SkiaChrome
{
    public const float CardMargin = 16f;
    public const float CardRadius = 12f;
    public const float LegendChipRadius = 3f;

    public static readonly SKColor PageBackground = new(0xE6, 0xE9, 0xEF);
    public static readonly SKColor ShadowTint = new(0x28, 0x30, 0x40, 0x12);
    public static readonly SKColor SoftStroke = new(0xB0, 0xB8, 0xC4);
    public static readonly SKColor ChartOuterFill = new(0xE8, 0xEB, 0xF2);
    public static readonly SKColor ChartPlotPanel = new(0xFE, 0xFE, 0xFF);

    public static void DrawCardDropShadow(SKCanvas canvas, float x, float y, float w, float h, float r)
    {
        using var p = new SKPaint { Color = ShadowTint, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 2f, y + 2.5f, x + w + 2f, y + h + 2.5f), r, r), p);
    }

    /// <summary>Rozpoczyna kartę: tłumaczenie + przycięcie do zaokrąglonego prostokąta.</summary>
    public static void PushClippedCard(SKCanvas canvas, float x, float y, float w, float h, float r)
    {
        canvas.Save();
        canvas.Translate(x, y);
        var rr = new SKRoundRect(new SKRect(0, 0, w, h), r, r);
        canvas.ClipRoundRect(rr, SKClipOperation.Intersect, antialias: true);
    }

    public static void PopClippedCard(SKCanvas canvas, float x, float y, float w, float h, float r)
    {
        canvas.Restore();
        using var stroke = new SKPaint
        {
            Color = SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1f,
            IsAntialias = true
        };
        canvas.DrawRoundRect(new SKRoundRect(new SKRect(x + 0.5f, y + 0.5f, x + w - 0.5f, y + h - 0.5f), r, r), stroke);
    }

    public static void FillLinearGradientRect(SKCanvas canvas, SKRect rect, SKColor top, SKColor bottom)
    {
        using var paint = new SKPaint { IsAntialias = true };
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(rect.Left, rect.Top),
            new SKPoint(rect.Left, rect.Bottom),
            new[] { top, bottom },
            null,
            SKShaderTileMode.Clamp);
        canvas.DrawRect(rect, paint);
        paint.Shader?.Dispose();
    }

    public static SKColor Lighten(SKColor c, byte amount)
    {
        return new SKColor(
            (byte)Math.Min(255, c.Red + amount),
            (byte)Math.Min(255, c.Green + amount),
            (byte)Math.Min(255, c.Blue + amount),
            c.Alpha);
    }

    public static SKColor Darken(SKColor c, byte amount)
    {
        return new SKColor(
            (byte)Math.Max(0, c.Red - amount),
            (byte)Math.Max(0, c.Green - amount),
            (byte)Math.Max(0, c.Blue - amount),
            c.Alpha);
    }
}
