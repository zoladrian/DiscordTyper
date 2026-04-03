using SkiaSharp;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG: tabela wyniku meczu (Gracz / Typ / Pkt) — ta sama kolorystyka co <see cref="TableGenerator"/> (WYNIKI).
/// </summary>
public class MatchResultsTableImageGenerator
{
    private const int TableWidth = 880;
    private const int RowHeight = 40;
    private const int TitleBarHeight = 56;
    private const int HeaderHeight = 52;
    private const int FooterHeight = 38;
    private const float BorderWidth = 1f;

    private static readonly SKColor TitleBg = new(0x5C, 0x2A, 0x2A);
    private static readonly SKColor HeaderGrey = new(0x42, 0x42, 0x48);
    private static readonly SKColor HeaderGreen = new(0x4A, 0x5C, 0x3A);
    private static readonly SKColor HeaderText = SKColors.White;
    private static readonly SKColor BodyGreyA = new(0xEC, 0xE9, 0xEB);
    private static readonly SKColor BodyGreyB = new(0xDF, 0xDA, 0xDD);
    private static readonly SKColor BodyGreenA = new(0xD6, 0xE4, 0xCA);
    private static readonly SKColor BodyGreenB = new(0xC5, 0xD5, 0xB6);
    private static readonly SKColor BodyText = new SKColor(0x14, 0x16, 0x1C);
    private static readonly SKColor FooterBg = new(0x2A, 0x2C, 0x32);
    private static readonly SKColor FooterText = new(0xC4, 0xCA, 0xD4);

    /// <summary>Koniec kolumny Gracz (tekst wyrównany do lewej).</summary>
    private const float XTyp = 560f;

    /// <summary>Koniec kolumny Typ (środek); Pkt od XTyp do TableWidth.</summary>
    private const float XPkt = 740f;

    private static SKTypeface ResolveBodyTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal)
        ?? SKTypeface.Default;

    private static SKTypeface ResolveBoldTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold)
        ?? SKTypeface.Default;

    /// <param name="rows">Pierwszy wiersz = wynik rzeczywisty (jak w tekście); kolejne = gracze.</param>
    /// <param name="footerText">Stopka (np. kolejka, drużyny, liczba graczy).</param>
    public byte[] Generate(IReadOnlyList<MatchResultTableRow> rows, string footerText)
    {
        ArgumentNullException.ThrowIfNull(rows);
        if (rows.Count == 0)
            throw new ArgumentException("Wymagany jest co najmniej jeden wiersz (wynik rzeczywisty).", nameof(rows));

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
            DrawDataRow(canvas, y, rows[i], alternate: i % 2 == 1, bold: i == 0);
            if (i == 0 && rows.Count > 1)
                DrawRowSeparator(canvas, y + RowHeight);
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
            SkiaChrome.Lighten(TitleBg, 18),
            SkiaChrome.Darken(TitleBg, 10));

        using var titleTf = ResolveBoldTypeface();
        using var titleFont = new SKFont(titleTf, 24f);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        const string title = "WYNIK MECZU";
        float w = titleFont.MeasureText(title);
        float baseline = TitleBarHeight / 2f + 9f;
        canvas.DrawText(title, (TableWidth - w) / 2f, baseline, titleFont, titlePaint);
    }

    private static void DrawHeader(SKCanvas canvas, float yTop)
    {
        float h = HeaderHeight;

        // Lewa część (Gracz + Typ) — jak NR+UCZESTNIK w TableGenerator; PKT — zielony pas.
        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(0, yTop, XPkt, yTop + h),
            SkiaChrome.Lighten(HeaderGrey, 14),
            SkiaChrome.Darken(HeaderGrey, 6));
        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(XPkt, yTop, TableWidth, yTop + h),
            SkiaChrome.Lighten(HeaderGreen, 12),
            SkiaChrome.Darken(HeaderGreen, 8));

        using var tf = ResolveBoldTypeface();
        using var font = new SKFont(tf, 12f);
        using var paint = new SKPaint { Color = HeaderText, IsAntialias = true };

        DrawTextCenteredInRect(canvas, "GRACZ", 0, yTop, XTyp, h, font, paint);
        DrawTextCenteredInRect(canvas, "TYP", XTyp, yTop, XPkt - XTyp, h, font, paint);
        DrawTextCenteredInRect(canvas, "PKT", XPkt, yTop, TableWidth - XPkt, h, font, paint);

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
        canvas.DrawLine(XPkt, yTop, XPkt, yBot, border);
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

    private static void DrawDataRow(SKCanvas canvas, float y, MatchResultTableRow row, bool alternate, bool bold)
    {
        var grey = new SKPaint { Color = alternate ? BodyGreyB : BodyGreyA, IsAntialias = true };
        var green = new SKPaint { Color = alternate ? BodyGreenB : BodyGreenA, IsAntialias = true };
        canvas.DrawRect(0, y, XTyp, RowHeight, grey);
        canvas.DrawRect(XTyp, y, XPkt - XTyp, RowHeight, grey);
        canvas.DrawRect(XPkt, y, TableWidth - XPkt, RowHeight, green);
        grey.Dispose();
        green.Dispose();

        DrawRowOuterBorder(canvas, y);

        using var bodyTf = bold ? ResolveBoldTypeface() : ResolveBodyTypeface();
        using var font = new SKFont(bodyTf, 14f);
        using var paint = new SKPaint { Color = BodyText, IsAntialias = true };

        string gracz = EllipsizeName(row.Gracz, font, XTyp - 28f);
        DrawTextLeftInRect(canvas, gracz, 0, y, XTyp, RowHeight, font, paint, 14f);

        string typ = row.Typ;
        float typW = font.MeasureText(typ);
        float typBaseline = y + RowHeight / 2f + font.Size * 0.35f;
        canvas.DrawText(typ, XTyp + (XPkt - XTyp - typW) / 2f, typBaseline, font, paint);

        string pkt = row.Pkt;
        float pktW = font.MeasureText(pkt);
        float pktBaseline = y + RowHeight / 2f + font.Size * 0.35f;
        canvas.DrawText(pkt, XPkt + (TableWidth - XPkt - pktW) / 2f, pktBaseline, font, paint);

        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };
        float yBot = y + RowHeight;
        canvas.DrawLine(XTyp, y, XTyp, yBot, border);
        canvas.DrawLine(XPkt, y, XPkt, yBot, border);
    }

    private static void DrawRowOuterBorder(SKCanvas canvas, float y)
    {
        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };
        canvas.DrawRect(0.5f, y + 0.5f, TableWidth - 1f, RowHeight - 1f, border);
    }

    private static void DrawRowSeparator(SKCanvas canvas, float y)
    {
        using var line = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.5f,
            IsAntialias = true
        };
        canvas.DrawLine(8f, y, TableWidth - 8f, y, line);
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

/// <param name="Gracz">Wyświetlana nazwa gracza lub etykieta wiersza podsumowania.</param>
/// <param name="Typ">Np. „45:45” albo wynik rzeczywisty „2:1”.</param>
/// <param name="Pkt">Punkty, „—”, „-” itd.</param>
public readonly record struct MatchResultTableRow(string Gracz, string Typ, string Pkt);
