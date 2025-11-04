using Microsoft.Extensions.Logging;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class DemoDataSeeder
{
    private readonly ILogger<DemoDataSeeder> _logger;
    private readonly ISeasonRepository _seasonRepository;
    private readonly IRoundRepository _roundRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly IPredictionRepository _predictionRepository;
    private readonly IPlayerScoreRepository _playerScoreRepository;
    private readonly ScoreCalculator _scoreCalculator;

    public DemoDataSeeder(
        ILogger<DemoDataSeeder> logger,
        ISeasonRepository seasonRepository,
        IRoundRepository roundRepository,
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        IPredictionRepository predictionRepository,
        IPlayerScoreRepository playerScoreRepository,
        ScoreCalculator scoreCalculator)
    {
        _logger = logger;
        _seasonRepository = seasonRepository;
        _roundRepository = roundRepository;
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _predictionRepository = predictionRepository;
        _playerScoreRepository = playerScoreRepository;
        _scoreCalculator = scoreCalculator;
    }

    public async Task<SeedResult> SeedDemoDataAsync(CancellationToken ct = default)
    {
        _logger.LogInformation("Rozpoczynam tworzenie danych testowych...");

        var result = new SeedResult();

        // Check if demo season already exists
        var existingSeason = await _seasonRepository.GetActiveSeasonAsync();
        if (existingSeason != null && existingSeason.Name.Contains("Demo Season"))
        {
            _logger.LogInformation("Sezon testowy już istnieje, pomijam tworzenie danych");
            return result;
        }

        // Create demo season
        var season = new Season
        {
            Name = "Demo Season 2025",
            IsActive = true
        };
        season = await _seasonRepository.AddAsync(season);
        result.SeasonsCreated = 1;
        _logger.LogInformation("Utworzono sezon testowy: {SeasonName} (ID: {SeasonId})", season.Name, season.Id);

        // Create 5 rounds
        var rounds = new List<Round>();
        for (int i = 1; i <= 5; i++)
        {
            var round = new Round
            {
                SeasonId = season.Id,
                Number = i,
                Description = $"Demo Round {i}"
            };
            round = await _roundRepository.AddAsync(round);
            rounds.Add(round);
            result.RoundsCreated++;
        }
        _logger.LogInformation("Utworzono {Count} kolejek", result.RoundsCreated);

        // Create matches (3-4 per round, with dates in the near future)
        var baseDate = DateTime.UtcNow.AddDays(1);
        var teamNames = new[]
        {
            "Motor Lublin", "Włókniarz Częstochowa", "Fogo Unia Leszno",
            "Stal Gorzów", "Betard Sparta Wrocław", "KS Toruń",
            "Orzeł Łódź", "GKM Grudziądz"
        };

        var random = new Random(42); // Fixed seed for reproducibility
        var matchIndex = 0;
        var allMatches = new List<Match>();

        foreach (var round in rounds)
        {
            var matchesPerRound = random.Next(3, 5); // 3-4 matches per round
            for (int i = 0; i < matchesPerRound; i++)
            {
                var homeTeam = teamNames[matchIndex % teamNames.Length];
                var awayTeam = teamNames[(matchIndex + 1) % teamNames.Length];
                
                // Avoid same team playing against itself
                while (homeTeam == awayTeam)
                {
                    awayTeam = teamNames[(matchIndex + random.Next(1, teamNames.Length)) % teamNames.Length];
                }

                var matchDate = baseDate.AddDays(matchIndex * 2 + round.Number * 7);
                var matchTime = new[] { 18, 19, 20, 21 }[random.Next(4)]; // 18:00, 19:00, 20:00, or 21:00
                var startTime = new DateTimeOffset(matchDate.Year, matchDate.Month, matchDate.Day, matchTime, 0, 0, TimeSpan.Zero);

                var match = new Match
                {
                    RoundId = round.Id,
                    HomeTeam = homeTeam,
                    AwayTeam = awayTeam,
                    StartTime = startTime,
                    Status = matchIndex < 8 ? MatchStatus.Finished : MatchStatus.Scheduled // First 8 matches finished
                };

                // Set results for finished matches
                if (match.Status == MatchStatus.Finished)
                {
                    var homeScore = random.Next(40, 51);
                    var awayScore = 90 - homeScore;
                    match.HomeScore = homeScore;
                    match.AwayScore = awayScore;
                }

                match = await _matchRepository.AddAsync(match);
                allMatches.Add(match);
                result.MatchesCreated++;
                matchIndex++;
            }
        }
        _logger.LogInformation("Utworzono {Count} meczów", result.MatchesCreated);

        // Create 5 demo players
        var playerNames = new[]
        {
            ("DemoPlayer1", 100000000000000001UL),
            ("DemoPlayer2", 100000000000000002UL),
            ("DemoPlayer3", 100000000000000003UL),
            ("DemoPlayer4", 100000000000000004UL),
            ("DemoPlayer5", 100000000000000005UL)
        };

        var players = new List<Player>();
        foreach (var (name, discordId) in playerNames)
        {
            var player = new Player
            {
                DiscordUserId = discordId,
                DiscordUsername = name,
                IsActive = true
            };
            player = await _playerRepository.AddAsync(player);
            players.Add(player);
            result.PlayersCreated++;
        }
        _logger.LogInformation("Utworzono {Count} graczy", result.PlayersCreated);

        // Create predictions for all matches (each player predicts for most matches)
        var finishedMatches = allMatches.Where(m => m.Status == MatchStatus.Finished).ToList();
        var scheduledMatches = allMatches.Where(m => m.Status == MatchStatus.Scheduled).ToList();

        foreach (var player in players)
        {
            // Predict for all finished matches
            foreach (var match in finishedMatches)
            {
                var homeTip = random.Next(40, 51);
                var awayTip = 90 - homeTip;
                
                var prediction = new Prediction
                {
                    MatchId = match.Id,
                    PlayerId = player.Id,
                    HomeTip = homeTip,
                    AwayTip = awayTip,
                    CreatedAt = match.StartTime.AddHours(-random.Next(1, 24)), // Created before match start
                    IsValid = true
                };
                prediction = await _predictionRepository.AddAsync(prediction);
                result.PredictionsCreated++;

                // Calculate and create score for finished matches
                if (match.HomeScore.HasValue && match.AwayScore.HasValue)
                {
                    var (points, bucket) = _scoreCalculator.CalculateScore(
                        match.HomeScore.Value,
                        match.AwayScore.Value,
                        homeTip,
                        awayTip
                    );

                    var playerScore = new PlayerScore
                    {
                        PredictionId = prediction.Id,
                        Points = points,
                        Bucket = bucket
                    };
                    await _playerScoreRepository.AddAsync(playerScore);
                    result.ScoresCreated++;
                }
            }

            // Predict for some scheduled matches (not all players predict for all)
            foreach (var match in scheduledMatches)
            {
                if (random.Next(100) < 70) // 70% chance to predict
                {
                    var homeTip = random.Next(40, 51);
                    var awayTip = 90 - homeTip;
                    
                    var prediction = new Prediction
                    {
                        MatchId = match.Id,
                        PlayerId = player.Id,
                        HomeTip = homeTip,
                        AwayTip = awayTip,
                        CreatedAt = DateTimeOffset.UtcNow.AddHours(-random.Next(1, 48)),
                        IsValid = true
                    };
                    await _predictionRepository.AddAsync(prediction);
                    result.PredictionsCreated++;
                }
            }
        }
        _logger.LogInformation("Utworzono {Count} typów", result.PredictionsCreated);
        _logger.LogInformation("Utworzono {Count} wyników punktowych", result.ScoresCreated);

        _logger.LogInformation(
            "Tworzenie danych testowych zakończone - Sezony: {Seasons}, Kolejki: {Rounds}, Mecze: {Matches}, Gracze: {Players}, Typy: {Predictions}, Wyniki punktowe: {Scores}",
            result.SeasonsCreated,
            result.RoundsCreated,
            result.MatchesCreated,
            result.PlayersCreated,
            result.PredictionsCreated,
            result.ScoresCreated);

        return result;
    }

    public class SeedResult
    {
        public int SeasonsCreated { get; set; }
        public int RoundsCreated { get; set; }
        public int MatchesCreated { get; set; }
        public int PlayersCreated { get; set; }
        public int PredictionsCreated { get; set; }
        public int ScoresCreated { get; set; }
    }
}

