using System.Linq;
using System.Reflection;
using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG: tabele punktów mecz/kolejka z deltą, histogram rozkładu punktów w całym sezonie, wykres skumulowanych punktów (cały sezon), wykres kołowy rozkładu punktów jednego gracza, tabela „Landrynki”.
/// </summary>
public sealed class StandingsAnalyticsGenerator
{
    private readonly IPlayerDisplayNameResolver _displayNames;

    public StandingsAnalyticsGenerator(IPlayerDisplayNameResolver displayNames) =>
        _displayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));

    private const int DeltaTableWidth = 820;
    private const int RowHeight = 38;
    private const int TitleBarHeight = 52;
    private const int HeaderHeight = 48;
    private const int FooterHeight = 36;

    private const int ChartWidth = 1240;
    private const int ChartHeight = 780;
    private const int ChartMarginLeft = 56;
    private const int ChartMarginRight = 168;
    private const int ChartMarginTop = 56;
    private const int ChartMarginBottom = 72;

    private const int HistogramMinWidth = 720;
    private const int HistogramMaxWidth = 1040;
    private const float HistogramNameCol = 168f;

    private const int PieChartWidth = 760;
    private const int PieChartHeight = 600;
    private const int PieLegendWidth = 248;

    /// <summary>Możliwe wartości punktów za mecz w obecnym regulaminie (malejąco); dodatkowe wartości doklejane jeśli wystąpią w danych.</summary>
    private static readonly int[] StandardPointColumnsDescending =
        { 50, 35, 20, 18, 16, 14, 12, 10, 8, 6, 4, 2, 0 };

    private static readonly SKColor TitleBg = new(0x3D, 0x3D, 0x45);
    private static readonly SKColor HeaderBg = new(0x55, 0x55, 0x5E);
    private static readonly SKColor RowA = new(0xF0, 0xF1, 0xF6);
    private static readonly SKColor RowB = new(0xE2, 0xE4, 0xED);
    private static readonly SKColor FooterBg = new(0x2A, 0x2C, 0x34);
    private static readonly SKColor FooterText = new(0xC0, 0xC6, 0xD0);
    private static readonly SKColor ChartBg = new(0xF6, 0xF7, 0xFB);
    private static readonly SKColor GridColor = new(0xD8, 0xDC, 0xE6);
    private static readonly SKColor RoundBand = new(0xE4, 0xE8, 0xF5);
    private static readonly SKColor AxisLine = new(0x8E, 0x94, 0xA3);

    /// <summary>Dokładnie referencyjny magenta (#FF00FF) — tylko 1. miejsce w Landrynkach.</summary>
    private static readonly SKColor LandPinkTop = new(0xFF, 0x00, 0xFF);

    /// <summary>Bardzo wyblakły róż przy ostatnim miejscu (malejąca saturacja przy H≈300°).</summary>
    private static readonly SKColor LandPinkBottomRow = ColorFromHsv(300f, 0.026f, 1f);

    /// <summary>Jasne tło za kartą PNG (neutralne, prawie biel).</summary>
    private static readonly SKColor LandTableCanvasBg = new(0xFF, 0xFE, 0xFF);

    /// <summary>
    /// S, V w zakresie 0–1. Własna konwersja — <see cref="SKColor.FromHsv"/> w SkiaSharp bywa źle interpretowana (prawie czarne kolory, szare wykresy).
    /// </summary>
    private static SKColor ColorFromHsv(float hDegrees, float s, float v, byte a = 255)
    {
        hDegrees %= 360f;
        if (hDegrees < 0) hDegrees += 360f;
        s = Math.Clamp(s, 0f, 1f);
        v = Math.Clamp(v, 0f, 1f);

        float c = v * s;
        float h = hDegrees / 60f;
        float x = c * (1f - Math.Abs(h % 2f - 1f));
        float m = v - c;

        float r1, g1, b1;
        if (h < 1f) { r1 = c; g1 = x; b1 = 0; }
        else if (h < 2f) { r1 = x; g1 = c; b1 = 0; }
        else if (h < 3f) { r1 = 0; g1 = c; b1 = x; }
        else if (h < 4f) { r1 = 0; g1 = x; b1 = c; }
        else if (h < 5f) { r1 = x; g1 = 0; b1 = c; }
        else { r1 = c; g1 = 0; b1 = x; }

        return new SKColor(
            (byte)Math.Round(Math.Clamp((r1 + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((g1 + m) * 255, 0, 255)),
            (byte)Math.Round(Math.Clamp((b1 + m) * 255, 0, 255)),
            a);
    }

    private static SKTypeface Body() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal)
        ?? SKTypeface.Default;

    private static SKTypeface Bold() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold)
        ?? SKTypeface.Default;

    public static bool IsFinishedWithScore(Match m) =>
        m.Status == MatchStatus.Finished && m.HomeScore.HasValue && m.AwayScore.HasValue;

    public static List<Match> OrderFinishedMatches(Season season)
    {
        var list = new List<Match>();
        foreach (var r in (season.Rounds ?? []).OrderBy(x => x.Number))
        foreach (var m in (r.Matches ?? []).Where(IsFinishedWithScore).OrderBy(x => x.StartTime))
            list.Add(m);
        return list;
    }

    /// <summary>Zachowuje kolejność jak <see cref="OrderFinishedMatches"/>; numer kolejki z drzewa sezonu (bez polegania na Match.Round).</summary>
    public static List<(Match Match, int RoundNumber)> OrderFinishedMatchesWithRoundNumbers(Season season)
    {
        var list = new List<(Match, int)>();
        foreach (var r in (season.Rounds ?? []).OrderBy(x => x.Number))
        foreach (var m in (r.Matches ?? []).Where(IsFinishedWithScore).OrderBy(x => x.StartTime))
            list.Add((m, r.Number));
        return list;
    }

    public static Match? GetPreviousFinishedMatch(IReadOnlyList<Match> ordered, Match target)
    {
        for (var i = 0; i < ordered.Count; i++)
        {
            if (ordered[i].Id != target.Id) continue;
            return i > 0 ? ordered[i - 1] : null;
        }
        return null;
    }

    public static int PointsInMatch(Player player, int matchId)
    {
        foreach (var pr in player.Predictions ?? Enumerable.Empty<Prediction>())
        {
            if (pr.MatchId != matchId || !pr.IsValid || pr.PlayerScore == null) continue;
            return pr.PlayerScore.Points;
        }
        return 0;
    }

    public static int PointsInRound(Player player, Round round)
    {
        var ids = (round.Matches ?? []).Where(IsFinishedWithScore).Select(m => m.Id).ToHashSet();
        var sum = 0;
        foreach (var pr in player.Predictions ?? Enumerable.Empty<Prediction>())
        {
            if (!ids.Contains(pr.MatchId) || !pr.IsValid || pr.PlayerScore == null) continue;
            sum += pr.PlayerScore.Points;
        }
        return sum;
    }

    public List<AnalyticsDeltaRow> BuildMatchDeltaRows(Match target, Match? previous, List<Player> players)
    {
        var sorted = players.Where(x => x.IsActive).ToList();
        sorted.Sort((a, b) =>
        {
            int ca = PointsInMatch(a, target.Id);
            int cb = PointsInMatch(b, target.Id);
            int c = cb.CompareTo(ca);
            if (c != 0) return c;
            int ba = (int)(BucketInMatch(a, target.Id) ?? Bucket.P0);
            int bb = (int)(BucketInMatch(b, target.Id) ?? Bucket.P0);
            c = bb.CompareTo(ba);
            return c != 0 ? c : string.Compare(a.DiscordUsername, b.DiscordUsername, StringComparison.OrdinalIgnoreCase);
        });

        var list = new List<AnalyticsDeltaRow>(sorted.Count);
        foreach (var p in sorted)
        {
            var cur = PointsInMatch(p, target.Id);
            int? prevPts = previous == null ? null : PointsInMatch(p, previous.Id);
            var delta = prevPts.HasValue ? cur - prevPts.Value : (int?)null;
            list.Add(new AnalyticsDeltaRow(_displayNames.GetDisplayName(p), cur, delta));
        }

        return list;
    }

    public List<AnalyticsDeltaRow> BuildRoundDeltaRows(Round target, Round? previousRound, List<Player> players)
    {
        var roundFinishedIds = (target.Matches ?? Enumerable.Empty<Match>())
            .Where(IsFinishedWithScore)
            .Select(m => m.Id)
            .ToHashSet();

        var sorted = players.Where(x => x.IsActive).ToList();
        sorted.Sort((a, b) =>
        {
            int ca = PointsInRound(a, target);
            int cb = PointsInRound(b, target);
            int c = cb.CompareTo(ca);
            if (c != 0) return c;
            if (roundFinishedIds.Count == 0)
                return string.Compare(a.DiscordUsername, b.DiscordUsername, StringComparison.OrdinalIgnoreCase);
            return StandingsTieBreak.ComparePlayersByPredictions(a, b, roundFinishedIds);
        });

        var list = new List<AnalyticsDeltaRow>(sorted.Count);
        foreach (var p in sorted)
        {
            var cur = PointsInRound(p, target);
            int? prevPts = previousRound == null ? null : PointsInRound(p, previousRound);
            var delta = prevPts.HasValue ? cur - prevPts.Value : (int?)null;
            list.Add(new AnalyticsDeltaRow(_displayNames.GetDisplayName(p), cur, delta));
        }

        return list;
    }

    private static Bucket? BucketInMatch(Player player, int matchId)
    {
        foreach (var pr in player.Predictions ?? Enumerable.Empty<Prediction>())
        {
            if (pr.MatchId != matchId || !pr.IsValid || pr.PlayerScore == null) continue;
            return pr.PlayerScore.Bucket;
        }

        return null;
    }

    public static Round? GetPreviousRound(Season season, Round target)
    {
        if (target.Number <= 1) return null;
        return (season.Rounds ?? []).FirstOrDefault(r => r.Number == target.Number - 1);
    }

    public byte[] GenerateMatchDeltaTablePng(string seasonName, string matchTitle, List<AnalyticsDeltaRow> rows, string footer)
    {
        const string hPts = "PKT";
        const string hDelta = "Δ POPRZ. MECZ";
        return RenderDeltaTable($"Punkty w meczu", $"{seasonName} — {matchTitle}", hPts, hDelta, rows, footer);
    }

    public byte[] GenerateRoundDeltaTablePng(string seasonName, string roundTitle, List<AnalyticsDeltaRow> rows, string footer)
    {
        const string hPts = "PKT W KOLEJCE";
        const string hDelta = "Δ POPRZ. KOLEJKI";
        return RenderDeltaTable($"Punkty w kolejce", $"{seasonName} — {roundTitle}", hPts, hDelta, rows, footer);
    }

    private byte[] RenderDeltaTable(string title, string subtitle, string colPts, string colDelta, List<AnalyticsDeltaRow> rows, string footer)
    {
        const float x0 = 0, x1 = 44, x2 = 420, x3 = 560, x4 = DeltaTableWidth;
        int n = rows.Count == 0 ? 1 : rows.Count;
        int dataY = TitleBarHeight + HeaderHeight;
        int h = dataY + n * RowHeight + FooterHeight;

        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(DeltaTableWidth + 2 * m);
        int outH = (int)(h + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(c, m, m, DeltaTableWidth, h, rad);
        SkiaChrome.PushClippedCard(c, m, m, DeltaTableWidth, h, rad);
        c.Clear(RowA);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, 0, DeltaTableWidth, TitleBarHeight),
            SkiaChrome.Lighten(TitleBg, 16),
            SkiaChrome.Darken(TitleBg, 8));
        using var tfB = Bold();
        using var fTitle = new SKFont(tfB, 18f);
        using var fSub = new SKFont(Body(), 12f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var grey = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD8), IsAntialias = true };
        float tw = fTitle.MeasureText(title);
        c.DrawText(title, (DeltaTableWidth - tw) / 2f, TitleBarHeight / 2f + 6f, fTitle, white);
        string sub = subtitle.Length > 90 ? subtitle[..87] + "…" : subtitle;
        float sw = fSub.MeasureText(sub);
        c.DrawText(sub, (DeltaTableWidth - sw) / 2f, TitleBarHeight - 8f, fSub, grey);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, TitleBarHeight, DeltaTableWidth, TitleBarHeight + HeaderHeight),
            SkiaChrome.Lighten(HeaderBg, 12),
            SkiaChrome.Darken(HeaderBg, 5));

        using var fH = new SKFont(tfB, 11f);
        using var blk = new SKPaint { Color = SKColors.White, IsAntialias = true };
        DrawCellCentered(c, "NR", x0, TitleBarHeight, x1 - x0, HeaderHeight, fH, blk);
        DrawCellCentered(c, "UCZESTNIK", x1, TitleBarHeight, x2 - x1, HeaderHeight, fH, blk);
        DrawCellCentered(c, colPts, x2, TitleBarHeight, x3 - x2, HeaderHeight, fH, blk);
        DrawCellCentered(c, colDelta, x3, TitleBarHeight, x4 - x3, HeaderHeight, fH, blk);

        using var fBody = new SKFont(Body(), 13f);
        using var txt = new SKPaint { Color = new SKColor(0x12, 0x12, 0x18), IsAntialias = true };

        if (rows.Count == 0)
        {
            using var rp = new SKPaint { Color = RowA, IsAntialias = true };
            c.DrawRect(0, dataY, DeltaTableWidth, RowHeight, rp);
            const string msg = "Brak danych";
            float mw = fBody.MeasureText(msg);
            c.DrawText(msg, (DeltaTableWidth - mw) / 2f, dataY + RowHeight / 2f + 5f, fBody, txt);
        }
        else
        {
            for (var i = 0; i < rows.Count; i++)
            {
                float y = dataY + i * RowHeight;
                using var rp = new SKPaint { Color = i % 2 == 1 ? RowB : RowA, IsAntialias = true };
                c.DrawRect(0, y, DeltaTableWidth, RowHeight, rp);
                var row = rows[i];
                string nr = (i + 1).ToString();
                float nw = fBody.MeasureText(nr);
                c.DrawText(nr, x0 + (x1 - x0 - nw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
                string name = Ellipsize(row.PlayerName, fBody, x2 - x1 - 12f);
                float nameW = fBody.MeasureText(name);
                c.DrawText(name, x1 + (x2 - x1 - nameW) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
                string pts = row.PointsCurrent.ToString();
                float pw = fBody.MeasureText(pts);
                c.DrawText(pts, x2 + (x3 - x2 - pw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
                string d = FormatDelta(row.DeltaVsPrevious);
                float dw = fBody.MeasureText(d);
                c.DrawText(d, x3 + (x4 - x3 - dw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
            }
        }

        DrawDeltaTableGrid(c, TitleBarHeight, HeaderHeight, dataY, rows.Count == 0 ? RowHeight : n * RowHeight, x1, x2, x3);
        DrawFooter(c, h, footer);

        SkiaChrome.PopClippedCard(c, m, m, DeltaTableWidth, h, rad);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private static void DrawDeltaTableGrid(SKCanvas c, float titleH, float headH, float dataY, float dataH, float x1, float x2, float x3)
    {
        float yTop = titleH;
        float yBot = dataY + dataH;
        using var pen = new SKPaint { Color = SkiaChrome.SoftStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };
        c.DrawRect(0.5f, yTop + 0.5f, DeltaTableWidth - 1f, yBot - yTop - 0.5f, pen);
        c.DrawLine(x1, yTop, x1, yBot, pen);
        c.DrawLine(x2, yTop, x2, yBot, pen);
        c.DrawLine(x3, yTop, x3, yBot, pen);
        c.DrawLine(0, titleH + headH, DeltaTableWidth, titleH + headH, pen);
        for (var i = 0; i * RowHeight < dataH; i++)
        {
            float y = dataY + i * RowHeight;
            if (i > 0) c.DrawLine(0, y, DeltaTableWidth, y, pen);
        }
    }

    private static void DrawFooter(SKCanvas c, int totalH, string text)
    {
        float y = totalH - FooterHeight;
        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, y, DeltaTableWidth, y + FooterHeight),
            SkiaChrome.Lighten(FooterBg, 10),
            FooterBg);
        using var f = new SKFont(Body(), 10f);
        using var p = new SKPaint { Color = FooterText, IsAntialias = true };
        string line = text;
        while (line.Length > 20 && f.MeasureText(line + "…") > DeltaTableWidth - 20f)
            line = line[..^1];
        if (line != text) line += "…";
        float w = f.MeasureText(line);
        c.DrawText(line, (DeltaTableWidth - w) / 2f, y + FooterHeight / 2f + 4f, f, p);
    }

    private static void DrawCellCentered(SKCanvas c, string s, float x, float y, float w, float h, SKFont font, SKPaint paint)
    {
        float tw = font.MeasureText(s);
        float bl = y + h / 2f + font.Size * 0.35f;
        c.DrawText(s, x + (w - tw) / 2f, bl, font, paint);
    }

    private static string FormatDelta(int? d)
    {
        if (!d.HasValue) return "—";
        if (d.Value > 0) return "+" + d.Value;
        return d.Value.ToString();
    }

    private static string Ellipsize(string name, SKFont font, float maxW)
    {
        if (string.IsNullOrEmpty(name)) return "?";
        if (font.MeasureText(name) <= maxW) return name;
        const string ell = "…";
        while (name.Length > 1 && font.MeasureText(name + ell) > maxW)
            name = name[..^1];
        return name + ell;
    }

    private static SKBitmap? _landrynkiCrownBitmap;
    private static readonly object LandrynkiCrownLock = new();

    /// <summary>PNG korony (alfa) osadzony w assembly — używany wyłącznie w tabeli Landrynki.</summary>
    private static SKBitmap? GetLandrynkiCrownBitmap()
    {
        lock (LandrynkiCrownLock)
        {
            if (_landrynkiCrownBitmap != null)
                return _landrynkiCrownBitmap;

            var asm = typeof(StandingsAnalyticsGenerator).Assembly;
            using var stream = asm.GetManifestResourceStream("TyperBot.Application.Assets.landrynki_crown_watermark.png")
                ?? asm.GetManifestResourceStream("TyperBot.Application.landrynki_crown_watermark.png")
                ?? OpenEmbeddedStreamBySuffix(asm, "landrynki_crown_watermark.png");
            if (stream == null)
                return null;

            _landrynkiCrownBitmap = SKBitmap.Decode(stream);
            return _landrynkiCrownBitmap;
        }
    }

    private static Stream? OpenEmbeddedStreamBySuffix(Assembly asm, string fileSuffix)
    {
        var name = asm.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith(fileSuffix, StringComparison.OrdinalIgnoreCase));
        return name == null ? null : asm.GetManifestResourceStream(name);
    }

    /// <summary>
    /// Mini-korona obok nicku lidera (1. miejsce): jednakowa skala, stała wysokość wizualna.
    /// </summary>
    private static void DrawLandrynkiLeaderCrownIcon(SKCanvas canvas, float leftX, float rowCenterY, float iconHeight)
    {
        var bmp = GetLandrynkiCrownBitmap();
        if (bmp == null || bmp.Width <= 0 || bmp.Height <= 0)
            return;

        float scale = iconHeight / bmp.Height;
        float cw = bmp.Width * scale;
        float ch = iconHeight;
        float top = rowCenterY - ch / 2f;
        var dest = new SKRect(leftX, top, leftX + cw, top + ch);
        using var image = SKImage.FromBitmap(bmp);
        using var p = new SKPaint { IsAntialias = true };
        canvas.DrawImage(image, dest, p);
    }

    public byte[] GenerateSeasonCumulativeChartPng(Season season, List<Player> players)
    {
        var points = OrderFinishedMatchesWithRoundNumbers(season);
        var active = players.Where(p => p.IsActive).OrderBy(p => p.DiscordUsername).ToList();

        float cm = SkiaChrome.CardMargin;
        float cr = SkiaChrome.CardRadius;
        int outW = (int)(ChartWidth + 2 * cm);
        int outH = (int)(ChartHeight + 2 * cm);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.ChartOuterFill);
        SkiaChrome.DrawCardDropShadow(c, cm, cm, ChartWidth, ChartHeight, cr);
        SkiaChrome.PushClippedCard(c, cm, cm, ChartWidth, ChartHeight, cr);
        c.Clear(ChartBg);

        float plotL = ChartMarginLeft;
        float plotR = ChartWidth - ChartMarginRight;
        float plotT = ChartMarginTop;
        float plotB = ChartHeight - ChartMarginBottom;
        float plotW = plotR - plotL;
        float plotH = plotB - plotT;

        using var titleFont = new SKFont(Bold(), 16f);
        using var axisFont = new SKFont(Body(), 11f);
        using var legFont = new SKFont(Body(), 10f);
        using var black = new SKPaint { Color = new SKColor(0x20, 0x20, 0x28), IsAntialias = true };
        const string chartTitle = "Skumulowane punkty — cały sezon (wszystkie zakończone mecze)";
        float tw = titleFont.MeasureText(chartTitle);
        c.DrawText(chartTitle, (ChartWidth - tw) / 2f, 28f, titleFont, black);

        if (points.Count == 0)
        {
            const string empty = "Brak zakończonych meczów z wynikiem — wykres niedostępny.";
            float ew = axisFont.MeasureText(empty);
            c.DrawText(empty, (ChartWidth - ew) / 2f, ChartHeight / 2f, axisFont, black);
            SkiaChrome.PopClippedCard(c, cm, cm, ChartWidth, ChartHeight, cr);
            using var img0 = surface.Snapshot();
            using var e0 = img0.Encode(SKEncodedImageFormat.Png, 100);
            return e0.ToArray();
        }

        int nMatches = points.Count;
        var cumByPlayer = new Dictionary<int, int[]>();
        var maxY = 1;
        foreach (var pl in active)
        {
            var cum = new int[points.Count];
            var run = 0;
            for (var i = 0; i < points.Count; i++)
            {
                run += PointsInMatch(pl, points[i].Match.Id);
                cum[i] = run;
                if (run > maxY) maxY = run;
            }
            cumByPlayer[pl.Id] = cum;
        }

        float MatchX(int i) => MatchIndexToX(i, nMatches, plotL, plotW);

        using (var panelFill = new SKPaint { Color = SkiaChrome.ChartPlotPanel, IsAntialias = true })
        using (var panelEdge = new SKPaint { Color = SkiaChrome.SoftStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
        {
            var panelR = new SKRoundRect(new SKRect(plotL - 8f, plotT - 6f, plotR + 8f, plotB + 6f), 10f, 10f);
            c.DrawRoundRect(panelR, panelFill);
            c.DrawRoundRect(panelR, panelEdge);
        }

        // Pasma kolejek (na przemian)
        int segStart = 0;
        for (var j = 1; j <= points.Count; j++)
        {
            var endSeg = j == points.Count || points[j].RoundNumber != points[segStart].RoundNumber;
            if (!endSeg) continue;
            float xa = MatchX(segStart);
            float xb = MatchX(j - 1);
            var bandColor = points[segStart].RoundNumber % 2 == 1 ? RoundBand : new SKColor(0xEE, 0xF0, 0xF7);
            using var bandPaint = new SKPaint { Color = bandColor, IsAntialias = true };
            c.DrawRect(xa, plotT, Math.Max(2f, xb - xa + 1f), plotH, bandPaint);
            segStart = j;
        }

        using (var grid = new SKPaint { Color = GridColor, StrokeWidth = 1, Style = SKPaintStyle.Stroke, IsAntialias = true })
        {
            for (var g = 0; g <= 4; g++)
            {
                float yy = plotB - (g / 4f) * plotH;
                c.DrawLine(plotL, yy, plotR, yy, grid);
            }
        }

        using (var axis = new SKPaint { Color = AxisLine, StrokeWidth = 1.25f, Style = SKPaintStyle.Stroke, IsAntialias = true })
        {
            c.DrawLine(plotL, plotT, plotL, plotB, axis);
            c.DrawLine(plotL, plotB, plotR, plotB, axis);
        }

        for (var g = 0; g <= 4; g++)
        {
            int val = (int)Math.Round(maxY * (g / 4f));
            string lab = val.ToString();
            float yy = plotB - (g / 4f) * plotH;
            float lw = axisFont.MeasureText(lab);
            c.DrawText(lab, plotL - lw - 8f, yy + 4f, axisFont, black);
        }

        using var dashFx = SKPathEffect.CreateDash(new float[] { 4, 4 }, 0);
        using var vp = new SKPaint
        {
            Color = new SKColor(0x88, 0x88, 0x98),
            PathEffect = dashFx,
            StrokeWidth = 1,
            Style = SKPaintStyle.Stroke,
            IsAntialias = true
        };

        int? prevRoundLabel = null;
        for (var i = 0; i < points.Count; i++)
        {
            int rn = points[i].RoundNumber;
            if (rn == prevRoundLabel) continue;
            prevRoundLabel = rn;
            float x = MatchX(i);
            string rl = RoundHelper.GetRoundLabel(rn);
            float rw = axisFont.MeasureText(rl);
            c.DrawText(rl, x - rw / 2f, plotB + 18f, axisFont, black);
            c.DrawLine(x, plotT, x, plotB, vp);
        }

        var colors = new List<SKColor>();
        for (var pi = 0; pi < active.Count; pi++)
            colors.Add(PlayerLineColor(pi, active.Count));

        float lineStroke = active.Count switch
        {
            <= 8 => 2.6f,
            <= 14 => 2.2f,
            <= 20 => 1.95f,
            _ => 1.7f
        };

        float lxEnd = MatchX(points.Count - 1);
        float labelX = Math.Min(plotR + 6f, lxEnd + 6f);

        for (var pi = 0; pi < active.Count; pi++)
        {
            var pl = active[pi];
            if (!cumByPlayer.TryGetValue(pl.Id, out var cum)) continue;
            using var path = new SKPath();
            for (var i = 0; i < points.Count; i++)
            {
                float x = MatchX(i);
                float yNorm = cum[i] / (float)maxY;
                float y = plotB - yNorm * plotH;
                if (i == 0) path.MoveTo(x, y);
                else path.LineTo(x, y);
            }

            using var stroke = new SKPaint
            {
                Color = colors[pi],
                Style = SKPaintStyle.Stroke,
                StrokeWidth = lineStroke,
                StrokeCap = SKStrokeCap.Round,
                StrokeJoin = SKStrokeJoin.Round,
                IsAntialias = true
            };
            c.DrawPath(path, stroke);
        }

        // Etykiety nicków przy ostatnim punkcie — rozsuwanie pionowe, gdy idealne Y są zbyt blisko
        // (wcześniej tylko identyczna suma punktów była grupowana; bliskie wartości nachodziły).
        var endLabels = new List<(string Nick, SKColor Color, float IdealBaseline)>();
        for (var pi = 0; pi < active.Count; pi++)
        {
            var pl = active[pi];
            if (!cumByPlayer.TryGetValue(pl.Id, out var cum)) continue;
            string nick = Ellipsize(_displayNames.GetDisplayName(pl), legFont, 200f);
            float yNorm = cum[^1] / (float)maxY;
            float ideal = plotB - yNorm * plotH + 4f;
            endLabels.Add((nick, colors[pi], ideal));
        }

        float minLabelY = plotT + legFont.Size + 2f;
        float maxLabelY = plotB - 4f;

        endLabels.Sort((a, b) => a.IdealBaseline.CompareTo(b.IdealBaseline));

        int nl = endLabels.Count;
        if (nl > 0)
        {
            var posY = new float[nl];
            float gap = legFont.Size * 1.22f;
            const float gapFloor = 9f;

            for (var attempt = 0; attempt < 18; attempt++)
            {
                posY[0] = Math.Max(endLabels[0].IdealBaseline, minLabelY);
                for (var i = 1; i < nl; i++)
                    posY[i] = Math.Max(endLabels[i].IdealBaseline, posY[i - 1] + gap);

                for (var pan = 0; pan < 16; pan++)
                {
                    if (posY[nl - 1] > maxLabelY)
                    {
                        float d = posY[nl - 1] - maxLabelY;
                        for (var i = 0; i < nl; i++)
                            posY[i] -= d;
                        continue;
                    }

                    if (posY[0] < minLabelY)
                    {
                        float d = minLabelY - posY[0];
                        for (var i = 0; i < nl; i++)
                            posY[i] += d;
                        continue;
                    }

                    break;
                }

                if (posY[0] >= minLabelY && posY[nl - 1] <= maxLabelY)
                    break;

                gap = Math.Max(gapFloor, gap * 0.88f);
            }

            for (var i = 0; i < nl; i++)
            {
                float yText = Math.Clamp(posY[i], minLabelY, maxLabelY);
                var row = endLabels[i];
                using var np = new SKPaint { Color = row.Color, IsAntialias = true };
                c.DrawText(row.Nick, labelX, yText, legFont, np);
            }
        }

        string foot = $"{season.Name}  •  Cały sezon  •  Mecze: {points.Count}  •  Gracze: {active.Count}";
        using var ff = new SKFont(Body(), 10f);
        using var fp = new SKPaint { Color = FooterText, IsAntialias = true };
        float fw = ff.MeasureText(foot);
        c.DrawText(foot, (ChartWidth - fw) / 2f, ChartHeight - 14f, ff, fp);

        SkiaChrome.PopClippedCard(c, cm, cm, ChartWidth, ChartHeight, cr);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    /// <summary>
    /// Tabela „Landrynki”: aktywni gracze z co najmniej jednym zakończonym meczem bez ważnego typu.
    /// Zwraca <c>null</c>, gdy brak zakończonych meczów z wynikiem albo wszyscy mają pełne typy.
    /// </summary>
    public byte[]? TryGenerateLandrynkiTablePng(Season season, List<Player> players)
    {
        var rows = BuildLandrynkiBarEntries(season, players, _displayNames.GetDisplayName);
        if (rows.Count == 0)
            return null;

        var nFin = ResolveSeasonFinishedMatchIds(season).Count;
        var footer =
            $"{season.Name}  •  Zakończone mecze z wynikiem: {nFin}  •  Liczba w kolumnie = mecze bez ważnego typu";
        return RenderLandrynkiTable(rows, footer);
    }

    /// <summary>Do testów — ta sama logika co <see cref="TryGenerateLandrynkiTablePng"/> bez resolvera Discord.</summary>
    public static List<LandrynkiBarEntry> BuildLandrynkiBarEntries(
        Season season,
        IEnumerable<Player> players,
        Func<Player, string> getDisplayName)
    {
        var finishedIds = ResolveSeasonFinishedMatchIds(season);
        if (finishedIds.Count == 0)
            return new List<LandrynkiBarEntry>();

        var list = new List<LandrynkiBarEntry>();
        foreach (var p in players.Where(x => x.IsActive))
        {
            var missed = 0;
            foreach (var mid in finishedIds)
            {
                var hasValid = (p.Predictions ?? Enumerable.Empty<Prediction>())
                    .Any(pr => pr.MatchId == mid && pr.IsValid);
                if (!hasValid)
                    missed++;
            }

            if (missed > 0)
                list.Add(new LandrynkiBarEntry(getDisplayName(p), missed));
        }

        list.Sort((a, b) =>
        {
            var c = b.MissedCount.CompareTo(a.MissedCount);
            return c != 0
                ? c
                : string.Compare(a.PlayerName, b.PlayerName, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }

    private byte[] RenderLandrynkiTable(IReadOnlyList<LandrynkiBarEntry> rows, string footer)
    {
        const float x0 = 0f, x1 = 52f, x2 = 560f, x3 = DeltaTableWidth;
        const string hMiss = "OPUSZCZONE";
        int n = rows.Count;
        int dataY = TitleBarHeight + HeaderHeight;
        int h = dataY + n * RowHeight + FooterHeight;

        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(DeltaTableWidth + 2 * m);
        int outH = (int)(h + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(c, m, m, DeltaTableWidth, h, rad);
        SkiaChrome.PushClippedCard(c, m, m, DeltaTableWidth, h, rad);
        c.Clear(LandTableCanvasBg);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, 0, DeltaTableWidth, TitleBarHeight),
            new SKColor(0xC8, 0x20, 0x88),
            LandPinkTop);
        using var tfB = Bold();
        using var fTitle = new SKFont(tfB, 18f);
        using var fSub = new SKFont(Body(), 11f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var grey = new SKPaint { Color = new SKColor(0xF8, 0xE8, 0xF4), IsAntialias = true };

        const string title = "Tabela Landrynek";
        float tw = fTitle.MeasureText(title);
        float titleBaseline = TitleBarHeight / 2f + 6f;
        c.DrawText(title, (DeltaTableWidth - tw) / 2f, titleBaseline, fTitle, white);

        const string sub = "Ranking wg liczby zakończonych meczów bez ważnego typu";
        string subEll = sub.Length > 96 ? sub[..93] + "…" : sub;
        float sw = fSub.MeasureText(subEll);
        c.DrawText(subEll, (DeltaTableWidth - sw) / 2f, TitleBarHeight - 8f, fSub, grey);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, TitleBarHeight, DeltaTableWidth, TitleBarHeight + HeaderHeight),
            SkiaChrome.Darken(LandPinkTop, 40),
            SkiaChrome.Darken(LandPinkTop, 18));

        using var fH = new SKFont(tfB, 11f);
        using var hWhite = new SKPaint { Color = SKColors.White, IsAntialias = true };
        DrawCellCentered(c, "NR", x0, TitleBarHeight, x1 - x0, HeaderHeight, fH, hWhite);
        DrawCellCentered(c, "UCZESTNIK", x1, TitleBarHeight, x2 - x1, HeaderHeight, fH, hWhite);
        DrawCellCentered(c, hMiss, x2, TitleBarHeight, x3 - x2, HeaderHeight, fH, hWhite);

        using var fBody = new SKFont(Body(), 13f);
        const float namePad = 6f;
        const float crownIconH = 17f;
        const float crownGap = 7f;

        for (var i = 0; i < rows.Count; i++)
        {
            float y = dataY + i * RowHeight;
            var rowBg = LandRowFill(i, rows.Count);
            using var rp = new SKPaint { Color = rowBg, IsAntialias = true };
            c.DrawRect(0, y, DeltaTableWidth, RowHeight, rp);

            var txtCol = LandTextOn(rowBg);
            using var txt = new SKPaint { Color = txtCol, IsAntialias = true };

            string nr = (i + 1).ToString();
            float nw = fBody.MeasureText(nr);
            c.DrawText(nr, x0 + (x1 - x0 - nw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);

            float baseline = y + RowHeight / 2f + 5f;
            float colInnerW = x2 - x1 - 2f * namePad;
            if (i == 0 && GetLandrynkiCrownBitmap() is { Width: > 0, Height: > 0 } crownBmp)
            {
                float crownW = crownBmp.Width * (crownIconH / crownBmp.Height);
                float nameMaxW = Math.Max(20f, colInnerW - crownW - crownGap);
                string name = Ellipsize(rows[i].PlayerName, fBody, nameMaxW);
                float nameW = fBody.MeasureText(name);
                float blockW = crownW + crownGap + nameW;
                float blockLeft = x1 + namePad + (colInnerW - blockW) / 2f;
                float rowMid = y + RowHeight / 2f;
                DrawLandrynkiLeaderCrownIcon(c, blockLeft, rowMid, crownIconH);
                c.DrawText(name, blockLeft + crownW + crownGap, baseline, fBody, txt);
            }
            else
            {
                string name = Ellipsize(rows[i].PlayerName, fBody, colInnerW);
                float nameW = fBody.MeasureText(name);
                c.DrawText(name, x1 + namePad + (colInnerW - nameW) / 2f, baseline, fBody, txt);
            }

            string mc = rows[i].MissedCount.ToString();
            float mw = fBody.MeasureText(mc);
            c.DrawText(mc, x2 + (x3 - x2 - mw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
        }

        DrawLandrynkiGrid(c, TitleBarHeight, HeaderHeight, dataY, n * RowHeight, x1, x2);
        DrawFooter(c, h, footer);

        SkiaChrome.PopClippedCard(c, m, m, DeltaTableWidth, h, rad);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    /// <summary>
    /// Skala koloru w HSV: stały odcień magenta (~300°), saturacja od 1 (dokładnie #FF00FF) do ~0.026 (prawie biel).
    /// Unika szarego „pasa” przy prostym lerpie RGB magenta→biały.
    /// </summary>
    private static SKColor LandRowFill(int rowIndex, int totalRows)
    {
        if (totalRows <= 1)
            return LandPinkTop;

        if (rowIndex == 0)
            return LandPinkTop;
        if (rowIndex == totalRows - 1)
            return LandPinkBottomRow;

        float u = rowIndex / (float)(totalRows - 1);
        u = Math.Clamp(u, 0f, 1f);

        const float hue = 300f;
        const float satTop = 1f;
        const float satBottom = 0.026f;

        float s = satTop + (satBottom - satTop) * u;
        return ColorFromHsv(hue, s, 1f);
    }

    private static SKColor LandTextOn(SKColor bg)
    {
        int lum = (299 * bg.Red + 587 * bg.Green + 114 * bg.Blue) / 1000;
        return lum < 150 ? SKColors.White : new SKColor(0x22, 0x0C, 0x1C);
    }

    private static void DrawLandrynkiGrid(SKCanvas c, float titleH, float headH, float dataY, float dataH, float x1, float x2)
    {
        float yTop = titleH;
        float yBot = dataY + dataH;
        using var pen = new SKPaint
        {
            Color = new SKColor(0xC8, 0x5A, 0x9A),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1,
            IsAntialias = true
        };
        c.DrawRect(0.5f, yTop + 0.5f, DeltaTableWidth - 1f, yBot - yTop - 0.5f, pen);
        c.DrawLine(x1, yTop, x1, yBot, pen);
        c.DrawLine(x2, yTop, x2, yBot, pen);
        c.DrawLine(0, titleH + headH, DeltaTableWidth, titleH + headH, pen);
        for (var i = 0; i * RowHeight < dataH; i++)
        {
            float y = dataY + i * RowHeight;
            if (i > 0)
                c.DrawLine(0, y, DeltaTableWidth, y, pen);
        }
    }

    public static HashSet<int> ResolveSeasonFinishedMatchIds(Season season)
    {
        var set = new HashSet<int>();
        foreach (var r in season.Rounds ?? Enumerable.Empty<Round>())
        foreach (var m in r.Matches ?? Enumerable.Empty<Match>())
        {
            if (IsFinishedWithScore(m))
                set.Add(m.Id);
        }
        return set;
    }

    public static List<int> ResolveHistogramPointColumns(Season season, List<Player> players, HashSet<int> finishedMatchIds)
    {
        var standard = new HashSet<int>(StandardPointColumnsDescending);
        var columns = StandardPointColumnsDescending.ToList();
        var extras = new SortedSet<int>(Comparer<int>.Create((a, b) => b.CompareTo(a)));

        foreach (var pl in players.Where(p => p.IsActive))
        {
            foreach (var pr in pl.Predictions ?? Enumerable.Empty<Prediction>())
            {
                if (!pr.IsValid || pr.PlayerScore == null) continue;
                if (!finishedMatchIds.Contains(pr.MatchId)) continue;
                var pts = pr.PlayerScore.Points;
                if (standard.Contains(pts)) continue;
                extras.Add(pts);
            }
        }

        foreach (var e in extras)
            columns.Add(e);

        columns.Sort((a, b) => b.CompareTo(a));
        return columns;
    }

    private List<PointsHistogramRow> BuildSeasonPointsHistogramRows(
        List<Player> players,
        HashSet<int> finishedMatchIds,
        IReadOnlyList<int> columns)
    {
        var list = new List<PointsHistogramRow>();
        var active = players.Where(p => p.IsActive).ToList();
        active.Sort((a, b) => StandingsTieBreak.ComparePlayersByPredictions(a, b, finishedMatchIds));

        foreach (var pl in active)
        {
            var counts = new int[columns.Count];
            foreach (var pr in pl.Predictions ?? Enumerable.Empty<Prediction>())
            {
                if (!pr.IsValid || pr.PlayerScore == null) continue;
                if (!finishedMatchIds.Contains(pr.MatchId)) continue;
                var pts = pr.PlayerScore.Points;
                var idx = -1;
                for (var i = 0; i < columns.Count; i++)
                {
                    if (columns[i] != pts) continue;
                    idx = i;
                    break;
                }

                if (idx >= 0)
                    counts[idx]++;
            }

            list.Add(new PointsHistogramRow(_displayNames.GetDisplayName(pl), counts));
        }

        return list;
    }

    /// <summary>Liczniki punktów za mecz (jak w histogramie sezonu), bez filtrowania <see cref="Player.IsActive"/> — pod widok „mój” wykres.</summary>
    public static int[] BuildSinglePlayerSeasonPointCounts(
        Player player,
        HashSet<int> finishedMatchIds,
        IReadOnlyList<int> columns)
    {
        var counts = new int[columns.Count];
        foreach (var pr in player.Predictions ?? Enumerable.Empty<Prediction>())
        {
            if (!pr.IsValid || pr.PlayerScore == null) continue;
            if (!finishedMatchIds.Contains(pr.MatchId)) continue;
            var pts = pr.PlayerScore.Points;
            var idx = -1;
            for (var i = 0; i < columns.Count; i++)
            {
                if (columns[i] != pts) continue;
                idx = i;
                break;
            }

            if (idx >= 0)
                counts[idx]++;
        }

        return counts;
    }

    public byte[] GeneratePlayerSeasonPointsPiePng(Season season, Player player)
    {
        var matchIds = ResolveSeasonFinishedMatchIds(season);
        var columns = ResolveHistogramPointColumns(season, new List<Player> { player }, matchIds);
        if (columns.Count == 0)
            columns = StandardPointColumnsDescending.ToList();

        if (matchIds.Count == 0)
            return RenderPlayerPointsPieMessage(
                season.Name,
                _displayNames.GetDisplayName(player),
                "Brak zakończonych meczów z wynikiem w sezonie.");

        var counts = BuildSinglePlayerSeasonPointCounts(player, matchIds, columns);
        var slices = new List<(int Points, int Count)>();
        for (var i = 0; i < columns.Count; i++)
        {
            if (counts[i] > 0)
                slices.Add((columns[i], counts[i]));
        }

        var total = slices.Sum(s => s.Count);
        if (total == 0)
            return RenderPlayerPointsPieMessage(
                season.Name,
                _displayNames.GetDisplayName(player),
                "Brak zaliczonych typów w zakończonych meczach tego sezonu.");

        return RenderPlayerPointsPie(season.Name, _displayNames.GetDisplayName(player), slices, total, matchIds.Count);
    }

    public byte[] GenerateSeasonPointsHistogramPng(Season season, List<Player> players)
    {
        var matchIds = ResolveSeasonFinishedMatchIds(season);
        var columns = ResolveHistogramPointColumns(season, players, matchIds);
        if (columns.Count == 0)
            columns = StandardPointColumnsDescending.ToList();

        var rows = BuildSeasonPointsHistogramRows(players, matchIds, columns);

        if (matchIds.Count == 0)
            return RenderHistogramTable(
                season.Name,
                columns,
                Array.Empty<PointsHistogramRow>(),
                "Brak zakończonych meczów z wynikiem w sezonie.");

        return RenderHistogramTable(
            season.Name,
            columns,
            rows,
            $"Liczba meczów z wynikiem w sezonie: {matchIds.Count}. Komórka = ile razy gracz dostał dokładnie tyle punktów w jednym meczu.");
    }

    private byte[] RenderHistogramTable(
        string seasonName,
        IReadOnlyList<int> columns,
        IReadOnlyList<PointsHistogramRow> rows,
        string footer)
    {
        float colW = columns.Count == 0
            ? 40f
            : Math.Max(26f, (HistogramMaxWidth - HistogramNameCol - 24f) / columns.Count);
        int tableW = (int)Math.Min(HistogramMaxWidth, Math.Max(HistogramMinWidth, HistogramNameCol + colW * columns.Count + 24f));
        if (columns.Count == 0)
            tableW = HistogramMinWidth;

        int n = rows.Count == 0 ? 1 : rows.Count;
        int dataY = TitleBarHeight + HeaderHeight;
        int h = dataY + n * RowHeight + FooterHeight;

        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(tableW + 2 * m);
        int outH = (int)(h + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(c, m, m, tableW, h, rad);
        SkiaChrome.PushClippedCard(c, m, m, tableW, h, rad);
        c.Clear(RowA);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, 0, tableW, TitleBarHeight),
            SkiaChrome.Lighten(TitleBg, 16),
            SkiaChrome.Darken(TitleBg, 8));
        using var fTitle = new SKFont(Bold(), 17f);
        using var fSub = new SKFont(Body(), 11f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var grey = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD8), IsAntialias = true };
        const string title = "Rozkład punktów — cały sezon";
        float tw = fTitle.MeasureText(title);
        c.DrawText(title, (tableW - tw) / 2f, TitleBarHeight / 2f + 5f, fTitle, white);
        string sub = seasonName.Length > 70 ? seasonName[..67] + "…" : seasonName;
        float sw = fSub.MeasureText(sub);
        c.DrawText(sub, (tableW - sw) / 2f, TitleBarHeight - 6f, fSub, grey);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, TitleBarHeight, tableW, TitleBarHeight + HeaderHeight),
            SkiaChrome.Lighten(HeaderBg, 12),
            SkiaChrome.Darken(HeaderBg, 5));

        using var fH = new SKFont(Bold(), 9f);
        using var fHdr = new SKPaint { IsAntialias = true };
        using var pen = new SKPaint { Color = SkiaChrome.SoftStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = true };

        float xName = 0f;
        float x0 = HistogramNameCol;
        c.DrawRect(0.5f, TitleBarHeight + 0.5f, tableW - 1f, HeaderHeight + n * RowHeight, pen);
        c.DrawLine(x0, TitleBarHeight, x0, TitleBarHeight + HeaderHeight + n * RowHeight, pen);

        fHdr.Color = SKColors.White;
        DrawCellCentered(c, "UCZESTNIK", xName, TitleBarHeight, x0 - xName, HeaderHeight, fH, fHdr);

        for (var col = 0; col < columns.Count; col++)
        {
            float xc = x0 + col * colW;
            string lab = columns[col].ToString();
            fHdr.Color = HistogramHeaderLabelForPoints(columns[col]);
            DrawCellCentered(c, lab, xc, TitleBarHeight, colW, HeaderHeight, fH, fHdr);
            c.DrawLine(xc + colW, TitleBarHeight, xc + colW, TitleBarHeight + HeaderHeight + n * RowHeight, pen);
        }

        using var fBody = new SKFont(Body(), 12f);
        using var txt = new SKPaint { Color = new SKColor(0x12, 0x12, 0x18), IsAntialias = true };
        using var cellPaint = new SKPaint { IsAntialias = true };
        var zeroMuted = new SKColor(0x6E, 0x72, 0x7C);

        if (rows.Count == 0)
        {
            using var rp = new SKPaint { Color = RowA, IsAntialias = true };
            c.DrawRect(0, dataY, tableW, RowHeight, rp);
            const string msg = "Brak danych";
            float mw = fBody.MeasureText(msg);
            c.DrawText(msg, (tableW - mw) / 2f, dataY + RowHeight / 2f + 5f, fBody, txt);
            c.DrawLine(0, dataY, tableW, dataY, pen);
        }
        else
        {
            for (var ri = 0; ri < rows.Count; ri++)
            {
                float y = dataY + ri * RowHeight;
                using var rp = new SKPaint { Color = ri % 2 == 1 ? RowB : RowA, IsAntialias = true };
                c.DrawRect(0, y, tableW, RowHeight, rp);
                c.DrawLine(0, y, tableW, y, pen);

                var row = rows[ri];
                string name = Ellipsize(row.PlayerName, fBody, x0 - 10f);
                float nw = fBody.MeasureText(name);
                c.DrawText(name, xName + (x0 - xName - nw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);

                for (var col = 0; col < columns.Count; col++)
                {
                    float xc = x0 + col * colW;
                    int val = row.CountsByColumnIndex[col];
                    string cell = val.ToString();
                    float cw = fBody.MeasureText(cell);
                    cellPaint.Color = val == 0 ? zeroMuted : HistogramValueTextForPoints(columns[col]);
                    c.DrawText(cell, xc + (colW - cw) / 2f, y + RowHeight / 2f + 5f, fBody, cellPaint);
                }
            }
        }

        c.DrawLine(0, TitleBarHeight + HeaderHeight, tableW, TitleBarHeight + HeaderHeight, pen);

        DrawHistogramFooter(c, tableW, h, footer);

        SkiaChrome.PopClippedCard(c, m, m, tableW, h, rad);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private static void DrawHistogramFooter(SKCanvas c, int tableW, int totalH, string text)
    {
        float y = totalH - FooterHeight;
        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, y, tableW, y + FooterHeight),
            SkiaChrome.Lighten(FooterBg, 10),
            FooterBg);
        using var f = new SKFont(Body(), 9f);
        using var p = new SKPaint { Color = FooterText, IsAntialias = true };
        string line = text;
        while (line.Length > 16 && f.MeasureText(line + "…") > tableW - 16f)
            line = line[..^1];
        if (line != text) line += "…";
        float w = f.MeasureText(line);
        c.DrawText(line, (tableW - w) / 2f, y + FooterHeight / 2f + 4f, f, p);
    }

    private byte[] RenderPlayerPointsPieMessage(string seasonName, string playerName, string message)
    {
        int innerH = TitleBarHeight + 120 + FooterHeight;
        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(PieChartWidth + 2 * m);
        int outH = (int)(innerH + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(c, m, m, PieChartWidth, innerH, rad);
        SkiaChrome.PushClippedCard(c, m, m, PieChartWidth, innerH, rad);
        c.Clear(ChartBg);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, 0, PieChartWidth, TitleBarHeight),
            SkiaChrome.Lighten(TitleBg, 16),
            SkiaChrome.Darken(TitleBg, 8));
        using var fTitle = new SKFont(Bold(), 16f);
        using var fSub = new SKFont(Body(), 11f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var grey = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD8), IsAntialias = true };
        const string title = "Twój rozkład punktów (wykres kołowy) — cały sezon";
        float tw = fTitle.MeasureText(title);
        c.DrawText(title, (PieChartWidth - tw) / 2f, TitleBarHeight / 2f + 6f, fTitle, white);
        string sub = $"{Ellipsize(playerName, fSub, PieChartWidth - 24f)}  •  {Ellipsize(seasonName, fSub, PieChartWidth - 24f)}";
        float sw = fSub.MeasureText(sub);
        c.DrawText(sub, (PieChartWidth - sw) / 2f, TitleBarHeight - 8f, fSub, grey);

        using (var panel = new SKPaint { Color = SkiaChrome.ChartPlotPanel, IsAntialias = true })
        using (var panelEdge = new SKPaint { Color = SkiaChrome.SoftStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
        {
            var pr = new SKRoundRect(new SKRect(12f, TitleBarHeight + 10f, PieChartWidth - 12f, innerH - FooterHeight - 10f), 10f, 10f);
            c.DrawRoundRect(pr, panel);
            c.DrawRoundRect(pr, panelEdge);
        }

        using var fMsg = new SKFont(Body(), 13f);
        using var blk = new SKPaint { Color = new SKColor(0x28, 0x28, 0x32), IsAntialias = true };
        float mw = fMsg.MeasureText(message);
        c.DrawText(message, (PieChartWidth - mw) / 2f, TitleBarHeight + 70f, fMsg, blk);

        DrawHistogramFooter(c, PieChartWidth, innerH, "Tylko Ty widzisz ten obrazek.");

        SkiaChrome.PopClippedCard(c, m, m, PieChartWidth, innerH, rad);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private byte[] RenderPlayerPointsPie(
        string seasonName,
        string playerName,
        List<(int Points, int Count)> slices,
        int totalPredictions,
        int finishedMatchesInSeason)
    {
        float m = SkiaChrome.CardMargin;
        float rad = SkiaChrome.CardRadius;
        int outW = (int)(PieChartWidth + 2 * m);
        int outH = (int)(PieChartHeight + 2 * m);

        using var surface = SKSurface.Create(new SKImageInfo(outW, outH, SKColorType.Rgba8888, SKAlphaType.Premul));
        var c = surface.Canvas;
        c.Clear(SkiaChrome.PageBackground);
        SkiaChrome.DrawCardDropShadow(c, m, m, PieChartWidth, PieChartHeight, rad);
        SkiaChrome.PushClippedCard(c, m, m, PieChartWidth, PieChartHeight, rad);
        c.Clear(ChartBg);

        SkiaChrome.FillLinearGradientRect(c,
            new SKRect(0, 0, PieChartWidth, TitleBarHeight),
            SkiaChrome.Lighten(TitleBg, 16),
            SkiaChrome.Darken(TitleBg, 8));
        using var fTitle = new SKFont(Bold(), 16f);
        using var fSub = new SKFont(Body(), 11f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var grey = new SKPaint { Color = new SKColor(0xD0, 0xD0, 0xD8), IsAntialias = true };
        const string title = "Twój rozkład punktów (wykres kołowy) — cały sezon";
        float tw = fTitle.MeasureText(title);
        c.DrawText(title, (PieChartWidth - tw) / 2f, TitleBarHeight / 2f + 6f, fTitle, white);
        string sub = Ellipsize($"{playerName}  •  {seasonName}", fSub, PieChartWidth - 40f);
        float sw = fSub.MeasureText(sub);
        c.DrawText(sub, (PieChartWidth - sw) / 2f, TitleBarHeight - 8f, fSub, grey);

        float plotTop = TitleBarHeight + 16f;
        float plotBottom = PieChartHeight - FooterHeight - 16f;
        float plotH = plotBottom - plotTop;
        float pieAreaW = PieChartWidth - PieLegendWidth - 32f;

        using (var plotFill = new SKPaint { Color = SkiaChrome.ChartPlotPanel, IsAntialias = true })
        using (var plotEdge = new SKPaint { Color = SkiaChrome.SoftStroke, Style = SKPaintStyle.Stroke, StrokeWidth = 1f, IsAntialias = true })
        {
            var plotR = new SKRoundRect(new SKRect(12f, plotTop - 4f, PieChartWidth - 12f, plotBottom + 4f), 12f, 12f);
            c.DrawRoundRect(plotR, plotFill);
            c.DrawRoundRect(plotR, plotEdge);
        }

        float cx = pieAreaW / 2f + 8f;
        float cy = plotTop + plotH / 2f;
        float r = Math.Min(pieAreaW / 2f - 12f, plotH / 2f - 12f);

        int n = slices.Count;
        float startDeg = -90f;
        float accDeg = 0f;
        for (var si = 0; si < n; si++)
        {
            float sweep = si == n - 1
                ? 360f - accDeg
                : 360f * slices[si].Count / (float)totalPredictions;
            var col = PieSliceFillForPoints(slices[si].Points);
            DrawPieSlice(c, cx, cy, r, startDeg, sweep, col);
            startDeg += sweep;
            accDeg += sweep;
        }

        float legX = pieAreaW + 20f;
        float legY = plotTop + 8f;
        using var legTitle = new SKFont(Bold(), 11f);
        using var legBody = new SKFont(Body(), 11f);
        using var black = new SKPaint { Color = new SKColor(0x20, 0x20, 0x28), IsAntialias = true };
        c.DrawText("Pkt — ile razy", legX, legY, legTitle, black);
        legY += 22f;
        using var legLinePaint = new SKPaint { IsAntialias = true };
        for (var si = 0; si < n; si++)
        {
            var (pts, cnt) = slices[si];
            var col = PieSliceFillForPoints(pts);
            using var sq = new SKPaint { Color = col, IsAntialias = true };
            c.DrawRoundRect(new SKRect(legX, legY - 9f, legX + 12f, legY + 1f), SkiaChrome.LegendChipRadius, SkiaChrome.LegendChipRadius, sq);
            float pct = 100f * cnt / totalPredictions;
            string legLine = $"{pts} pkt: {cnt}× ({pct:0.#}%)";
            legLine = Ellipsize(legLine, legBody, PieLegendWidth - 28f);
            legLinePaint.Color = HistogramValueTextForPoints(pts);
            c.DrawText(legLine, legX + 18f, legY, legBody, legLinePaint);
            legY += 16f;
            if (legY > plotBottom - 4f) break;
        }

        string foot =
            $"{seasonName}  •  Zakończonych meczów w sezonie: {finishedMatchesInSeason}  •  Twych typów w nich: {totalPredictions}  •  Cały sezon";
        using var ff = new SKFont(Body(), 9f);
        string footLine = foot;
        while (footLine.Length > 16 && ff.MeasureText(footLine + "…") > PieChartWidth - 16f)
            footLine = footLine[..^1];
        if (footLine != foot) footLine += "…";
        DrawHistogramFooter(c, PieChartWidth, PieChartHeight, footLine);

        SkiaChrome.PopClippedCard(c, m, m, PieChartWidth, PieChartHeight, rad);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private static void DrawPieSlice(SKCanvas canvas, float cx, float cy, float radius, float startAngleDeg, float sweepAngleDeg, SKColor fill)
    {
        if (sweepAngleDeg <= 0.01f) return;
        var oval = SKRect.Create(cx - radius, cy - radius, radius * 2f, radius * 2f);
        if (sweepAngleDeg >= 359.5f)
        {
            using var fillP = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
            canvas.DrawOval(oval, fillP);
            using var edge = new SKPaint
            {
                Color = new SKColor(0x38, 0x3A, 0x44),
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 1.35f
            };
            canvas.DrawOval(oval, edge);
            return;
        }

        // AddArc dodaje osobny kontur (sam łuk) — wypełnienie daje artefakty. ArcTo łączy środek z łukiem (prawdziwy klin).
        using var path = new SKPath();
        path.MoveTo(cx, cy);
        path.ArcTo(oval, startAngleDeg, sweepAngleDeg, forceMoveTo: false);
        path.Close();
        using var sliceFill = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, sliceFill);
        using var sliceEdge = new SKPaint
        {
            Color = new SKColor(0x38, 0x3A, 0x44),
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 1.35f
        };
        canvas.DrawPath(path, sliceEdge);
    }

    /// <summary>Jedna barwa na wartość kolumny (jak w tabeli histogramu) — wypełnienie segmentu koła.</summary>
    private static SKColor PieSliceFillForPoints(int points) =>
        ColorFromHsv(HueForPointColumn(points), 0.88f, 0.82f);

    /// <summary>Liczby w tabeli rozkładu: mocny kolor, ciemniejszy niż wypełnienie — czytelnie na białym.</summary>
    private static SKColor HistogramValueTextForPoints(int points) =>
        ColorFromHsv(HueForPointColumn(points), 0.90f, 0.42f);

    /// <summary>Etykiety kolumn na ciemnym nagłówku — jasne, lekko nasycone.</summary>
    private static SKColor HistogramHeaderLabelForPoints(int points) =>
        ColorFromHsv(HueForPointColumn(points), 0.55f, 0.98f);

    private static float HueForPointColumn(int points) =>
        points switch
        {
            50 => 352f,
            35 => 18f,
            20 => 205f,
            18 => 168f,
            16 => 265f,
            14 => 132f,
            12 => 312f,
            10 => 88f,
            8 => 348f,
            6 => 55f,
            4 => 28f,
            2 => 238f,
            0 => 0f,
            _ => Math.Abs(points) * 47.371f % 360f
        };

    /// <summary>Kolor linii / nicku gracza — złoty kąt rozdziela barwy przy wielu osobach (np. 20).</summary>
    private static SKColor PlayerLineColor(int index, int totalPlayers)
    {
        totalPlayers = Math.Max(totalPlayers, 1);
        float h = (index * 137.508f + 23f) % 360f;
        float s = 0.90f;
        // Przy dużej liczbie graczy lekko niższa jasność = mniej „pasteli”, lepszy kontrast linii obok siebie
        float v = totalPlayers >= 18 ? 0.52f : totalPlayers >= 12 ? 0.56f : 0.60f;
        return ColorFromHsv(h, s, v);
    }

    /// <summary>Oś X: przy jednym meczu punkt na środku wykresu; przy wielu — równomiernie od lewej do prawej.</summary>
    private static float MatchIndexToX(int i, int matchCount, float plotL, float plotW)
    {
        if (matchCount <= 1)
            return plotL + plotW / 2f;
        return plotL + i / (float)(matchCount - 1) * plotW;
    }
}

public sealed record AnalyticsDeltaRow(string PlayerName, int PointsCurrent, int? DeltaVsPrevious);

public sealed record LandrynkiBarEntry(string PlayerName, int MissedCount);

public sealed record PointsHistogramRow(string PlayerName, int[] CountsByColumnIndex);
