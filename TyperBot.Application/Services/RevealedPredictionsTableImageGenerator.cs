using SkiaSharp;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG: tabela ujawnionych typów (Gracz / Typ) — ta sama estetyka co <see cref="MatchResultsTableImageGenerator"/> (margines, cień, zebra).
/// </summary>
public class RevealedPredictionsTableImageGenerator
{
    private const int TableWidth = 880;
    private const int RowHeight = 40;
    private const int TitleBarHeight = 56;
    private const int HeaderHeight = 52;
    private const int FooterHeight = 38;
    private const float BorderWidth = 1f;

    /// <summary>Lewa kolumna kończy się tutaj — prawa to TYP (wyśrodkowany).</summary>
    private const float XTyp = 520f;

    private static readonly SKColor TitleBg = new(0x9A, 0x6B, 0x0C);
    private static readonly SKColor HeaderGrey = new(0x42, 0x42, 0x48);
    private static readonly SKColor HeaderText = SKColors.White;
    private static readonly SKColor BodyGreyA = new(0xEC, 0xE9, 0xEB);
    private static readonly SKColor BodyGreyB = new(0xDF, 0xDA, 0xDD);
    private static readonly SKColor BodyText = new SKColor(0x14, 0x16, 0x1C);
    private static readonly SKColor FooterBg = new(0x2A, 0x2C, 0x32);
    private static readonly SKColor FooterText = new(0xC4, 0xCA, 0xD4);

    private static SKTypeface ResolveBodyTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal)
        ?? SKTypeface.Default;

    private static SKTypeface ResolveBoldTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold)
        ?? SKTypeface.Default;

    public byte[] Generate(IReadOnlyList<RevealedTipRow> rows, string footerText)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            throw new ArgumentException("Wymagany jest co najmniej jeden wiersz.", nameof(rows));

        int dataRows = rows.Count;
        int dataStartY = TitleBarHeight + HeaderHeight;
        int totalHeight = dataStartY + dataRows * RowHeight + FooterHeight;

        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(TableWidth + 2 * m);
        int outH = (int)(totalHeight + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH));
        var canvas = surface.Canvas;
        canvas.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(canvas, m, m, TableWidth, totalHeight, rad);
        SkiaChrome.PushClippedCard(canvas, m, m, TableWidth, totalHeight, rad);
        canvas.Clear(new SKColor(0xFA, 0xFA, 0xFC));

        DrawTitleBar(canvas);
        DrawHeader(canvas, TitleBarHeight);

        for (int i = 0; i < rows.Count; i++)
        {
            float y = dataStartY + i * RowHeight;
            DrawDataRow(canvas, y, rows[i], alternate: i % 2 == 1);
        }

        DrawFooter(canvas, totalHeight, footerText);

        SkiaChrome.PopClippedCard(canvas, m, m, TableWidth, totalHeight, rad);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawTitleBar(SKCanvas canvas)
    {
        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(0, 0, TableWidth, TitleBarHeight),
            SkiaChrome.Lighten(TitleBg, 22),
            SkiaChrome.Darken(TitleBg, 12));

        using var titleTf = ResolveBoldTypeface();
        using var titleFont = new SKFont(titleTf, 24f);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        const string title = "UJAWNIONE TYPY";
        float w = titleFont.MeasureText(title);
        float baseline = TitleBarHeight / 2f + 9f;
        canvas.DrawText(title, (TableWidth - w) / 2f, baseline, titleFont, titlePaint);
    }

    private static void DrawHeader(SKCanvas canvas, float yTop)
    {
        float h = HeaderHeight;

        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(0, yTop, TableWidth, yTop + h),
            SkiaChrome.Lighten(HeaderGrey, 14),
            SkiaChrome.Darken(HeaderGrey, 6));

        using var tf = ResolveBoldTypeface();
        using var font = new SKFont(tf, 12f);
        using var paint = new SKPaint { Color = HeaderText, IsAntialias = true };

        DrawTextCenteredInRect(canvas, "GRACZ", 0, yTop, XTyp, h, font, paint);
        DrawTextCenteredInRect(canvas, "TYP", XTyp, yTop, TableWidth - XTyp, h, font, paint);

        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };
        float yBot = yTop + h;
        canvas.DrawRect(0.5f, yTop + 0.5f, TableWidth - 1f, h - 1f, border);
        canvas.DrawLine(XTyp, yTop, XTyp, yBot, border);
    }

    private static void DrawTextCenteredInRect(
        SKCanvas canvas, string text, float x, float y, float wCell, float hCell, SKFont font, SKPaint paint)
    {
        float tw = font.MeasureText(text);
        float baseline = y + hCell / 2f + font.Size * 0.35f;
        canvas.DrawText(text, x + (wCell - tw) / 2f, baseline, font, paint);
    }

    private static void DrawTextLeftInRect(
        SKCanvas canvas, string text, float x, float y, float wCell, float hCell, SKFont font, SKPaint paint, float padLeft)
    {
        float baseline = y + hCell / 2f + font.Size * 0.35f;
        canvas.DrawText(text, x + padLeft, baseline, font, paint);
    }

    private static void DrawDataRow(SKCanvas canvas, float y, RevealedTipRow row, bool alternate)
    {
        using var fill = new SKPaint { Color = alternate ? BodyGreyB : BodyGreyA, IsAntialias = true };
        canvas.DrawRect(0, y, TableWidth, RowHeight, fill);

        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };
        canvas.DrawRect(0.5f, y + 0.5f, TableWidth - 1f, RowHeight - 1f, border);
        float yBot = y + RowHeight;
        canvas.DrawLine(XTyp, y, XTyp, yBot, border);

        using var bodyTf = ResolveBodyTypeface();
        using var font = new SKFont(bodyTf, 14f);
        using var paint = new SKPaint { Color = BodyText, IsAntialias = true };

        string gracz = EllipsizeName(row.Gracz, font, XTyp - 28f);
        DrawTextLeftInRect(canvas, gracz, 0, y, XTyp, RowHeight, font, paint, 14f);

        string typ = row.Typ;
        float typW = font.MeasureText(typ);
        float typBaseline = y + RowHeight / 2f + font.Size * 0.35f;
        canvas.DrawText(typ, XTyp + (TableWidth - XTyp - typW) / 2f, typBaseline, font, paint);
    }

    private static void DrawFooter(SKCanvas canvas, int totalHeight, string footerText)
    {
        float y = totalHeight - FooterHeight;
        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(0, y, TableWidth, y + FooterHeight),
            SkiaChrome.Lighten(FooterBg, 8),
            FooterBg);

        using var tf = ResolveBodyTypeface();
        using var font = new SKFont(tf, 11f);
        using var textPaint = new SKPaint { Color = FooterText, IsAntialias = true };

        string line = footerText ?? string.Empty;
        const float maxW = TableWidth - 24f;
        if (font.MeasureText(line) > maxW)
        {
            while (line.Length > 24 && font.MeasureText(line + "…") > maxW)
                line = line[..^1];
            line += "…";
        }

        float w = font.MeasureText(line);
        canvas.DrawText(line, (TableWidth - w) / 2f, y + FooterHeight / 2f + 4f, font, textPaint);
    }

    private static string EllipsizeName(string name, SKFont font, float maxWidth)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        if (font.MeasureText(name) <= maxWidth) return name;
        const string ell = "…";
        while (name.Length > 1 && font.MeasureText(name + ell) > maxWidth)
            name = name[..^1];
        return name + ell;
    }
}

public readonly record struct RevealedTipRow(string Gracz, string Typ);
