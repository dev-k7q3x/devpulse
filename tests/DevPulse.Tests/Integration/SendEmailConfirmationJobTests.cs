using DevPulse.Application.Interfaces;
using DevPulse.Application.Options;
using DevPulse.Application.Settings;
using DevPulse.Infrastructure.Email;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>Job maila potwierdzającego na prawdziwym PostgreSQL – sprawdza stan oczekującej zmiany w bazie przed wysyłką.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class SendEmailConfirmationJobTests(DevPulseApiFactory factory)
{
    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);
    private readonly EphemeralDataProtectionProvider _dataProtection = new();
    private readonly CapturingEmailSender _emailSender = new();

    [Fact]
    public async Task Should_SendConfirmationLinkToPendingAddress_When_ChangeStillPending()
    {
        // Arrange
        var (token, hash) = EmailConfirmationToken.Generate();
        var userId = await SeedPendingAsync(hash);

        // Act
        await RunJobAsync(userId, Protect(token));

        // Assert
        var message = _emailSender.Messages.Should().ContainSingle().Subject;
        message.To.Should().Be("pending@example.com");
        message.Subject.Should().Be(EmailConfirmationMessage.Subject);
        message.HtmlBody.Should().Contain($"https://devpulse.example/confirm-email?token={token}");
    }

    [Fact]
    public async Task Should_SendNothing_When_ChangeSupersededByNewerOne()
    {
        // Arrange – ponowienie starego joba po tym, jak użytkownik wpisał inny adres
        var (oldToken, _) = EmailConfirmationToken.Generate();
        var (_, newHash) = EmailConfirmationToken.Generate();
        var userId = await SeedPendingAsync(newHash);

        // Act
        await RunJobAsync(userId, Protect(oldToken));

        // Assert
        _emailSender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_SendNothing_When_ChangeCancelled()
    {
        // Arrange
        var (token, _) = EmailConfirmationToken.Generate();
        var user = new UserBuilder().Build();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).Build());

        // Act
        await RunJobAsync(user.Id, Protect(token));

        // Assert
        _emailSender.Messages.Should().BeEmpty();
    }

    [Fact]
    public async Task Should_SendNothing_When_TokenPayloadCannotBeDecrypted()
    {
        // Arrange – np. payload zaszyfrowany kluczem spoza pierścienia
        var (_, hash) = EmailConfirmationToken.Generate();
        var userId = await SeedPendingAsync(hash);
        var foreignPayload = new EphemeralDataProtectionProvider()
            .CreateProtector(SendEmailConfirmationJob.TokenProtectorPurpose)
            .Protect("cokolwiek");

        // Act
        var act = () => RunJobAsync(userId, foreignPayload);

        // Assert
        await act.Should().NotThrowAsync();
        _emailSender.Messages.Should().BeEmpty();
    }

    private string Protect(string token) =>
        _dataProtection.CreateProtector(SendEmailConfirmationJob.TokenProtectorPurpose).Protect(token);

    private async Task RunJobAsync(Guid userId, string protectedToken)
    {
        await using var db = factory.CreateDbContext();
        var job = new SendEmailConfirmationJob(
            db,
            _emailSender,
            _dataProtection,
            Options.Create(new AppOptions { PublicBaseUrl = "https://devpulse.example" }),
            _timeProvider,
            NullLogger<SendEmailConfirmationJob>.Instance);

        await job.ExecuteAsync(userId, protectedToken, CancellationToken.None);
    }

    private async Task<Guid> SeedPendingAsync(byte[] tokenHash)
    {
        var user = new UserBuilder().Build();
        await SeedAsync(
            user,
            new UserSettingsBuilder().ForUser(user.Id).WithPendingEmail("pending@example.com", tokenHash, TestClock.Now.AddHours(24)).Build());
        return user.Id;
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private sealed class CapturingEmailSender : IEmailSender
    {
        public List<EmailMessage> Messages { get; } = [];

        public Task<string?> SendAsync(EmailMessage message, CancellationToken cancellationToken)
        {
            Messages.Add(message);
            return Task.FromResult<string?>("fake-message-id");
        }
    }
}
