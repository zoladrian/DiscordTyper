using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;

namespace TyperBot.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<TyperContext>(options =>
            options.UseSqlite(connectionString));

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

