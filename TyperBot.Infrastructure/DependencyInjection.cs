using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        // Do not set a global SplitQuery default: it multiplies SQLite round-trips for every multi-include query
        // and can feel slower on VPS/loaded hosts. Use AsSplitQuery() only on hot paths with two collection
        // includes (e.g. Player: PlayerScores + Predictions).
        services.AddDbContext<TyperContext>(options =>
            options.UseSqlite(connectionString)
                .AddInterceptors(new SqlitePragmaConnectionInterceptor())
                .ConfigureWarnings(warnings =>
                    warnings.Ignore(RelationalEventId.PendingModelChangesWarning)));

        // Register repositories
        services.AddScoped<ISeasonRepository, SeasonRepository>();
        services.AddScoped<IRoundRepository, RoundRepository>();
        services.AddScoped<IMatchRepository, MatchRepository>();
        services.AddScoped<IPlayerRepository, PlayerRepository>();
        services.AddScoped<IPredictionRepository, PredictionRepository>();
        services.AddScoped<IPlayerScoreRepository, PlayerScoreRepository>();

        return services;
    }
}

