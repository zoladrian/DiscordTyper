using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Application.Services;

public class PredictionService
{
    private readonly IPredictionRepository _predictionRepository;
    private readonly IMatchRepository _matchRepository;
    private readonly IPlayerRepository _playerRepository;
    private readonly ScoreCalculator _scoreCalculator;
    private readonly IPlayerScoreRepository _playerScoreRepository;
    private readonly TyperContext _context;

    public PredictionService(
        IPredictionRepository predictionRepository,
        IMatchRepository matchRepository,
        IPlayerRepository playerRepository,
        ScoreCalculator scoreCalculator,
        IPlayerScoreRepository playerScoreRepository,
        TyperContext context)
    {
        _predictionRepository = predictionRepository;
        _matchRepository = matchRepository;
        _playerRepository = playerRepository;
        _scoreCalculator = scoreCalculator;
        _playerScoreRepository = playerScoreRepository;
        _context = context;
    }

    public async Task<(bool isValid, string? errorMessage)> ValidatePrediction(
        ulong discordUserId, int matchId, int homeTip, int awayTip)
    {
        // Validation 1: Both integers >= 0
        if (homeTip < 0 || awayTip < 0)
        {
            return (false, "Oba wyniki muszą być większe lub równe 0.");
        }

        // Validation 2: Sum equals 90
        if (homeTip + awayTip != 90)
        {
            return (false, "Suma obu wyników musi wynosić 90 (np. 50:40, 46:44, 45:45).");
        }

        // Validation 3: Before match start time
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            return (false, "Mecz nie znaleziony.");
        }

        if (DateTimeOffset.UtcNow >= match.StartTime)
        {
            return (false, "Czas na typowanie minął. Możesz typować tylko przed rozpoczęciem meczu.");
        }

        // Validation 4: Match not cancelled
        if (match.Status == MatchStatus.Cancelled)
        {
            return (false, "Ten mecz został odwołany.");
        }

        return (true, null);
    }

    public async Task<Prediction?> CreateOrUpdatePredictionAsync(
        ulong discordUserId, int matchId, int homeTip, int awayTip)
    {
        // Use database transaction to prevent race conditions
        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
            // Re-validate match status within transaction to prevent race conditions
            var match = await _matchRepository.GetByIdAsync(matchId);
            if (match == null)
            {
                await transaction.RollbackAsync();
                return null;
            }

            if (DateTimeOffset.UtcNow >= match.StartTime)
            {
                await transaction.RollbackAsync();
                return null;
            }

            if (match.Status == MatchStatus.Cancelled || match.Status == MatchStatus.Finished)
            {
                await transaction.RollbackAsync();
                return null;
            }

            // Get or create player
            var player = await _playerRepository.GetByDiscordUserIdAsync(discordUserId);
            if (player == null)
            {
                // Player will be created by the bot service when they first interact
                await transaction.RollbackAsync();
                return null;
            }

            // Check if prediction exists (within transaction to prevent race conditions)
            var existingPrediction = await _predictionRepository.GetByMatchAndPlayerAsync(matchId, player.Id);

            if (existingPrediction != null)
            {
                // Update existing prediction
                existingPrediction.HomeTip = homeTip;
                existingPrediction.AwayTip = awayTip;
                existingPrediction.UpdatedAt = DateTimeOffset.UtcNow;
                await _predictionRepository.UpdateAsync(existingPrediction);
                await transaction.CommitAsync();
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
                await transaction.CommitAsync();
                return prediction;
            }
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
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
                    PlayerId = prediction.PlayerId, // ← CRITICAL FIX: Set PlayerId for direct relationship
                    Points = points,
                    Bucket = bucket
                };
                await _playerScoreRepository.AddAsync(playerScore);
            }
            else
            {
                prediction.PlayerScore.Points = points;
                prediction.PlayerScore.Bucket = bucket;
                prediction.PlayerScore.PlayerId = prediction.PlayerId; // ← Ensure consistency
                await _playerScoreRepository.UpdateAsync(prediction.PlayerScore);
            }
        }
    }
}

