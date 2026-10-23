using DevPulse.Domain.Entities;
using DevPulse.Domain.ValueObjects;
using DevPulse.Tests.Builders;
using FluentAssertions;

namespace DevPulse.Tests.Unit.Domain;

[Trait("Category", "Unit")]
public class UserSettingsTests
{
    private static readonly byte[] TokenHash = new byte[UserSettings.TokenHashLength];

    [Fact]
    public void Should_ConfirmGitHubEmailAndUseDefaults_When_CreatedWithVerifiedEmail()
    {
        // Arrange
        var userId = Guid.CreateVersion7();

        // Act
        var settings = UserSettings.CreateDefault(userId, "octocat@example.com", TestClock.Now);

        // Assert
        settings.UserId.Should().Be(userId);
        settings.Email.Should().Be("octocat@example.com");
        settings.EmailConfirmed.Should().BeTrue();
        settings.SendHour.Should().Be(SendHour.Default);
        settings.TimeZoneId.Should().Be("UTC");
        settings.DigestEnabled.Should().BeTrue();
    }

    [Fact]
    public void Should_LeaveEmailUnconfirmed_When_CreatedWithoutEmail()
    {
        // Arrange & Act
        var settings = UserSettings.CreateDefault(Guid.CreateVersion7(), null, TestClock.Now);

        // Assert
        settings.Email.Should().BeNull();
        settings.EmailConfirmed.Should().BeFalse();
    }

    [Fact]
    public void Should_UpdateSchedule_When_ValuesValid()
    {
        // Arrange
        var settings = new UserSettingsBuilder().Build();

        // Act
        settings.UpdateSchedule(new SendHour(0), "Europe/Warsaw", digestEnabled: false, TestClock.Now.AddMinutes(5));

        // Assert
        settings.SendHour.Value.Should().Be(0);
        settings.TimeZoneId.Should().Be("Europe/Warsaw");
        settings.DigestEnabled.Should().BeFalse();
    }

    [Fact]
    public void Should_Throw_When_TimeZoneIdTooLong()
    {
        // Arrange
        var settings = new UserSettingsBuilder().Build();
        var act = () => settings.UpdateSchedule(
            SendHour.Default,
            new string('x', UserSettings.TimeZoneIdMaxLength + 1),
            true,
            TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Should_KeepCurrentEmailAndStorePending_When_EmailChangeStarted()
    {
        // Arrange
        var settings = new UserSettingsBuilder().WithEmail("old@example.com").Build();
        var expiresAt = TestClock.Now.AddHours(24);

        // Act
        settings.StartEmailChange("new@example.com", TokenHash, expiresAt, TestClock.Now);

        // Assert
        settings.Email.Should().Be("old@example.com");
        settings.PendingEmail.Should().Be("new@example.com");
        settings.PendingEmailTokenHash.Should().BeEquivalentTo(TokenHash);
        settings.PendingEmailTokenExpiresAt.Should().Be(expiresAt);
    }

    [Fact]
    public void Should_Throw_When_TokenHashIsNotSha256()
    {
        // Arrange
        var settings = new UserSettingsBuilder().Build();
        var act = () => settings.StartEmailChange("new@example.com", new byte[16], TestClock.Now.AddHours(1), TestClock.Now);

        // Act & Assert
        act.Should().Throw<ArgumentException>().WithMessage("*SHA-256*");
    }

    [Fact]
    public void Should_ClearPendingEmail_When_ChangeCancelled()
    {
        // Arrange
        var settings = new UserSettingsBuilder()
            .WithPendingEmail("new@example.com", TokenHash, TestClock.Now.AddHours(24))
            .Build();

        // Act
        settings.CancelEmailChange(TestClock.Now.AddMinutes(1));

        // Assert
        settings.PendingEmail.Should().BeNull();
        settings.PendingEmailTokenHash.Should().BeNull();
        settings.PendingEmailTokenExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Should_SwitchToPendingEmail_When_ConfirmedBeforeExpiry()
    {
        // Arrange
        var settings = new UserSettingsBuilder()
            .WithEmail(null)
            .WithPendingEmail("new@example.com", TokenHash, TestClock.Now.AddHours(24))
            .Build();

        // Act
        var confirmed = settings.TryConfirmPendingEmail(TestClock.Now.AddHours(1));

        // Assert
        confirmed.Should().BeTrue();
        settings.Email.Should().Be("new@example.com");
        settings.EmailConfirmed.Should().BeTrue();
        settings.PendingEmail.Should().BeNull();
        settings.PendingEmailTokenHash.Should().BeNull();
    }

    [Fact]
    public void Should_RejectConfirmation_When_TokenExpired()
    {
        // Arrange
        var expiresAt = TestClock.Now.AddHours(24);
        var settings = new UserSettingsBuilder()
            .WithEmail("old@example.com")
            .WithPendingEmail("new@example.com", TokenHash, expiresAt)
            .Build();

        // Act
        var confirmed = settings.TryConfirmPendingEmail(expiresAt);

        // Assert
        confirmed.Should().BeFalse();
        settings.Email.Should().Be("old@example.com");
        settings.PendingEmail.Should().Be("new@example.com");
    }

    [Fact]
    public void Should_RejectConfirmation_When_NoPendingEmail()
    {
        // Arrange
        var settings = new UserSettingsBuilder().Build();

        // Act
        var confirmed = settings.TryConfirmPendingEmail(TestClock.Now);

        // Assert
        confirmed.Should().BeFalse();
    }
}
