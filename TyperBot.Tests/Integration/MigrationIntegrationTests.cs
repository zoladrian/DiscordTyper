using System.Data.Common;
using System.Reflection;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using TyperBot.Domain.Entities;
using TyperBot.Domain.Enums;
using TyperBot.Infrastructure.Data;

namespace TyperBot.Tests.Integration;

/// <summary>
/// Guards against broken migration ordering (e.g. ALTER before CREATE), orphaned migrations
/// without <see cref="MigrationAttribute"/>, and schema drift on a fresh database.
/// </summary>
public sealed class MigrationIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;

    public MigrationIntegrationTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();
    }

    public void Dispose() => _connection.Dispose();

    private TyperContext CreateContext() =>
        new(new DbContextOptionsBuilder<TyperContext>().UseSqlite(_connection).Options);

    [Fact]
    public void Migrate_on_empty_database_applies_every_migration_without_error()
    {
        using var context = CreateContext();

        var action = () => context.Database.Migrate();

        action.Should().NotThrow();
    }

    [Fact]
    public void After_migrate_applied_migrations_match_full_chain()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var all = context.Database.GetMigrations().ToList();
        var applied = context.Database.GetAppliedMigrations().ToList();

        all.Should().NotBeEmpty("at least one migration must be registered");
        applied.Should().Equal(all, "fresh DB must apply the full chain with no skips");
        context.Database.GetPendingMigrations().Should().BeEmpty();
    }

    [Fact]
    public async Task After_migrate_core_tables_and_columns_exist()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var tables = await GetTableNamesAsync(_connection);
        tables.Should().Contain("Seasons");
        tables.Should().Contain("Matches");
        tables.Should().Contain("Rounds");
        tables.Should().Contain("Players");
        tables.Should().Contain("Predictions");
        tables.Should().Contain("PlayerScores");
        tables.Should().Contain("__EFMigrationsHistory");

        var matchCols = await GetColumnNamesAsync(_connection, "Matches");
        matchCols.Should().Contain("ThreadCreationTime");
        matchCols.Should().Contain("TypingDeadline");
        matchCols.Should().Contain("ThreadId");
        matchCols.Should().Contain("PredictionsRevealed");

        var seasonCols = await GetColumnNamesAsync(_connection, "Seasons");
        seasonCols.Should().Contain("PreferredTableFormat");
    }

    [Fact]
    public async Task After_migrate_can_persist_minimal_match_graph()
    {
        using var context = CreateContext();
        context.Database.Migrate();

        var season = new Season
        {
            Name = "Migration test season",
            IsActive = true
        };
        context.Seasons.Add(season);
        await context.SaveChangesAsync();

        var round = new Round
        {
            SeasonId = season.Id,
            Number = 1,
            Description = "R1"
        };
        context.Rounds.Add(round);
        await context.SaveChangesAsync();

        context.Matches.Add(new Match
        {
            RoundId = round.Id,
            HomeTeam = "A",
            AwayTeam = "B",
            StartTime = DateTimeOffset.UtcNow.AddDays(1),
            Status = MatchStatus.Scheduled
        });
        await context.SaveChangesAsync();

        context.Matches.Should().HaveCount(1);
    }

    [Fact]
    public void InitialCreate_migration_is_first_in_lexicographic_order()
    {
        var ids = GetRegisteredMigrationIds();

        ids.Should().NotBeEmpty();
        ids[0].Should().EndWith("_InitialCreate",
            "migrations are applied in lexicographic ID order; anything before InitialCreate would run ALTER on missing tables");
    }

    [Fact]
    public void AddPreferredTableFormat_migration_runs_after_initial_create()
    {
        var ids = GetRegisteredMigrationIds();

        var initialIdx = ids.FindIndex(id => id.EndsWith("_InitialCreate", StringComparison.Ordinal));
        var preferredIdx = ids.FindIndex(id => id.EndsWith("_AddPreferredTableFormatToSeason", StringComparison.Ordinal));

        initialIdx.Should().BeGreaterThanOrEqualTo(0);
        preferredIdx.Should().BeGreaterThanOrEqualTo(0);
        preferredIdx.Should().BeGreaterThan(initialIdx,
            "PreferredTableFormat alters Seasons; InitialCreate must create Seasons first");
    }

    [Fact]
    public void Every_concrete_migration_type_has_migration_attribute()
    {
        foreach (var type in GetConcreteMigrationTypes())
        {
            var attr = type.GetCustomAttribute<MigrationAttribute>();
            attr.Should().NotBeNull($"migration {type.Name} must declare [Migration(\"...\")] on a partial (typically *.Designer.cs) or EF will not apply it");
            attr!.Id.Should().NotBeNullOrWhiteSpace();
        }
    }

    [Fact]
    public void Migration_ids_are_unique()
    {
        var ids = GetConcreteMigrationTypes()
            .Select(t => t.GetCustomAttribute<MigrationAttribute>()!.Id)
            .ToList();

        ids.Should().OnlyHaveUniqueItems();
    }

    private static List<string> GetRegisteredMigrationIds()
    {
        using var connection = new SqliteConnection("DataSource=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<TyperContext>().UseSqlite(connection).Options;
        using var context = new TyperContext(options);

        return context.Database.GetMigrations().OrderBy(id => id, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<Type> GetConcreteMigrationTypes() =>
        typeof(TyperContext).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsSubclassOf(typeof(Migration)));

    private static async Task<HashSet<string>> GetTableNamesAsync(DbConnection connection)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "SELECT name FROM sqlite_master WHERE type='table' AND name NOT LIKE 'sqlite_%'";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add(reader.GetString(0));
        return set;
    }

    private static string QuoteSqliteIdentifier(string name)
    {
        if (name.Contains('"', StringComparison.Ordinal))
            throw new ArgumentException("Invalid identifier", nameof(name));
        return "\"" + name + "\"";
    }

    private static async Task<HashSet<string>> GetColumnNamesAsync(DbConnection connection, string table)
    {
        await using var cmd = connection.CreateCommand();
        cmd.CommandText = "PRAGMA table_info(" + QuoteSqliteIdentifier(table) + ");";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            set.Add(reader.GetString(1));
        return set;
    }
}
