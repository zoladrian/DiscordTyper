using SkiaSharp;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;

namespace TyperBot.Application.Services;

/// <summary>
/// PNG tables aligned with <c>AdminModule</c> text/embed tables: Poz, Gracz, Pkt, Typ, Cel, Wyg (same formulas and sort).
/// </summary>
public class TableGenerator
{
    private const int TableWidth = 1040;
    private const int RowHeight = 42;
    private const int TitleBarHeight = 64;
    private const int ColumnHeaderHeight = 38;
    private const int FooterHeight = 44;
    private const int PaddingH = 22;

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
            title: $"🏆 Tabela sezonu — {season.Name}",
            subtitle: null,
            rows,
            footer: $"Typ = typy w sezonie  •  Pkt / Cel / Wyg = tylko mecze z wynikiem  •  {players.Count(p => p.IsActive)} graczy");
    }

    public byte[] GenerateRoundTable(Season season, Round round, List<Player> players)
    {
        var rows = CalculateRoundRows(players, round);
        var roundLabel = RoundHelper.GetRoundLabel(round.Number);
        var matchCount = round.Matches?.Count ?? 0;
        var finished = round.Matches?.Count(m => m.Status == MatchStatus.Finished || m.Status == MatchStatus.Cancelled) ?? 0;
        return RenderPng(
            title: $"📊 {roundLabel} — {season.Name}",
            subtitle: round.Description,
            rows,
            footer: $"Typ = typy w tej kolejce  •  Pkt / Cel / Wyg = tylko mecze z wynikiem  •  Mecze: {finished}/{matchCount}");
    }

    private byte[] RenderPng(string title, string? subtitle, List<StandingsRow> rows, string footer)
    {
        int dataRows = rows.Count == 0 ? 1 : rows.Count;
        int totalHeight = TitleBarHeight + ColumnHeaderHeight + dataRows * RowHeight + FooterHeight;

        using var surface = SKSurface.Create(new SKImageInfo(TableWidth, totalHeight));
        var canvas = surface.Canvas;
        canvas.Clear(new SKColor(0x16, 0x18, 0x1C));

        DrawTitleBar(canvas, title, subtitle, TableWidth, TitleBarHeight);
        DrawColumnHeaders(canvas, TitleBarHeight, TableWidth);

        int yBase = TitleBarHeight + ColumnHeaderHeight;
        if (rows.Count == 0)
            DrawNoDataRow(canvas, yBase);
        else
        {
            for (int i = 0; i < rows.Count; i++)
            {
                int y = yBase + i * RowHeight;
                DrawDataRow(canvas, y, i + 1, rows[i], rows.Count, i % 2 == 1);
            }
        }

        DrawFooter(canvas, totalHeight, footer, TableWidth);

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void DrawTitleBar(SKCanvas canvas, string title, string? subtitle, int width, int height)
    {
        using var shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0),
            new SKPoint(width, height),
            new[] { new SKColor(0x3B, 0x4F, 0xA8), new SKColor(0x1E, 0x2A, 0x5E) },
            SKShaderTileMode.Clamp);
        using var grad = new SKPaint { Shader = shader, IsAntialias = true };
        canvas.DrawRect(0, 0, width, height, grad);

        using var titleTf = ResolveBoldTypeface();
        using var titleFont = new SKFont(titleTf, 22f);
        using var titlePaint = new SKPaint { Color = SKColors.White, IsAntialias = true };

        float titleY = string.IsNullOrEmpty(subtitle) ? height / 2f + 8f : height / 2f - 2f;
        float titleW = titleFont.MeasureText(title);
        canvas.DrawText(title, (width - titleW) / 2f, titleY, titleFont, titlePaint);

        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            using var subTf = ResolveBodyTypeface();
            using var subFont = new SKFont(subTf, 14f);
            using var subPaint = new SKPaint { Color = new SKColor(220, 225, 240), IsAntialias = true };
            float sw = subFont.MeasureText(subtitle);
            canvas.DrawText(subtitle, (width - sw) / 2f, height / 2f + 18f, subFont, subPaint);
        }
    }

    private static void DrawColumnHeaders(SKCanvas canvas, int yTop, int width)
    {
        using var bg = new SKPaint { Color = new SKColor(0x22, 0x26, 0x2E), IsAntialias = true };
        canvas.DrawRect(0, yTop, width, ColumnHeaderHeight, bg);

        using var line = new SKPaint { Color = new SKColor(0x3D, 0x45, 0x55), StrokeWidth = 1 };
        canvas.DrawLine(0, yTop + ColumnHeaderHeight - 0.5f, width, yTop + ColumnHeaderHeight - 0.5f, line);

        using var tf = ResolveBoldTypeface();
        using var font = new SKFont(tf, 13f);
        using var paint = new SKPaint { Color = new SKColor(0xB8, 0xC5, 0xDC), IsAntialias = true };

        float baseline = yTop + ColumnHeaderHeight / 2f + 5f;
        var cols = GetColumnLayout(width);
        float pozW = font.MeasureText("Poz");
        canvas.DrawText("Poz", cols.PozCenter - pozW / 2f, baseline, font, paint);
        canvas.DrawText("Gracz", cols.NameLeft, baseline, font, paint);
        DrawRightAligned(canvas, "Pkt", cols.PktRight, baseline, font, paint);
        DrawRightAligned(canvas, "Typ", cols.TypRight, baseline, font, paint);
        DrawRightAligned(canvas, "Cel", cols.CelRight, baseline, font, paint);
        DrawRightAligned(canvas, "Wyg", cols.WygRight, baseline, font, paint);
    }

    private readonly struct ColumnLayout
    {
        public float PozCenter { get; init; }
        public float NameLeft { get; init; }
        public float NameMaxWidth { get; init; }
        public float PktRight { get; init; }
        public float TypRight { get; init; }
        public float CelRight { get; init; }
        public float WygRight { get; init; }
    }

    private static ColumnLayout GetColumnLayout(int width)
    {
        float right = width - PaddingH;
        float wygR = right;
        float celR = right - 72f;
        float typR = celR - 72f;
        float pktR = typR - 72f;
        float nameLeft = PaddingH + 78f;
        float nameMax = pktR - nameLeft - 24f;
        return new ColumnLayout
        {
            PozCenter = PaddingH + 26f,
            NameLeft = nameLeft,
            NameMaxWidth = Math.Max(120f, nameMax),
            PktRight = pktR,
            TypRight = typR,
            CelRight = celR,
            WygRight = wygR
        };
    }

    private static void DrawDataRow(SKCanvas canvas, int y, int rank, StandingsRow row, int totalPlayers, bool alternate)
    {
        var bg = alternate ? new SKColor(0x1C, 0x20, 0x28) : new SKColor(0x18, 0x1B, 0x22);
        using var bgPaint = new SKPaint { Color = bg };
        canvas.DrawRect(0, y, TableWidth, RowHeight, bgPaint);

        var cols = GetColumnLayout(TableWidth);
        using var bodyTf = ResolveBodyTypeface();
        using var bodyFont = new SKFont(bodyTf, 15f);
        using var white = new SKPaint { Color = SKColors.White, IsAntialias = true };
        using var muted = new SKPaint { Color = new SKColor(0xC0, 0xC8, 0xD8), IsAntialias = true };

        float baseline = y + RowHeight / 2f + 5f;

        string posCol = (totalPlayers > 1 && rank == totalPlayers)
            ? $"💩 {rank,2}"
            : rank switch
            {
                1 => $"🥇 {rank,2}",
                2 => $"🥈 {rank,2}",
                3 => $"🥉 {rank,2}",
                _ => $"   {rank,2}"
            };
        canvas.DrawText(posCol, PaddingH, baseline, bodyFont, white);

        string name = EllipsizeName(row.PlayerName, bodyFont, cols.NameMaxWidth);
        canvas.DrawText(name, cols.NameLeft, baseline, bodyFont, white);

        DrawRightAligned(canvas, row.TotalPoints.ToString(), cols.PktRight, baseline, bodyFont, white);
        DrawRightAligned(canvas, row.Typ.ToString(), cols.TypRight, baseline, bodyFont, muted);
        DrawRightAligned(canvas, row.Cel.ToString(), cols.CelRight, baseline, bodyFont, muted);
        DrawRightAligned(canvas, row.Wyg.ToString(), cols.WygRight, baseline, bodyFont, muted);
    }

    private static void DrawNoDataRow(SKCanvas canvas, int y)
    {
        using var bgPaint = new SKPaint { Color = new SKColor(0x18, 0x1B, 0x22) };
        canvas.DrawRect(0, y, TableWidth, RowHeight, bgPaint);
        using var tf = ResolveBodyTypeface();
        using var font = new SKFont(tf, 15f);
        using var paint = new SKPaint { Color = new SKColor(0x88, 0x90, 0xA0), IsAntialias = true };
        const string msg = "Brak danych do wyświetlenia";
        float w = font.MeasureText(msg);
        canvas.DrawText(msg, (TableWidth - w) / 2f, y + RowHeight / 2f + 5f, font, paint);
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

    private static void DrawRightAligned(SKCanvas canvas, string text, float rightX, float baseline, SKFont font, SKPaint paint)
    {
        float w = font.MeasureText(text);
        canvas.DrawText(text, rightX - w, baseline, font, paint);
    }

    private static void DrawFooter(SKCanvas canvas, int totalHeight, string footerText, int width)
    {
        using var paint = new SKPaint
        {
            Color = new SKColor(0x24, 0x28, 0x32),
            Style = SKPaintStyle.Fill,
            IsAntialias = true
        };
        canvas.DrawRect(0, totalHeight - FooterHeight, width, FooterHeight, paint);

        using var tf = ResolveBodyTypeface();
        using var font = new SKFont(tf, 12f);
        using var textPaint = new SKPaint { Color = new SKColor(0x98, 0xA4, 0xB8), IsAntialias = true };

        string line = footerText;
        if (font.MeasureText(line) > width - PaddingH * 2)
        {
            while (line.Length > 20 && font.MeasureText(line + "…") > width - PaddingH * 2)
                line = line[..^1];
            line += "…";
        }

        float w = font.MeasureText(line);
        canvas.DrawText(line, (width - w) / 2f, totalHeight - FooterHeight / 2f + 4f, font, textPaint);
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
                Typ = predsInSeason.Count,
                Cel = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50),
                Wyg = scored.Count(s => s.Points > 0)
            });
        }

        list.Sort((a, b) =>
        {
            int c = b.TotalPoints.CompareTo(a.TotalPoints);
            return c != 0 ? c : b.Typ.CompareTo(a.Typ);
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
                Typ = predsInRound.Count,
                Cel = scored.Count(s => s.Bucket == Bucket.P35 || s.Bucket == Bucket.P50),
                Wyg = scored.Count(s => s.Points > 0)
            });
        }

        list.Sort((a, b) =>
        {
            int c = b.TotalPoints.CompareTo(a.TotalPoints);
            return c != 0 ? c : b.Typ.CompareTo(a.Typ);
        });
        return list;
    }

    private sealed class StandingsRow
    {
        public string PlayerName { get; set; } = string.Empty;
        public int TotalPoints { get; set; }
        public int Typ { get; set; }
        public int Cel { get; set; }
        public int Wyg { get; set; }
    }
}
