using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG tabeli: WYNIKI, strata, trafiony wynik (dokładny typ: buckety 35/50).
/// </summary>
public class TableGenerator
{
    private const int TableWidth = 880;
    private const int RowHeight = 40;
    private const int TitleBarHeight = 56;
    private const int HeaderHeight = 52;
    private const int FooterHeight = 38;
    private const float BorderWidth = 1f;

    // Kolorystyka jak na screenie (burgundia / grafit / oliwka) — lekko „żywsze” odcienie na przemian
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

    private static SKTypeface ResolveBodyTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Normal)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Normal)
        ?? SKTypeface.Default;

    private static SKTypeface ResolveBoldTypeface() =>
        SKTypeface.FromFamilyName("Segoe UI", SKFontStyle.Bold)
        ?? SKTypeface.FromFamilyName("DejaVu Sans", SKFontStyle.Bold)
        ?? SKTypeface.Default;

    public byte[] GenerateSeasonTable(Season season, List<Player> players)
    {
        var rows = CalculateSeasonRows(players, season);
        return RenderPng(
            footer: $"Sezon: {season.Name}  •  Trafiony wynik = liczba trafionych dokładnych wyników (35 lub 50 pkt)  •  {players.Count(p => p.IsActive)} graczy",
            rows);
    }

    public byte[] GenerateRoundTable(Season season, Round round, List<Player> players)
    {
        var rows = CalculateRoundRows(players, round);
        var roundLabel = RoundHelper.GetRoundLabel(round.Number);
        var matchCount = round.Matches?.Count ?? 0;
        var finished = round.Matches?.Count(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Cancelled) ?? 0;
        var desc = string.IsNullOrWhiteSpace(round.Description) ? "" : $" — {round.Description}";
        return RenderPng(
            footer: $"{roundLabel}{desc}  •  {season.Name}  •  Mecze: {finished}/{matchCount}",
            rows);
    }

    private byte[] RenderPng(string footer, List<StandingsRow> rows)
    {
        int dataRows = rows.Count == 0 ? 1 : rows.Count;
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
        DrawHeaderGrid(canvas, TitleBarHeight);
        EnrichRowsWithGapsAndRanks(rows);

        if (rows.Count == 0)
            DrawNoDataRow(canvas, dataStartY);
        else
        {
            for (int i = 0; i < rows.Count; i++)
                DrawDataRow(canvas, dataStartY + i * RowHeight, rows[i], i % 2 == 1);
        }

        DrawFooter(canvas, totalHeight, footer);

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
        const string title = "WYNIKI";
        float w = titleFont.MeasureText(title);
        float baseline = TitleBarHeight / 2f + 9f;
        canvas.DrawText(title, (TableWidth - w) / 2f, baseline, titleFont, titlePaint);
    }

    /// <summary>Column boundaries: NR | UCZESTNIK | PKT | MIEJSCE WYŻEJ | DO LIDERA | TRAFIONY WYNIK</summary>
    private static float XNr => 0f;
    private static float XName => 46f;
    private static float XPkt => 312f;
    private static float XMwyzej => 372f;
    private static float XDolidera => 438f;
    private static float XTrafWynik => 504f;
    private static float XEnd => TableWidth;

    private static void DrawHeaderGrid(SKCanvas canvas, float yTop)
    {
        float h = HeaderHeight;
        float h2 = h / 2f;
        float yMid = yTop + h2;

        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(XNr, yTop, XMwyzej, yTop + h),
            SkiaChrome.Lighten(HeaderGrey, 14),
            SkiaChrome.Darken(HeaderGrey, 6));
        SkiaChrome.FillLinearGradientRect(canvas,
            new SKRect(XMwyzej, yTop, XEnd, yTop + h),
            SkiaChrome.Lighten(HeaderGreen, 12),
            SkiaChrome.Darken(HeaderGreen, 8));

        using var tf = ResolveBoldTypeface();
        using var fontSmall = new SKFont(tf, 11f);
        using var fontStrata = new SKFont(tf, 12f);
        using var paint = new SKPaint { Color = HeaderText, IsAntialias = true };

        DrawTextCenteredInRect(canvas, "NR", XNr, yTop, XName - XNr, h, fontSmall, paint);
        DrawTextCenteredInRect(canvas, "UCZESTNIK", XName, yTop, XPkt - XName, h, fontSmall, paint);
        DrawTextCenteredInRect(canvas, "PKT", XPkt, yTop, XMwyzej - XPkt, h, fontSmall, paint);

        // STRATA only above MIEJSCE WYŻEJ | DO LIDERA (not above TRAFIONY WYNIK)
        DrawTextCenteredInRect(canvas, "STRATA", XMwyzej, yTop, XTrafWynik - XMwyzej, h2, fontStrata, paint);
        DrawTextCenteredInRect(canvas, "", XMwyzej, yMid, XDolidera - XMwyzej, h2, fontSmall, paint, twoLines: ("MIEJSCE", "WYŻEJ"));
        DrawTextCenteredInRect(canvas, "", XDolidera, yMid, XTrafWynik - XDolidera, h2, fontSmall, paint, twoLines: ("DO", "LIDERA"));

        DrawTextCenteredInRect(canvas, "", XTrafWynik, yTop, XEnd - XTrafWynik, h, fontSmall, paint, twoLines: ("TRAFIONY", "WYNIK"));

        DrawHeaderBorders(canvas, yTop, h, yMid);
    }

    private static void DrawTextCenteredInRect(
        SKCanvas canvas,
        string singleLine,
        float x,
        float y,
        float wCell,
        float hCell,
        SKFont font,
        SKPaint paint,
        (string a, string b)? twoLines = null)
    {
        if (twoLines.HasValue)
        {
            var (a, b) = twoLines.Value;
            float wa = font.MeasureText(a);
            float wb = font.MeasureText(b);
            float lineH = font.Size * 1.15f;
            float cy = y + hCell / 2f;
            canvas.DrawText(a, x + (wCell - wa) / 2f, cy - lineH * 0.15f, font, paint);
            canvas.DrawText(b, x + (wCell - wb) / 2f, cy + lineH * 0.85f, font, paint);
            return;
        }

        float tw = font.MeasureText(singleLine);
        float baseline = y + hCell / 2f + font.Size * 0.35f;
        canvas.DrawText(singleLine, x + (wCell - tw) / 2f, baseline, font, paint);
    }

    private static void DrawHeaderBorders(SKCanvas canvas, float yTop, float h, float yMid)
    {
        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };

        float yBot = yTop + h;
        canvas.DrawRect(0.5f, yTop + 0.5f, TableWidth - 1f, h - 1f, border);

        float x1 = XName, x2 = XPkt, x3 = XMwyzej, x4 = XDolidera, x5 = XTrafWynik;
        canvas.DrawLine(x1, yTop, x1, yBot, border);
        canvas.DrawLine(x2, yTop, x2, yBot, border);
        canvas.DrawLine(x3, yTop, x3, yBot, border);
        // Split MIEJSCE WYŻEJ | DO LIDERA only in lower header row (STRATA spans both columns above)
        canvas.DrawLine(x4, yMid, x4, yBot, border);
        canvas.DrawLine(x5, yTop, x5, yBot, border);
        canvas.DrawLine(x3, yMid, x5, yMid, border);
    }

    private static void DrawTableBorders(SKCanvas canvas, float yTop, float height)
    {
        using var border = new SKPaint
        {
            Color = SkiaChrome.SoftStroke,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = true
        };

        float yBot = yTop + height;
        canvas.DrawRect(0.5f, yTop + 0.5f, TableWidth - 1f, height - 1f, border);

        float x1 = XName, x2 = XPkt, x3 = XMwyzej, x4 = XDolidera, x5 = XTrafWynik;
        canvas.DrawLine(x1, yTop, x1, yBot, border);
        canvas.DrawLine(x2, yTop, x2, yBot, border);
        canvas.DrawLine(x3, yTop, x3, yBot, border);
        canvas.DrawLine(x4, yTop, x4, yBot, border);
        canvas.DrawLine(x5, yTop, x5, yBot, border);
    }

    private static void DrawDataRow(SKCanvas canvas, float y, StandingsRow row, bool alternate)
    {
        var leftA = alternate ? BodyGreyB : BodyGreyA;
        var leftB = alternate ? BodyGreyB : BodyGreyA;
        var rightA = alternate ? BodyGreenB : BodyGreenA;
        var rightB = alternate ? BodyGreenB : BodyGreenA;

        using var pl = new SKPaint { Color = leftA, IsAntialias = true };
        using var pr = new SKPaint { Color = rightA, IsAntialias = true };
        canvas.DrawRect(XNr, y, XPkt - XNr, RowHeight, pl);
        canvas.DrawRect(XPkt, y, XEnd - XPkt, RowHeight, pr);

        DrawTableBorders(canvas, y, RowHeight);

        using var bodyTf = ResolveBodyTypeface();
        using var font = new SKFont(bodyTf, 14f);
        using var paint = new SKPaint { Color = BodyText, IsAntialias = true };

        float baseline = y + RowHeight / 2f + 5f;

        string nrText = row.DisplayRank.HasValue ? row.DisplayRank.Value.ToString() : "";
        float nw = font.MeasureText(nrText);
        canvas.DrawText(nrText, XNr + (XName - XNr - nw) / 2f, baseline, font, paint);

        string name = EllipsizeName(row.PlayerName, font, XPkt - XName - 16f);
        float nameW = font.MeasureText(name);
        canvas.DrawText(name, XName + (XPkt - XName - nameW) / 2f, baseline, font, paint);

        string pkt = row.TotalPoints.ToString();
        float pw = font.MeasureText(pkt);
        canvas.DrawText(pkt, XPkt + (XMwyzej - XPkt - pw) / 2f, baseline, font, paint);

        string mw = row.GapToRowAbove ?? "";
        float mww = font.MeasureText(mw);
        canvas.DrawText(mw, XMwyzej + (XDolidera - XMwyzej - mww) / 2f, baseline, font, paint);

        string dl = row.GapToLeader ?? "";
        float dlw = font.MeasureText(dl);
        canvas.DrawText(dl, XDolidera + (XTrafWynik - XDolidera - dlw) / 2f, baseline, font, paint);

        string tw = row.TrafioneWyniki.ToString();
        float tww = font.MeasureText(tw);
        canvas.DrawText(tw, XTrafWynik + (XEnd - XTrafWynik - tww) / 2f, baseline, font, paint);
    }

    private static void DrawNoDataRow(SKCanvas canvas, float y)
    {
        using var pl = new SKPaint { Color = BodyGreyA, IsAntialias = true };
        canvas.DrawRect(0, y, TableWidth, RowHeight, pl);
        DrawTableBorders(canvas, y, RowHeight);
        using var tf = ResolveBodyTypeface();
        using var font = new SKFont(tf, 14f);
        using var paint = new SKPaint { Color = BodyText, IsAntialias = true };
        const string msg = "Brak danych do wyświetlenia";
        float w = font.MeasureText(msg);
        canvas.DrawText(msg, (TableWidth - w) / 2f, y + RowHeight / 2f + 5f, font, paint);
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

        string line = footerText;
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

    private static void EnrichRowsWithGapsAndRanks(List<StandingsRow> rows)
    {
        if (rows.Count == 0) return;

        for (int i = 0; i < rows.Count; i++)
        {
            if (i == 0)
            {
                rows[i].DisplayRank = 1;
                rows[i].GapToRowAbove = "-";
                rows[i].GapToLeader = "-";
                continue;
            }

            // Remis z osobą wyżej: puste NR (jak na klasycznych tabelach)
            rows[i].DisplayRank = rows[i].TotalPoints == rows[i - 1].TotalPoints ? null : i + 1;

            int gapAbove = rows[i - 1].TotalPoints - rows[i].TotalPoints;
            rows[i].GapToRowAbove = gapAbove == 0 ? "0" : gapAbove.ToString();

            int gapLeader = rows[0].TotalPoints - rows[i].TotalPoints;
            rows[i].GapToLeader = gapLeader == 0 ? "0" : gapLeader.ToString();
        }
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

    private static HashSet<int> ResolveSeasonMatchIds(Season season)
    {
        var set = new HashSet<int>();
        if (season.Rounds == null) return set;
        foreach (var r in season.Rounds)
        {
            if (r.Matches == null) continue;
            foreach (var m in r.Matches)
                set.Add(m.Id);
        }
        return set;
    }

    private static List<StandingsRow> CalculateSeasonRows(List<Player> players, Season season)
    {
        var seasonMatchIds = ResolveSeasonMatchIds(season);
        var filterBySeason = seasonMatchIds.Count > 0;
        var list = new List<StandingsRow>();

        foreach (var player in players.Where(p => p.IsActive))
        {
            var predsInSeason = player.Predictions
                .Where(p => p.IsValid && (!filterBySeason || seasonMatchIds.Contains(p.MatchId)))
                .ToList();
            var scored = predsInSeason.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();
            list.Add(BuildRow(player.DiscordUsername, scored));
        }

        list.Sort(CompareStandingsRows);
        return list;
    }

    private static List<StandingsRow> CalculateRoundRows(List<Player> players, Round round)
    {
        var roundMatchIds = (round.Matches ?? Array.Empty<Match>()).Select(m => m.Id).ToHashSet();
        var list = new List<StandingsRow>();

        foreach (var player in players.Where(p => p.IsActive))
        {
            var predsInRound = player.Predictions
                .Where(p => roundMatchIds.Contains(p.MatchId) && p.IsValid)
                .ToList();
            var scored = predsInRound.Where(p => p.PlayerScore != null).Select(p => p.PlayerScore!).ToList();
            list.Add(BuildRow(player.DiscordUsername, scored));
        }

        list.Sort(CompareStandingsRows);
        return list;
    }

    private static StandingsRow BuildRow(string playerName, List<PlayerScore> scored) =>
        new()
        {
            PlayerName = playerName,
            TotalPoints = scored.Sum(s => s.Points),
            TrafioneWyniki = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50)
        };

    private static int CompareStandingsRows(StandingsRow a, StandingsRow b)
    {
        int c = b.TotalPoints.CompareTo(a.TotalPoints);
        if (c != 0) return c;
        c = b.TrafioneWyniki.CompareTo(a.TrafioneWyniki);
        return c != 0 ? c : string.Compare(a.PlayerName, b.PlayerName, StringComparison.OrdinalIgnoreCase);
    }

    private sealed class StandingsRow
    {
        public string PlayerName { get; set; } = string.Empty;
        public int TotalPoints { get; set; }
        public int TrafioneWyniki { get; set; }
        public int? DisplayRank { get; set; }
        public string? GapToRowAbove { get; set; }
        public string? GapToLeader { get; set; }
    }
}
