using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using TyperBot.Domain.Entities;
using TyperBot.Infrastructure.Data;
using TyperBot.Infrastructure.Repositories;
using Xunit;

namespace TyperBot.Tests.Repositories;

public class PlayerRepositoryTests : IDisposable
{
    private readonly TyperContext _context;
    private readonly PlayerRepository _repository;

    public PlayerRepositoryTests()
    {
        var options = new DbContextOptionsBuilder<TyperContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _context = new TyperContext(options);
        _repository = new PlayerRepository(_context);
    }

    [Fact]
    public async Task AddAsync_NewPlayer_AddsSuccessfully()
    {
        // Arrange
        var player = new Player
        {
            DiscordUserId = 123456789,
            DiscordUsername = "TestUser",
            IsActive = true
        };

        // Act
        var result = await _repository.AddAsync(player);

        // Assert
        result.Id.Should().BeGreaterThan(0);
        result.DiscordUserId.Should().Be(123456789);
    }

    [Fact]
    public async Task GetByDiscordUserIdAsync_ExistingPlayer_ReturnsPlayer()
    {
        // Arrange
        var player = new Player
        {
            DiscordUserId = 987654321,
            DiscordUsername = "AnotherUser",
            IsActive = true
        };
        await _repository.AddAsync(player);

        // Act
        var result = await _repository.GetByDiscordUserIdAsync(987654321);

        // Assert
        result.Should().NotBeNull();
        result!.DiscordUsername.Should().Be("AnotherUser");
    }

    [Fact]
    public async Task GetByDiscordUserIdAsync_NonExistingPlayer_ReturnsNull()
    {
        // Act
        var result = await _repository.GetByDiscordUserIdAsync(999999999);

        // Assert
        result.Should().BeNull();
    }

    [Fact]
    public async Task GetActivePlayersAsync_ReturnsOnlyActivePlayers()
    {
        // Arrange
        await _repository.AddAsync(new Player { DiscordUserId = 1, DiscordUsername = "Active1", IsActive = true });
        await _repository.AddAsync(new Player { DiscordUserId = 2, DiscordUsername = "Active2", IsActive = true });
        await _repository.AddAsync(new Player { DiscordUserId = 3, DiscordUsername = "Inactive", IsActive = false });

        // Act
        var result = await _repository.GetActivePlayersAsync();

        // Assert
        result.Should().HaveCount(2);
        result.Should().OnlyContain(p => p.IsActive);
    }

    [Fact]
    public async Task UpdateAsync_ModifiesPlayer()
    {
        // Arrange
        var player = new Player
        {
            DiscordUserId = 111,
            DiscordUsername = "OldName",
            IsActive = true
        };
        await _repository.AddAsync(player);

        // Act
        player.DiscordUsername = "NewName";
        player.IsActive = false;
        await _repository.UpdateAsync(player);

        // Assert
        var updated = await _repository.GetByIdAsync(player.Id);
        updated!.DiscordUsername.Should().Be("NewName");
        updated.IsActive.Should().BeFalse();
    }

    [Fact]
    public async Task DeleteAsync_RemovesPlayer()
    {
        // Arrange
        var player = new Player { DiscordUserId = 222, DiscordUsername = "ToDelete", IsActive = true };
        await _repository.AddAsync(player);
        var id = player.Id;

        // Act
        await _repository.DeleteAsync(id);

        // Assert
        var deleted = await _repository.GetByIdAsync(id);
        deleted.Should().BeNull();
    }

    public void Dispose()
    {
        _context.Dispose();
    }
}

