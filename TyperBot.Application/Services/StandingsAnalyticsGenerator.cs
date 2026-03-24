using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG: tabele punktów mecz/kolejka z deltą, histogram rozkładu punktów w całym sezonie, wykres skumulowanych punktów (cały sezon), wykres kołowy rozkładu punktów jednego gracza.
/// Bez emoji w grafice.
/// </summary>
public sealed class StandingsAnalyticsGenerator
{
    private const int DeltaTableWidth = 820;
    private const int RowHeight = 38;
    private const int TitleBarHeight = 52;
    private const int HeaderHeight = 48;
    private const int FooterHeight = 36;

    private const int ChartWidth = 1100;
    private const int ChartHeight = 720;
    private const int ChartMarginLeft = 56;
    private const int ChartMarginRight = 220;
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
    private static readonly SKColor RowA = new(0xEE, 0xEE, 0xF0);
    private static readonly SKColor RowB = new(0xE0, 0xE0, 0xE6);
    private static readonly SKColor Border = SKColors.Black;
    private static readonly SKColor FooterBg = new(0x2A, 0x2A, 0x2E);
    private static readonly SKColor FooterText = new(0xB0, 0xB8, 0xC0);
    private static readonly SKColor ChartBg = new(0xFA, 0xFA, 0xFC);
    private static readonly SKColor GridColor = new(0xCC, 0xCC, 0xD5);
    private static readonly SKColor RoundBand = new(0xE8, 0xEC, 0xF8);

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

    public static List<AnalyticsDeltaRow> BuildMatchDeltaRows(Match target, Match? previous, List<Player> players)
    {
        var list = new List<AnalyticsDeltaRow>();
        foreach (var p in players.Where(x => x.IsActive))
        {
            var cur = PointsInMatch(p, target.Id);
            int? prevPts = previous == null ? null : PointsInMatch(p, previous.Id);
            var delta = prevPts.HasValue ? cur - prevPts.Value : (int?)null;
            list.Add(new AnalyticsDeltaRow(p.DiscordUsername, cur, delta));
        }

        list.Sort((a, b) =>
        {
            var c = b.PointsCurrent.CompareTo(a.PointsCurrent);
            return c != 0 ? c : string.Compare(a.PlayerName, b.PlayerName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
    }

    public static List<AnalyticsDeltaRow> BuildRoundDeltaRows(Round target, Round? previousRound, List<Player> players)
    {
        var list = new List<AnalyticsDeltaRow>();
        foreach (var p in players.Where(x => x.IsActive))
        {
            var cur = PointsInRound(p, target);
            int? prevPts = previousRound == null ? null : PointsInRound(p, previousRound);
            var delta = prevPts.HasValue ? cur - prevPts.Value : (int?)null;
            list.Add(new AnalyticsDeltaRow(p.DiscordUsername, cur, delta));
        }

        list.Sort((a, b) =>
        {
            var c = b.PointsCurrent.CompareTo(a.PointsCurrent);
            return c != 0 ? c : string.Compare(a.PlayerName, b.PlayerName, StringComparison.OrdinalIgnoreCase);
        });
        return list;
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

        using var surface = SKSurface.Create(new SKImageInfo(DeltaTableWidth, h));
        var c = surface.Canvas;
        c.Clear(RowA);

        using (var p = new SKPaint { Color = TitleBg, IsAntialias = true })
            c.DrawRect(0, 0, DeltaTableWidth, TitleBarHeight, p);
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

        using (var p = new SKPaint { Color = HeaderBg, IsAntialias = true })
            c.DrawRect(0, TitleBarHeight, DeltaTableWidth, HeaderHeight, p);

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

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private static void DrawDeltaTableGrid(SKCanvas c, float titleH, float headH, float dataY, float dataH, float x1, float x2, float x3)
    {
        float yTop = titleH;
        float yBot = dataY + dataH;
        using var pen = new SKPaint { Color = Border, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };
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
        using var bg = new SKPaint { Color = FooterBg, IsAntialias = true };
        c.DrawRect(0, y, DeltaTableWidth, FooterHeight, bg);
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

    public byte[] GenerateSeasonCumulativeChartPng(Season season, List<Player> players)
    {
        var points = OrderFinishedMatchesWithRoundNumbers(season);
        var active = players.Where(p => p.IsActive).OrderBy(p => p.DiscordUsername).ToList();

        using var surface = SKSurface.Create(new SKImageInfo(ChartWidth, ChartHeight));
        var c = surface.Canvas;
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

        // Pasma kolejek (na przemian)
        int segStart = 0;
        for (var j = 1; j <= points.Count; j++)
        {
            var endSeg = j == points.Count || points[j].RoundNumber != points[segStart].RoundNumber;
            if (!endSeg) continue;
            float xa = MatchX(segStart);
            float xb = MatchX(j - 1);
            var bandColor = points[segStart].RoundNumber % 2 == 1 ? RoundBand : new SKColor(0xF2, 0xF2, 0xF6);
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

        using (var axis = new SKPaint { Color = Border, StrokeWidth = 1.25f, Style = SKPaintStyle.Stroke, IsAntialias = true })
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
            colors.Add(HueColor(pi, active.Count));

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
                StrokeWidth = active.Count > 20 ? 1.5f : 2.25f,
                IsAntialias = true
            };
            c.DrawPath(path, stroke);
        }

        // Etykiety nicków przy ostatnim punkcie — przy tym samym wyniku układane pionowo
        var endNickRows = new List<(int PointsTotal, string Nick, SKColor Color)>();
        for (var pi = 0; pi < active.Count; pi++)
        {
            var pl = active[pi];
            if (!cumByPlayer.TryGetValue(pl.Id, out var cum)) continue;
            string nick = Ellipsize(pl.DiscordUsername, legFont, 140f);
            endNickRows.Add((cum[^1], nick, colors[pi]));
        }

        float lineStep = legFont.Size * 1.22f;
        float minY = plotT + legFont.Size + 2f;
        float maxYText = plotB - 4f;
        foreach (var grp in endNickRows.GroupBy(r => r.PointsTotal).OrderBy(g => g.Key))
        {
            var ordered = grp.OrderBy(r => r.Nick, StringComparer.OrdinalIgnoreCase).ToList();
            int n = ordered.Count;
            float yNorm = grp.Key / (float)maxY;
            float baseLine = plotB - yNorm * plotH + 4f;
            float totalStack = (n - 1) * lineStep;
            float startY = baseLine - totalStack / 2f;
            if (startY < minY)
                startY = minY;
            if (n > 1 && startY + totalStack > maxYText)
                startY = Math.Max(minY, maxYText - totalStack);
            for (var i = 0; i < n; i++)
            {
                float yText = startY + i * lineStep;
                yText = Math.Clamp(yText, minY, maxYText);
                var row = ordered[i];
                using var np = new SKPaint { Color = row.Color, IsAntialias = true };
                c.DrawText(row.Nick, labelX, yText, legFont, np);
            }
        }

        // Legend column
        float legX = plotR + 12f;
        float legY = plotT;
        using var legTitle = new SKFont(Bold(), 11f);
        c.DrawText("Legenda:", legX, legY, legTitle, black);
        legY += 20f;
        for (var pi = 0; pi < active.Count; pi++)
        {
            using var sq = new SKPaint { Color = colors[pi], IsAntialias = true };
            c.DrawRect(legX, legY - 8f, 12f, 8f, sq);
            string nm = Ellipsize(active[pi].DiscordUsername, legFont, ChartMarginRight - 36f);
            c.DrawText(nm, legX + 18f, legY, legFont, black);
            legY += 14f;
            if (legY > plotB - 8f) break;
        }

        string foot = $"{season.Name}  •  Cały sezon  •  Mecze: {points.Count}  •  Gracze: {active.Count}";
        using var ff = new SKFont(Body(), 10f);
        using var fp = new SKPaint { Color = FooterText, IsAntialias = true };
        float fw = ff.MeasureText(foot);
        c.DrawText(foot, (ChartWidth - fw) / 2f, ChartHeight - 14f, ff, fp);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
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

    public static List<PointsHistogramRow> BuildSeasonPointsHistogramRows(
        List<Player> players,
        HashSet<int> finishedMatchIds,
        IReadOnlyList<int> columns)
    {
        var list = new List<PointsHistogramRow>();
        foreach (var pl in players.Where(p => p.IsActive).OrderBy(p => p.DiscordUsername))
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

            list.Add(new PointsHistogramRow(pl.DiscordUsername, counts));
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
                player.DiscordUsername,
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
                player.DiscordUsername,
                "Brak zaliczonych typów w zakończonych meczach tego sezonu.");

        return RenderPlayerPointsPie(season.Name, player.DiscordUsername, slices, total, matchIds.Count);
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

        using var surface = SKSurface.Create(new SKImageInfo(tableW, h));
        var c = surface.Canvas;
        c.Clear(RowA);

        using (var p = new SKPaint { Color = TitleBg, IsAntialias = true })
            c.DrawRect(0, 0, tableW, TitleBarHeight, p);
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

        using (var hp = new SKPaint { Color = HeaderBg, IsAntialias = true })
            c.DrawRect(0, TitleBarHeight, tableW, HeaderHeight, hp);

        using var fH = new SKFont(Bold(), 9f);
        using var fHdr = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var pen = new SKPaint { Color = Border, Style = SKPaintStyle.Stroke, StrokeWidth = 1, IsAntialias = false };

        float xName = 0f;
        float x0 = HistogramNameCol;
        c.DrawRect(0.5f, TitleBarHeight + 0.5f, tableW - 1f, HeaderHeight + n * RowHeight, pen);
        c.DrawLine(x0, TitleBarHeight, x0, TitleBarHeight + HeaderHeight + n * RowHeight, pen);

        DrawCellCentered(c, "UCZESTNIK", xName, TitleBarHeight, x0 - xName, HeaderHeight, fH, fHdr);

        for (var col = 0; col < columns.Count; col++)
        {
            float xc = x0 + col * colW;
            string lab = columns[col].ToString();
            DrawCellCentered(c, lab, xc, TitleBarHeight, colW, HeaderHeight, fH, fHdr);
            c.DrawLine(xc + colW, TitleBarHeight, xc + colW, TitleBarHeight + HeaderHeight + n * RowHeight, pen);
        }

        using var fBody = new SKFont(Body(), 12f);
        using var txt = new SKPaint { Color = new SKColor(0x12, 0x12, 0x18), IsAntialias = true };

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
                    string cell = row.CountsByColumnIndex[col].ToString();
                    float cw = fBody.MeasureText(cell);
                    c.DrawText(cell, xc + (colW - cw) / 2f, y + RowHeight / 2f + 5f, fBody, txt);
                }
            }
        }

        c.DrawLine(0, TitleBarHeight + HeaderHeight, tableW, TitleBarHeight + HeaderHeight, pen);

        DrawHistogramFooter(c, tableW, h, footer);

        using var img = surface.Snapshot();
        using var enc = img.Encode(SKEncodedImageFormat.Png, 100);
        return enc.ToArray();
    }

    private static void DrawHistogramFooter(SKCanvas c, int tableW, int totalH, string text)
    {
        float y = totalH - FooterHeight;
        using var bg = new SKPaint { Color = FooterBg, IsAntialias = true };
        c.DrawRect(0, y, tableW, FooterHeight, bg);
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
        int h = TitleBarHeight + 120 + FooterHeight;
        using var surface = SKSurface.Create(new SKImageInfo(PieChartWidth, h));
        var c = surface.Canvas;
        c.Clear(ChartBg);

        using (var p = new SKPaint { Color = TitleBg, IsAntialias = true })
            c.DrawRect(0, 0, PieChartWidth, TitleBarHeight, p);
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

        using var fMsg = new SKFont(Body(), 13f);
        using var blk = new SKPaint { Color = new SKColor(0x28, 0x28, 0x32), IsAntialias = true };
        float mw = fMsg.MeasureText(message);
        c.DrawText(message, (PieChartWidth - mw) / 2f, TitleBarHeight + 70f, fMsg, blk);

        DrawHistogramFooter(c, PieChartWidth, h, "Tylko Ty widzisz ten obrazek.");

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
        using var surface = SKSurface.Create(new SKImageInfo(PieChartWidth, PieChartHeight));
        var c = surface.Canvas;
        c.Clear(ChartBg);

        using (var p = new SKPaint { Color = TitleBg, IsAntialias = true })
            c.DrawRect(0, 0, PieChartWidth, TitleBarHeight, p);
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
            var col = HueColor(si, Math.Max(n, 3));
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
        for (var si = 0; si < n; si++)
        {
            var (pts, cnt) = slices[si];
            var col = HueColor(si, Math.Max(n, 3));
            using var sq = new SKPaint { Color = col, IsAntialias = true };
            c.DrawRect(legX, legY - 9f, 12f, 10f, sq);
            float pct = 100f * cnt / totalPredictions;
            string legLine = $"{pts} pkt: {cnt}× ({pct:0.#}%)";
            legLine = Ellipsize(legLine, legBody, PieLegendWidth - 28f);
            c.DrawText(legLine, legX + 18f, legY, legBody, black);
            legY += 16f;
            if (legY > plotBottom - 4f) break;
        }

        string foot =
            $"{seasonName}  •  Zakończonych meczów w sezonie: {finishedMatchesInSeason}  •  Twych typów w nich: {totalPredictions}  •  Cały sezon";
        using var ff = new SKFont(Body(), 9f);
        using var fp = new SKPaint { Color = FooterText, IsAntialias = true };
        string footLine = foot;
        while (footLine.Length > 16 && ff.MeasureText(footLine + "…") > PieChartWidth - 16f)
            footLine = footLine[..^1];
        if (footLine != foot) footLine += "…";
        float fw = ff.MeasureText(footLine);
        c.DrawText(footLine, (PieChartWidth - fw) / 2f, PieChartHeight - 14f, ff, fp);

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
                Color = SKColors.White,
                IsAntialias = true,
                Style = SKPaintStyle.Stroke,
                StrokeWidth = 2f
            };
            canvas.DrawOval(oval, edge);
            return;
        }

        using var path = new SKPath();
        path.MoveTo(cx, cy);
        path.AddArc(oval, startAngleDeg, sweepAngleDeg);
        path.Close();
        using var sliceFill = new SKPaint { Color = fill, IsAntialias = true, Style = SKPaintStyle.Fill };
        canvas.DrawPath(path, sliceFill);
        using var sliceEdge = new SKPaint
        {
            Color = SKColors.White,
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = 2f
        };
        canvas.DrawPath(path, sliceEdge);
    }

    private static SKColor HueColor(int index, int total)
    {
        double hue = (index * 360.0 / Math.Max(1, Math.Min(total, 24))) + (index * 47 % 17);
        hue %= 360;
        return SKColor.FromHsv((float)hue, 0.72f, 0.92f);
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

public sealed record PointsHistogramRow(string PlayerName, int[] CountsByColumnIndex);
