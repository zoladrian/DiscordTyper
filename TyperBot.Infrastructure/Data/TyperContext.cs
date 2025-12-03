using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;

namespace TyperBot.Infrastructure.Data;

public class TyperContext : DbContext
{
    public TyperContext(DbContextOptions<TyperContext> options) : base(options)
    {
    }

    public DbSet<Season> Seasons { get; set; }
    public DbSet<Round> Rounds { get; set; }
    public DbSet<Match> Matches { get; set; }
    public DbSet<Player> Players { get; set; }
    public DbSet<Prediction> Predictions { get; set; }
    public DbSet<PlayerScore> PlayerScores { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Season configuration
        modelBuilder.Entity<Season>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).IsRequired().HasMaxLength(200);
        });

        // Round configuration
        modelBuilder.Entity<Round>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Number).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(500).IsRequired(false);

            entity.HasOne(e => e.Season)
                .WithMany(s => s.Rounds)
                .HasForeignKey(e => e.SeasonId)
                .OnDelete(DeleteBehavior.Cascade);

            // Add index for frequently queried columns
            entity.HasIndex(e => e.SeasonId);
            entity.HasIndex(e => new { e.SeasonId, e.Number });
        });

        // Match configuration
        modelBuilder.Entity<Match>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.HomeTeam).IsRequired().HasMaxLength(200);
            entity.Property(e => e.AwayTeam).IsRequired().HasMaxLength(200);
            entity.Property(e => e.Status).HasConversion<int>();
            entity.Property(e => e.StartTime).IsRequired();

            entity.HasOne(e => e.Round)
                .WithMany(r => r.Matches)
                .HasForeignKey(e => e.RoundId)
                .OnDelete(DeleteBehavior.Cascade);

            // Add indexes for frequently queried columns
            entity.HasIndex(e => e.RoundId);
            entity.HasIndex(e => e.StartTime);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ThreadId);
        });

        // Player configuration
        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.DiscordUserId).IsRequired();
            entity.Property(e => e.DiscordUsername).IsRequired().HasMaxLength(200);

            entity.HasIndex(e => e.DiscordUserId);
        });

        // Prediction configuration
        modelBuilder.Entity<Prediction>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).IsRequired();
            entity.Property(e => e.IsValid).IsRequired().HasDefaultValue(true);

            entity.HasOne(e => e.Match)
                .WithMany(m => m.Predictions)
                .HasForeignKey(e => e.MatchId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.Player)
                .WithMany(p => p.Predictions)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(e => e.PlayerScore)
                .WithOne(ps => ps.Prediction)
                .HasForeignKey<PlayerScore>(ps => ps.PredictionId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.MatchId, e.PlayerId }).IsUnique();
            entity.HasIndex(e => e.MatchId);
            entity.HasIndex(e => e.PlayerId);
        });

        // PlayerScore configuration
        modelBuilder.Entity<PlayerScore>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Points).IsRequired();
            entity.Property(e => e.Bucket).HasConversion<int>();
            entity.Property(e => e.PlayerId).IsRequired(); // Explicitly mark as required
            
            // CRITICAL FIX: Direct relationship to Player for season standings calculation
            entity.HasOne(e => e.Player)
                .WithMany(p => p.PlayerScores)
                .HasForeignKey(e => e.PlayerId)
                .OnDelete(DeleteBehavior.Cascade)
                .IsRequired();
        });
    }
}

