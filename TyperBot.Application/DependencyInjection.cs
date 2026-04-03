using Microsoft.Extensions.DependencyInjection;
using TyperBot.Application.Services;

namespace TyperBot.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IPlayerDisplayNameResolver, DbUsernameDisplayNameResolver>();
        services.AddScoped<ScoreCalculator>();
        services.AddScoped<PredictionService>();
        services.AddScoped<RoundManager>();
        services.AddScoped<TableGenerator>();
        services.AddScoped<MatchResultsTableImageGenerator>();
        services.AddScoped<RevealedPredictionsTableImageGenerator>();
        services.AddScoped<StandingsAnalyticsGenerator>();
        services.AddScoped<ExportService>();
        services.AddScoped<MatchManagementService>();
        services.AddScoped<DemoDataSeeder>();

        return services;
    }
}

