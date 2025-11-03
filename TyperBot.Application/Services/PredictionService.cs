using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class PredictionService
{
    private readonly IPredictionRepository _predictionRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ScoreCalculator _scoreCalculator;
    private readonly IPlayerScoreRepository _playerScoreRepository;

    public PredictionService(
        IPredictionRepository predictionRepository,
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        ScoreCalculator scoreCalculator,
        IPlayerScoreRepository playerScoreRepository)
    {
        _predictionRepository = predictionRepository;
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _scoreCalculator = scoreCalculator;
        _playerScoreRepository = playerScoreRepository;
    }

    public async Task<(bool isValid, string? errorMessage)> ValidatePrediction(
        ulong discordUserId, int matchId, int homeTip, int awayTip)
    {
        // Validation 1: Both integers >= 0
        if (homeTip < 0 || awayTip < 0)
        {
            return (false, "Both scores must be greater than or equal to 0.");
        }

        // Validation 2: Sum equals 90
        if (homeTip + awayTip != 90)
        {
            return (false, "The sum of both scores must equal 90 (e.g., 50:40, 46:44, 45:45).");
        }

        // Validation 3: Before match start time
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            return (false, "Match not found.");
        }

        if (DateTimeOffset.UtcNow >= match.StartTime)
        {
            return (false, "Prediction time has expired. Matches can only be predicted before their start time.");
        }

        // Validation 4: Match not cancelled
        if (match.Status == MatchStatus.Cancelled)
        {
            return (false, "This match has been cancelled.");
        }

        return (true, null);
    }

    public async Task<Prediction?> CreateOrUpdatePredictionAsync(
        ulong discordUserId, int matchId, int homeTip, int awayTip)
    {
        // Get or create player
        var player = await _playerRepository.GetByDiscordUserIdAsync(discordUserId);
        if (player == null)
        {
            // Player will be created by the bot service when they first interact
            return null;
        }

        // Check if prediction exists
        var existingPrediction = await _predictionRepository.GetByMatchAndPlayerAsync(matchId, player.Id);

        if (existingPrediction != null)
        {
            // Update existing prediction
            existingPrediction.HomeTip = homeTip;
            existingPrediction.AwayTip = awayTip;
            existingPrediction.UpdatedAt = DateTimeOffset.UtcNow;
            await _predictionRepository.UpdateAsync(existingPrediction);
            return existingPrediction;
        }
        else
        {
            // Create new prediction
            var prediction = new Prediction
            {
                MatchId = matchId,
                PlayerId = player.Id,
                HomeTip = homeTip,
                AwayTip = awayTip,
                CreatedAt = DateTimeOffset.UtcNow,
                IsValid = true
            };

            await _predictionRepository.AddAsync(prediction);
            return prediction;
        }
    }

    public async Task RecalculateMatchScoresAsync(int matchId)
    {
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null || match.Status != MatchStatus.Finished || !match.HomeScore.HasValue || !match.AwayScore.HasValue)
        {
            return;
        }

        var predictions = await _predictionRepository.GetValidPredictionsByMatchAsync(matchId);

        foreach (var prediction in predictions)
        {
            var (points, bucket) = _scoreCalculator.CalculateScore(
                match.HomeScore.Value,
                match.AwayScore.Value,
                prediction.HomeTip,
                prediction.AwayTip
            );

            if (prediction.PlayerScore == null)
            {
                var playerScore = new PlayerScore
                {
                    PredictionId = prediction.Id,
                    Points = points,
                    Bucket = bucket
                };
                await _playerScoreRepository.AddAsync(playerScore);
            }
            else
            {
                prediction.PlayerScore.Points = points;
                prediction.PlayerScore.Bucket = bucket;
                await _playerScoreRepository.UpdateAsync(prediction.PlayerScore);
            }
        }
    }
}

