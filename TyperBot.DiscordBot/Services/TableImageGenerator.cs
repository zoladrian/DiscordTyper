using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Drawing.Processing;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SixLabors.ImageSharp.Drawing;
using SixLabors.Fonts;
using Microsoft.Extensions.Logging;

namespace TyperBot.DiscordBot.Services;

public class TableImageGenerator
{
    private readonly ILogger<TableImageGenerator> _logger;
    
    // Colors
    private static readonly Color HeaderGradientStart = Color.ParseHex("#667eea");
    private static readonly Color HeaderGradientEnd = Color.ParseHex("#764ba2");
    private static readonly Color RowEven = Color.ParseHex("#2c3e50");
    private static readonly Color RowOdd = Color.ParseHex("#34495e");
    private static readonly Color TextWhite = Color.White;
    private static readonly Color TextGray = Color.ParseHex("#bdc3c7");
    private static readonly Color BorderColor = Color.ParseHex("#95a5a6");
    private static readonly Color HighlightGold = Color.ParseHex("#f1c40f");
    private static readonly Color HighlightSilver = Color.ParseHex("#95a5a6");
    private static readonly Color HighlightBronze = Color.ParseHex("#cd7f32");

    public TableImageGenerator(ILogger<TableImageGenerator> logger)
    {
        _logger = logger;
    }

    public class PlayerRow
    {
        public int Position { get; set; }
        public string PlayerName { get; set; } = string.Empty;
        public int TotalPoints { get; set; }
        public int PredictionsCount { get; set; }
        public int BullseyeCount { get; set; }
        public int WinsCount { get; set; }
    }

    public async Task<Stream> GenerateSeasonTableAsync(string seasonName, List<PlayerRow> players)
    {
        try
        {
            // Dimensions
            const int width = 1200;
            const int headerHeight = 80;
            const int rowHeight = 50;
            const int padding = 20;
            int totalHeight = headerHeight + (players.Count * rowHeight) + (padding * 2);

            // Create image
            using var image = new Image<Rgba32>(width, totalHeight);

            // Load font (fallback to system default if not available)
            var fontCollection = new FontCollection();
            Font headerFont;
            Font bodyFont;
            Font bodyFontBold;

            try
            {
                // Try to use Segoe UI (common on Windows)
                var family = SystemFonts.Get("Segoe UI");
                headerFont = family.CreateFont(24, FontStyle.Bold);
                bodyFont = family.CreateFont(18, FontStyle.Regular);
                bodyFontBold = family.CreateFont(18, FontStyle.Bold);
            }
            catch
            {
                // Fallback to Arial
                var family = SystemFonts.Get("Arial");
                headerFont = family.CreateFont(24, FontStyle.Bold);
                bodyFont = family.CreateFont(18, FontStyle.Regular);
                bodyFontBold = family.CreateFont(18, FontStyle.Bold);
            }

            image.Mutate(ctx =>
            {
                // Background
                ctx.Fill(Color.ParseHex("#1a1a2e"));

                // Header with gradient
                var headerRect = new RectangleF(padding, padding, width - (padding * 2), headerHeight);
                var headerBrush = new LinearGradientBrush(
                    new PointF(padding, padding),
                    new PointF(width - padding, padding),
                    GradientRepetitionMode.None,
                    new ColorStop(0, HeaderGradientStart),
                    new ColorStop(1, HeaderGradientEnd)
                );
                ctx.Fill(headerBrush, headerRect);

                // Header text
                var titleOptions = new RichTextOptions(headerFont)
                {
                    Origin = new PointF(width / 2, padding + (headerHeight / 2)),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                };
                ctx.DrawText(titleOptions, $"🏆 {seasonName}", TextWhite);

                // Column headers
                int yPos = padding + headerHeight + 15;
                var columnHeaderOptions = new RichTextOptions(bodyFontBold)
                {
                    Origin = new PointF(padding + 50, yPos),
                    VerticalAlignment = VerticalAlignment.Center
                };

                ctx.DrawText(columnHeaderOptions, "Poz", TextGray);
                columnHeaderOptions.Origin = new PointF(padding + 150, yPos);
                ctx.DrawText(columnHeaderOptions, "Gracz", TextGray);
                columnHeaderOptions.Origin = new PointF(padding + 600, yPos);
                ctx.DrawText(columnHeaderOptions, "Pkt", TextGray);
                columnHeaderOptions.Origin = new PointF(padding + 750, yPos);
                ctx.DrawText(columnHeaderOptions, "Typ", TextGray);
                columnHeaderOptions.Origin = new PointF(padding + 900, yPos);
                ctx.DrawText(columnHeaderOptions, "🎯 Cel", TextGray);
                columnHeaderOptions.Origin = new PointF(padding + 1050, yPos);
                ctx.DrawText(columnHeaderOptions, "💪 Wyg", TextGray);

                yPos += 10;

                // Draw rows
                for (int i = 0; i < players.Count; i++)
                {
                    var player = players[i];
                    yPos += rowHeight;

                    // Row background (alternating colors)
                    var rowColor = i % 2 == 0 ? RowEven : RowOdd;
                    var rowRect = new RectangleF(padding, yPos - 25, width - (padding * 2), rowHeight);
                    ctx.Fill(rowColor, rowRect);

                    // Highlight top 3
                    if (player.Position <= 3)
                    {
                        var highlightColor = player.Position switch
                        {
                            1 => HighlightGold,
                            2 => HighlightSilver,
                            3 => HighlightBronze,
                            _ => BorderColor
                        };
                        var borderRect = new RectangleF(padding, yPos - 25, 5, rowHeight);
                        ctx.Fill(highlightColor, borderRect);
                    }

                    var textOptions = new RichTextOptions(bodyFont)
                    {
                        Origin = new PointF(padding + 50, yPos),
                        VerticalAlignment = VerticalAlignment.Center
                    };

                    // Position with medal emoji
                    var positionText = player.Position switch
                    {
                        1 => "🥇",
                        2 => "🥈",
                        3 => "🥉",
                        _ => player.Position.ToString()
                    };
                    ctx.DrawText(textOptions, positionText, TextWhite);

                    // Player name
                    textOptions.Origin = new PointF(padding + 150, yPos);
                    var nameFont = player.Position <= 3 ? bodyFontBold : bodyFont;
                    textOptions.Font = nameFont;
                    ctx.DrawText(textOptions, player.PlayerName, TextWhite);

                    // Points (bold and highlighted)
                    textOptions.Origin = new PointF(padding + 600, yPos);
                    textOptions.Font = bodyFontBold;
                    var pointsColor = player.TotalPoints > 50 ? Color.ParseHex("#2ecc71") : 
                                      player.TotalPoints > 20 ? Color.ParseHex("#f39c12") : 
                                      Color.ParseHex("#e74c3c");
                    ctx.DrawText(textOptions, player.TotalPoints.ToString(), pointsColor);

                    // Stats
                    textOptions.Font = bodyFont;
                    textOptions.Origin = new PointF(padding + 750, yPos);
                    ctx.DrawText(textOptions, player.PredictionsCount.ToString(), TextWhite);
                    
                    textOptions.Origin = new PointF(padding + 900, yPos);
                    ctx.DrawText(textOptions, player.BullseyeCount.ToString(), TextWhite);
                    
                    textOptions.Origin = new PointF(padding + 1050, yPos);
                    ctx.DrawText(textOptions, player.WinsCount.ToString(), TextWhite);
                }

                // Footer border
                var footerLine = new RectangleF(padding, yPos + 35, width - (padding * 2), 2);
                ctx.Fill(BorderColor, footerLine);
            });

            // Save to stream
            var stream = new MemoryStream();
            await image.SaveAsPngAsync(stream);
            stream.Position = 0;
            return stream;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating table image");
            throw;
        }
    }

    public async Task<Stream> GenerateRoundTableAsync(string seasonName, int roundNumber, List<PlayerRow> players)
    {
        // Similar to season table, but with round-specific header
        var roundLabel = $"Kolejka {roundNumber}";
        return await GenerateSeasonTableAsync($"{seasonName} - {roundLabel}", players);
    }
}
