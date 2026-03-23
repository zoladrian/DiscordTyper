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

        // Validation 2: Sum must equal 90
        if (homeTip + awayTip != 90)
        {
            return (false, $"Suma punktów musi wynosić 90, a nie {homeTip + awayTip}. Oglądałeś kiedyś żużel?");
        }

        // Validation 3: Before typing deadline (or match start time if deadline not set)
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null)
        {
            return (false, "Mecz nie znaleziony.");
        }

        // Validation 4: Match status checks
        if (match.Status == MatchStatus.Cancelled)
        {
            return (false, "Ten mecz został odwołany.");
        }

        if (match.Status == MatchStatus.Finished)
        {
            return (false, "Ten mecz już się zakończył.");
        }

        if (match.Status == MatchStatus.InProgress)
        {
            return (false, "Ten mecz jest w trakcie rozgrywania.");
        }

        // Validation 5: Timing — postponed uses StartTime, others use TypingDeadline
        if (match.Status == MatchStatus.Postponed)
        {
            if (DateTimeOffset.UtcNow >= match.StartTime)
            {
                return (false, "Czas na typowanie minął. Możesz typować tylko przed pierwotną godziną rozpoczęcia meczu.");
            }
        }
        else
        {
            var deadline = match.TypingDeadline ?? match.StartTime.AddHours(-1);
            if (DateTimeOffset.UtcNow >= deadline)
            {
                return (false, "Czas na typowanie minął. Możesz typować tylko przed deadline typowania.");
            }
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

            var deadline = match.TypingDeadline ?? match.StartTime.AddHours(-1); // Default: 1 hour before match
            if (DateTimeOffset.UtcNow >= deadline)
            {
                await transaction.RollbackAsync();
                return null;
            }

            if (match.Status == MatchStatus.Cancelled || match.Status == MatchStatus.Finished)
            {
                await transaction.RollbackAsync();
                return null;
            }

            // Allow postponed matches to be predicted/updated before original start time
            if (match.Status == MatchStatus.Postponed && DateTimeOffset.UtcNow >= match.StartTime)
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

    public async Task ClearMatchScoresAsync(int matchId)
    {
        var predictions = await _predictionRepository.GetValidPredictionsByMatchAsync(matchId);
        foreach (var prediction in predictions)
        {
            if (prediction.PlayerScore != null)
            {
                await _playerScoreRepository.DeleteAsync(prediction.PlayerScore.Id);
            }
        }
    }

    public async Task RecalculateMatchScoresAsync(int matchId)
    {
        var match = await _matchRepository.GetByIdAsync(matchId);
        if (match == null || match.Status != MatchStatus.Finished || !match.HomeScore.HasValue || !match.AwayScore.HasValue)
        {
            return;
        }

        await using var transaction = await _context.Database.BeginTransactionAsync();
        try
        {
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
                        PlayerId = prediction.PlayerId,
                        Points = points,
                        Bucket = bucket
                    };
                    await _playerScoreRepository.AddAsync(playerScore);
                }
                else
                {
                    prediction.PlayerScore.Points = points;
                    prediction.PlayerScore.Bucket = bucket;
                    prediction.PlayerScore.PlayerId = prediction.PlayerId;
                    await _playerScoreRepository.UpdateAsync(prediction.PlayerScore);
                }
            }

            await transaction.CommitAsync();
        }
        catch
        {
            await transaction.RollbackAsync();
            throw;
        }
    }
}

