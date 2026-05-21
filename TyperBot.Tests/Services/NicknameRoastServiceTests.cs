using Discord;
using FluentAssertions;
using Moq;
using TyperBot.DiscordBot.Services;

namespace TyperBot.Tests.Services;

public class NicknameRoastServiceTests
{
    [Fact]
    public async Task TryChangeNicknameByUsernameAsync_WhenUserExists_ChangesNicknameAndReturnsTrue()
    {
        // Arrange
        var service = new NicknameRoastService();
        var targetUser = new Mock<IGuildUser>();
        targetUser.SetupGet(x => x.Username).Returns("justynkaaa");

        string? changedNickname = null;
        targetUser
            .Setup(x => x.ModifyAsync(It.IsAny<Action<GuildUserProperties>>(), It.IsAny<RequestOptions?>()))
            .Returns<Action<GuildUserProperties>, RequestOptions?>((action, _) =>
            {
                var props = new GuildUserProperties();
                action(props);
                changedNickname = props.Nickname.IsSpecified ? props.Nickname.Value : null;
                return Task.CompletedTask;
            });

        // Act
        var result = await service.TryChangeNicknameByUsernameAsync(
            new[] { targetUser.Object },
            "justynkaaa",
            "Piździnka");

        // Assert
        result.Should().BeTrue();
        changedNickname.Should().Be("Piździnka");
        targetUser.Verify(
            x => x.ModifyAsync(It.IsAny<Action<GuildUserProperties>>(), It.IsAny<RequestOptions?>()),
            Times.Once);
    }

    [Fact]
    public async Task TryChangeNicknameByUsernameAsync_WhenUserMissing_ReturnsFalse()
    {
        // Arrange
        var service = new NicknameRoastService();
        var user = new Mock<IGuildUser>();
        user.SetupGet(x => x.Username).Returns("someoneElse");

        // Act
        var result = await service.TryChangeNicknameByUsernameAsync(
            new[] { user.Object },
            "agness88",
            "Cwelinica");

        // Assert
        result.Should().BeFalse();
        user.Verify(
            x => x.ModifyAsync(It.IsAny<Action<GuildUserProperties>>(), It.IsAny<RequestOptions?>()),
            Times.Never);
    }

    [Fact]
    public async Task TryChangeNicknameByUsernameAsync_WhenMatchesByNickname_ReturnsTrue()
    {
        // Arrange
        var service = new NicknameRoastService();
        var user = new Mock<IGuildUser>();
        user.SetupGet(x => x.Username).Returns("differentUsername");
        user.SetupGet(x => x.Nickname).Returns("agness88");
        user.SetupGet(x => x.GlobalName).Returns((string?)null);
        user.Setup(x => x.ModifyAsync(It.IsAny<Action<GuildUserProperties>>(), It.IsAny<RequestOptions?>()))
            .Returns(Task.CompletedTask);

        // Act
        var result = await service.TryChangeNicknameByUsernameAsync(
            new[] { user.Object },
            "agness88",
            "Cwelinica");

        // Assert
        result.Should().BeTrue();
        user.Verify(
            x => x.ModifyAsync(It.IsAny<Action<GuildUserProperties>>(), It.IsAny<RequestOptions?>()),
            Times.Once);
    }
}
