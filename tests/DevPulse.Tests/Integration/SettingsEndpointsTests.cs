using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DevPulse.Application.DTOs.Settings;
using DevPulse.Application.Errors;
using DevPulse.Application.Settings;
using DevPulse.Domain.Entities;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DevPulse.Tests.Integration;

/// <summary>/api/settings (plan 03 §4): ustawienia, zmiana i potwierdzenie emaila, 401/400/409/429, izolacja użytkowników.</summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class SettingsEndpointsTests(DevPulseApiFactory factory)
{
    // API działa na zegarze systemowym – token z wygaśnięciem daleko w przyszłości jest ważny, a z TestClock + 1 h już nie.
    private static readonly DateTimeOffset FarFuture = TestClock.Now.AddYears(5);

    [Fact]
    public async Task Should_ReturnSettingsWithNextDigest_When_UserSignedIn()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithSchedule(9, "Europe/Warsaw"));

        // Act
        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        // Assert
        settings!.Email.Should().Be("octocat@example.com");
        settings.EmailConfirmed.Should().BeTrue();
        settings.SendHour.Should().Be(9);
        settings.TimeZoneId.Should().Be("Europe/Warsaw");
        settings.DigestEnabled.Should().BeTrue();
        settings.NextDigestAt.Should().NotBeNull();
        TimeZoneInfo.ConvertTime(settings.NextDigestAt!.Value, TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")).Hour.Should().Be(9);
    }

    [Fact]
    public async Task Should_ReturnNullNextDigest_When_NoConfirmedEmail()
    {
        // Arrange – konto GitHub bez publicznego/primary emaila
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithEmail(null));

        // Act
        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");

        // Assert
        settings!.EmailConfirmed.Should().BeFalse();
        settings.NextDigestAt.Should().BeNull();
    }

    [Fact]
    public async Task Should_UpdateScheduleAndReturnFullState_When_PutValid()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));

        // Act
        var response = await client.PutAsJsonAsync("/api/settings", new { sendHour = 0, timeZoneId = "America/New_York", digestEnabled = false });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        settings!.SendHour.Should().Be(0);
        settings.TimeZoneId.Should().Be("America/New_York");
        settings.DigestEnabled.Should().BeFalse();
        settings.NextDigestAt.Should().BeNull("digest jest wyłączony");

        await using var db = factory.CreateDbContext();
        var stored = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        stored.SendHour.Value.Should().Be(0);
        stored.DigestEnabled.Should().BeFalse();
    }

    [Theory]
    [InlineData(7, "Mars/Olympus")]
    [InlineData(7, "Central European Standard Time")]
    [InlineData(24, "Europe/Warsaw")]
    public async Task Should_Return400Validation_When_PutInvalid(int sendHour, string timeZoneId)
    {
        // Arrange – tylko strefy IANA, godzina 0–23
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));

        // Act
        var response = await client.PutAsJsonAsync("/api/settings", new { sendHour, timeZoneId, digestEnabled = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Type.Should().Be(ProblemTypes.Validation);
    }

    [Fact]
    public async Task Should_Return401_When_NoSession()
    {
        // Arrange
        using var client = factory.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync("/api/settings");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    [Fact]
    public async Task Should_Return400_When_PutWithoutXsrfHeader()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));
        client.DefaultRequestHeaders.Remove("X-XSRF-TOKEN");

        // Act
        var response = await client.PutAsJsonAsync("/api/settings", new { sendHour = 8, timeZoneId = "UTC", digestEnabled = true });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task Should_ReturnOwnSettingsOnly_When_TwoUsersSignedIn()
    {
        // Arrange
        var userA = new UserBuilder().Build();
        var userB = new UserBuilder().Build();
        using var clientA = await CreateClientAsync(userA, new UserSettingsBuilder().ForUser(userA.Id).WithEmail("a@example.com"));
        using var clientB = await CreateClientAsync(userB, new UserSettingsBuilder().ForUser(userB.Id).WithEmail("b@example.com"));

        // Act – B zmienia swoje ustawienia, A czyta swoje
        (await clientB.PutAsJsonAsync("/api/settings", new { sendHour = 22, timeZoneId = "UTC", digestEnabled = false })).EnsureSuccessStatusCode();
        var settingsA = await clientA.GetFromJsonAsync<SettingsResponse>("/api/settings");

        // Assert
        settingsA!.Email.Should().Be("a@example.com");
        settingsA.SendHour.Should().Be(7);
        settingsA.DigestEnabled.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return202WithPendingEmailAndEnqueueMailWithEncryptedToken_When_EmailChangeStarted()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));

        // Act
        var response = await client.PostAsJsonAsync("/api/settings/email", new { email = "new@example.com" });

        // Assert – odpowiedź
        response.StatusCode.Should().Be(HttpStatusCode.Accepted);
        var settings = await response.Content.ReadFromJsonAsync<SettingsResponse>();
        settings!.Email.Should().Be("octocat@example.com", "digest dalej idzie na stary adres do czasu potwierdzenia");
        settings.PendingEmail.Should().Be("new@example.com");
        settings.PendingEmailExpiresAt.Should().NotBeNull();

        // Assert – job Hangfire niesie token zaszyfrowany, który pasuje do hasha w bazie
        await using var db = factory.CreateDbContext();
        var stored = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        var protectedToken = await ReadEnqueuedProtectedTokenAsync(db, user.Id);
        var token = factory.Services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(SendEmailConfirmationJob.TokenProtectorPurpose)
            .Unprotect(protectedToken);
        protectedToken.Should().NotContain(token);
        EmailConfirmationToken.TryHash(token).Should().Equal(stored.PendingEmailTokenHash);
    }

    [Fact]
    public async Task Should_Return409_When_NewEmailIsCurrentConfirmedAddress()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithEmail("same@example.com"));

        // Act
        var response = await client.PostAsJsonAsync("/api/settings/email", new { email = "SAME@example.com" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Conflict);
    }

    [Fact]
    public async Task Should_Return400Validation_When_EmailInvalid()
    {
        // Arrange
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));

        // Act
        var response = await client.PostAsJsonAsync("/api/settings/email", new { email = "to-nie-email" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Errors.Should().ContainKey("email");
    }

    [Fact]
    public async Task Should_Return429RateLimited_When_EmailLimitExceeded()
    {
        // Arrange – polityka email: 3 na godzinę per użytkownik (koszt SendGrid, ochrona cudzych skrzynek)
        var user = new UserBuilder().Build();
        using var client = await CreateClientAsync(user, new UserSettingsBuilder().ForUser(user.Id));
        for (var i = 0; i < 3; i++)
        {
            (await client.PostAsJsonAsync("/api/settings/email", new { email = $"n{i}@example.com" })).StatusCode
                .Should().Be(HttpStatusCode.Accepted);
        }

        // Act
        var response = await client.PostAsJsonAsync("/api/settings/email", new { email = "n3@example.com" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
        response.Headers.RetryAfter.Should().NotBeNull();
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.RateLimited);
    }

    [Fact]
    public async Task Should_ClearPendingEmail_When_ChangeCancelled()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var (_, hash) = EmailConfirmationToken.Generate();
        using var client = await CreateClientAsync(
            user,
            new UserSettingsBuilder().ForUser(user.Id).WithPendingEmail("pending@example.com", hash, FarFuture));

        // Act
        var response = await client.DeleteAsync("/api/settings/email/pending");
        var second = await client.DeleteAsync("/api/settings/email/pending");

        // Assert – idempotentne
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);
        second.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var settings = await client.GetFromJsonAsync<SettingsResponse>("/api/settings");
        settings!.PendingEmail.Should().BeNull();
    }

    [Fact]
    public async Task Should_ConfirmEmailWithoutSession_When_TokenValid()
    {
        // Arrange – link otwarty na innym urządzeniu, bez logowania
        var user = new UserBuilder().Build();
        var (token, hash) = EmailConfirmationToken.Generate();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithEmail(null).WithPendingEmail("confirmed@example.com", hash, FarFuture));
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        (await response.Content.ReadFromJsonAsync<EmailConfirmResponse>())!.Email.Should().Be("confirmed@example.com");
        await using var db = factory.CreateDbContext();
        var stored = await db.UserSettings.SingleAsync(s => s.UserId == user.Id);
        stored.Email.Should().Be("confirmed@example.com");
        stored.EmailConfirmed.Should().BeTrue();
        stored.PendingEmail.Should().BeNull();
    }

    [Fact]
    public async Task Should_Return400InvalidToken_When_TokenUsedTwice()
    {
        // Arrange
        var user = new UserBuilder().Build();
        var (token, hash) = EmailConfirmationToken.Generate();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithPendingEmail("twice@example.com", hash, FarFuture));
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();
        (await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token })).EnsureSuccessStatusCode();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token });

        // Assert
        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task Should_Return400InvalidToken_When_TokenExpired()
    {
        // Arrange – wygasł godzinę po TestClock, czyli dawno przed zegarem systemowym API
        var user = new UserBuilder().Build();
        var (token, hash) = EmailConfirmationToken.Generate();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithPendingEmail("late@example.com", hash, TestClock.Now.AddHours(1)));
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token });

        // Assert
        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task Should_Return400InvalidToken_When_TokenUnknown()
    {
        // Arrange – poprawny format, ale nie ma go w bazie
        var (token, _) = EmailConfirmationToken.Generate();
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token });

        // Assert
        await AssertInvalidTokenAsync(response);
    }

    [Fact]
    public async Task Should_InvalidateOldToken_When_EmailChangeRestarted()
    {
        // Arrange – stary link z pierwszej zmiany, potem użytkownik wpisuje inny adres
        var user = new UserBuilder().Build();
        var (oldToken, oldHash) = EmailConfirmationToken.Generate();
        using var client = await CreateClientAsync(
            user,
            new UserSettingsBuilder().ForUser(user.Id).WithPendingEmail("first@example.com", oldHash, FarFuture));
        (await client.PostAsJsonAsync("/api/settings/email", new { email = "second@example.com" })).StatusCode.Should().Be(HttpStatusCode.Accepted);
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token = oldToken });

        // Assert
        await AssertInvalidTokenAsync(response);
        await using var db = factory.CreateDbContext();
        (await db.UserSettings.SingleAsync(s => s.UserId == user.Id)).PendingEmail.Should().Be("second@example.com");
    }

    [Fact]
    public async Task Should_Return400Validation_When_TokenFormatInvalid()
    {
        // Arrange
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token = "za-krotki" });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await response.Content.ReadFromJsonAsync<ValidationProblemDetails>())!.Type.Should().Be(ProblemTypes.Validation);
    }

    [Fact]
    public async Task Should_Return429_When_ConfirmAttemptsExceedAnonymousLimit()
    {
        // Arrange – polityka anonymous: 10 na minutę per IP (ochrona przed zgadywaniem tokenu)
        using var anonymous = await CreateAnonymousClientWithXsrfAsync();
        for (var i = 0; i < 10; i++)
        {
            await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token = EmailConfirmationToken.Generate().Token });
        }

        // Act
        var response = await anonymous.PostAsJsonAsync("/api/settings/email/confirm", new { token = EmailConfirmationToken.Generate().Token });

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);
    }

    private async Task<HttpClient> CreateClientAsync(User user, UserSettingsBuilder settings)
    {
        await SeedAsync(user, settings);
        return await factory.CreateSignedInClientAsync(user.Id);
    }

    private async Task SeedAsync(User user, UserSettingsBuilder settings)
    {
        await using var db = factory.CreateDbContext();
        db.Users.Add(user);
        db.UserSettings.Add(settings.Build());
        await db.SaveChangesAsync();
    }

    /// <summary>Anonimowy klient z tokenem XSRF – SPA pobiera go z GET /api/auth/me przed POST potwierdzenia.</summary>
    private async Task<HttpClient> CreateAnonymousClientWithXsrfAsync()
    {
        var client = factory.CreateAnonymousClient();
        await client.UseXsrfTokenAsync();
        return client;
    }

    private static async Task AssertInvalidTokenAsync(HttpResponseMessage response)
    {
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        problem!.Type.Should().Be(ProblemTypes.InvalidToken);
        problem.Detail.Should().Be(new InvalidTokenException().Message, "jeden komunikat dla każdego błędu tokenu");
    }

    /// <summary>Drugi argument joba (zaszyfrowany token) z tabeli Hangfire – dokładnie to, co leży w bazie.</summary>
    private static async Task<string> ReadEnqueuedProtectedTokenAsync(Infrastructure.Persistence.DevPulseDbContext db, Guid userId)
    {
        var arguments = await db.Database
            .SqlQuery<string>($"SELECT arguments AS \"Value\" FROM hangfire.job WHERE invocationdata::text LIKE '%SendEmailConfirmationJob%' AND arguments::text LIKE {"%" + userId + "%"}")
            .SingleAsync();

        using var document = JsonDocument.Parse(arguments);
        var serialized = document.RootElement[1].GetString()!;
        return JsonSerializer.Deserialize<string>(serialized)!;
    }
}
