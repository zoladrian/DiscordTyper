using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG standings table styled like classic results sheets: WYNIKI, strata vs row above / vs leader, poprawne typy.
/// </summary>
public class TableGenerator
{
    private const int TableWidth = 1100;
    private const int RowHeight = 40;
    private const int TitleBarHeight = 56;
    private const int HeaderHeight = 52;
    private const int FooterHeight = 38;
    private const float BorderWidth = 1f;

    // Title bar — burgundy
    private static readonly SKColor TitleBg = new(0x5C, 0x2A, 0x2A);
    // Header — charcoal (left) / olive (right)
    private static readonly SKColor HeaderGrey = new(0x42, 0x42, 0x48);
    private static readonly SKColor HeaderGreen = new(0x4A, 0x5C, 0x3A);
    private static readonly SKColor HeaderText = SKColors.White;
    // Body — zebra greys + green-tinted right block
    private static readonly SKColor BodyGreyA = new(0xE8, 0xE8, 0xE8);
    private static readonly SKColor BodyGreyB = new(0xD4, 0xD4, 0xD4);
    private static readonly SKColor BodyGreenA = new(0xD4, 0xDE, 0xCC);
    private static readonly SKColor BodyGreenB = new(0xC0, 0xCC, 0xB4);
    private static readonly SKColor BodyText = new SKColor(0x10, 0x10, 0x10);
    private static readonly SKColor BorderBlack = SKColors.Black;
    private static readonly SKColor FooterBg = new(0x2A, 0x2A, 0x2E);
    private static readonly SKColor FooterText = new(0xB0, 0xB8, 0xC0);

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
            footer: $"Sezon: {season.Name}  •  Pkt i poprawne typy = mecze z wynikiem  •  {players.Count(p => p.IsActive)} graczy",
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

        using var surface = SKSurface.Create(new SKImageInfo(TableWidth, totalHeight));
        var canvas = surface.Canvas;
        canvas.Clear(BodyGreyA);

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

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawTitleBar(SKCanvas canvas)
    {
        using var bg = new SKPaint { Color = TitleBg, IsAntialias = true };
        canvas.DrawRect(0, 0, TableWidth, TitleBarHeight, bg);

        using var titleTf = ResolveBoldTypeface();
        using var titleFont = new SKFont(titleTf, 24f);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };
        const string title = "WYNIKI";
        float w = titleFont.MeasureText(title);
        float baseline = TitleBarHeight / 2f + 9f;
        canvas.DrawText(title, (TableWidth - w) / 2f, baseline, titleFont, titlePaint);
    }

    /// <summary>Column boundaries: NR | UCZESTNIK | PKT | MIEJSCE WYŻEJ | DO LIDERA | POPRAWNE TYPY</summary>
    private static float XNr => 0f;
    private static float XName => 52f;
    private static float XPkt => 380f;
    private static float XMwyzej => 468f;
    private static float XDolidera => 628f;
    private static float XPoprawne => 788f;
    private static float XEnd => TableWidth;

    private static void DrawHeaderGrid(SKCanvas canvas, float yTop)
    {
        float h = HeaderHeight;
        float h2 = h / 2f;
        float yMid = yTop + h2;

        // Grey = NR + UCZESTNIK + PKT; green = strata + poprawne typy
        using var grey = new SKPaint { Color = HeaderGrey, IsAntialias = true };
        using var green = new SKPaint { Color = HeaderGreen, IsAntialias = true };
        canvas.DrawRect(XNr, yTop, XMwyzej - XNr, h, grey);
        canvas.DrawRect(XMwyzej, yTop, XEnd - XMwyzej, h, green);

        using var tf = ResolveBoldTypeface();
        using var fontSmall = new SKFont(tf, 11f);
        using var fontStrata = new SKFont(tf, 12f);
        using var paint = new SKPaint { Color = HeaderText, IsAntialias = true };

        DrawTextCenteredInRect(canvas, "NR", XNr, yTop, XName - XNr, h, fontSmall, paint);
        DrawTextCenteredInRect(canvas, "UCZESTNIK", XName, yTop, XPkt - XName, h, fontSmall, paint);
        DrawTextCenteredInRect(canvas, "PKT", XPkt, yTop, XMwyzej - XPkt, h, fontSmall, paint);
        DrawTextCenteredInRect(canvas, "POPRAWNE TYPY", XPoprawne, yTop, XEnd - XPoprawne, h, fontSmall, paint);

        // STRATA only above MIEJSCE WYŻEJ | DO LIDERA
        DrawTextCenteredInRect(canvas, "STRATA", XMwyzej, yTop, XPoprawne - XMwyzej, h2, fontStrata, paint);
        DrawTextCenteredInRect(canvas, "", XMwyzej, yMid, XDolidera - XMwyzej, h2, fontSmall, paint, twoLines: ("MIEJSCE", "WYŻEJ"));
        DrawTextCenteredInRect(canvas, "", XDolidera, yMid, XPoprawne - XDolidera, h2, fontSmall, paint, twoLines: ("DO", "LIDERA"));

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
            Color = BorderBlack,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = false
        };

        float yBot = yTop + h;
        canvas.DrawRect(0.5f, yTop + 0.5f, TableWidth - 1f, h - 1f, border);

        float x1 = XName, x2 = XPkt, x3 = XMwyzej, x4 = XDolidera, x5 = XPoprawne;
        canvas.DrawLine(x1, yTop, x1, yBot, border);
        canvas.DrawLine(x2, yTop, x2, yBot, border);
        canvas.DrawLine(x3, yTop, x3, yBot, border);
        canvas.DrawLine(x4, yTop, x4, yBot, border);
        canvas.DrawLine(x5, yTop, x5, yBot, border);
        // Mid line only under STRATA (not through POPRAWNE)
        canvas.DrawLine(x3, yMid, x5, yMid, border);
    }

    private static void DrawTableBorders(SKCanvas canvas, float yTop, float height)
    {
        using var border = new SKPaint
        {
            Color = BorderBlack,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = BorderWidth,
            IsAntialias = false
        };

        float yBot = yTop + height;
        canvas.DrawRect(0.5f, yTop + 0.5f, TableWidth - 1f, height - 1f, border);

        float x1 = XName, x2 = XPkt, x3 = XMwyzej, x4 = XDolidera, x5 = XPoprawne;
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
        canvas.DrawText(dl, XDolidera + (XPoprawne - XDolidera - dlw) / 2f, baseline, font, paint);

        string pop = row.PoprawneTypy.ToString();
        float popw = font.MeasureText(pop);
        canvas.DrawText(pop, XPoprawne + (XEnd - XPoprawne - popw) / 2f, baseline, font, paint);
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
        using var paint = new SKPaint { Color = FooterBg, Style = SKPaintStyle.Fill, IsAntialias = true };
        canvas.DrawRect(0, y, TableWidth, FooterHeight, paint);

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
            list.Add(new StandingsRow
            {
                PlayerName = player.DiscordUsername,
                TotalPoints = scored.Sum(s => s.Points),
                PoprawneTypy = scored.Count(s => s.Points > 0)
            });
        }

        list.Sort((a, b) =>
        {
            int c = b.TotalPoints.CompareTo(a.TotalPoints);
            return c != 0 ? c : b.PoprawneTypy.CompareTo(a.PoprawneTypy);
        });
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
            list.Add(new StandingsRow
            {
                PlayerName = player.DiscordUsername,
                TotalPoints = scored.Sum(s => s.Points),
                PoprawneTypy = scored.Count(s => s.Points > 0)
            });
        }

        list.Sort((a, b) =>
        {
            int c = b.TotalPoints.CompareTo(a.TotalPoints);
            return c != 0 ? c : b.PoprawneTypy.CompareTo(a.PoprawneTypy);
        });
        return list;
    }

    private sealed class StandingsRow
    {
        public string PlayerName { get; set; } = string.Empty;
        public int TotalPoints { get; set; }
        public int PoprawneTypy { get; set; }
        public int? DisplayRank { get; set; }
        public string? GapToRowAbove { get; set; }
        public string? GapToLeader { get; set; }
    }
}
