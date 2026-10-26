using System.Net;
using System.Net.Http.Json;
using System.Runtime.CompilerServices;
using System.Text.Json;
using DevPulse.Api.Controllers;
using DevPulse.Application.DigestHistory;
using DevPulse.Application.DTOs.Digests;
using DevPulse.Application.Errors;
using DevPulse.Domain.Entities;
using DevPulse.Domain.Enums;
using DevPulse.Tests.Builders;
using DevPulse.Tests.Integration.TestSupport;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace DevPulse.Tests.Integration;

/// <summary>
/// Historia digestów (plan 03 §5) na seedowanych digestach. Czas aplikacji podmieniony na <see cref="TestClock.Now"/>
/// (2026-10-21 07:00 UTC) – okno 30 dni zaczyna się 2026-09-21 (UTC) niezależnie od dnia uruchomienia testów.
/// </summary>
[Trait("Category", "Integration")]
[Collection(DevPulseApiCollection.Name)]
public class DigestsEndpointsTests(DevPulseApiFactory factory)
{
    private static readonly ConditionalWeakTable<DevPulseApiFactory, WebApplicationFactory<Program>> Apps = [];

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private WebApplicationFactory<Program> App => Apps.GetValue(factory, baseFactory => baseFactory.WithWebHostBuilder(builder =>
        builder.ConfigureTestServices(services => services.AddSingleton<TimeProvider>(new FakeTimeProvider(TestClock.Now)))));

    [Fact]
    public async Task Should_ReturnDigestsFromWindowNewestFirst_When_ListRequested()
    {
        // Arrange – 2026-09-21 to pierwszy dzień okna, 2026-09-20 już poza nim
        var user = await SeedUserAsync();
        await SeedAsync(
            Digest(user.Id, new DateOnly(2026, 10, 20)),
            Digest(user.Id, new DateOnly(2026, 9, 20)),
            Digest(user.Id, new DateOnly(2026, 10, 21)),
            Digest(user.Id, new DateOnly(2026, 9, 21)));
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var list = await client.GetFromJsonAsync<List<DigestListItem>>("/api/digests", Json);

        // Assert
        list!.Select(d => d.DigestDate).Should().Equal(
            new DateOnly(2026, 10, 21),
            new DateOnly(2026, 10, 20),
            new DateOnly(2026, 9, 21));
        list![0].Priorities.Should().HaveCount(2);
        list[0].AiUsed.Should().BeTrue();
    }

    [Fact]
    public async Task Should_OmitHtmlAndMonitoringFields_When_ListSerialized()
    {
        // Arrange
        var user = await SeedUserAsync();
        await SeedAsync(Digest(user.Id, new DateOnly(2026, 10, 21)));
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var json = await client.GetStringAsync("/api/digests");

        // Assert
        json.Should().NotContainAny("htmlBody", "<html", "repositorySummary", "aiModel", "sendgrid-message-id");
        json.Should().Contain("\"status\":\"Sent\"").And.Contain("\"digestDate\":\"2026-10-21\"");
    }

    [Fact]
    public async Task Should_UseUsersLocalDate_When_TimeZoneBehindUtc()
    {
        // Arrange – w Pago Pago (UTC−11) jest jeszcze 2026-10-20, więc 2026-09-20 mieści się w oknie
        var user = await SeedUserAsync("Pacific/Pago_Pago");
        await SeedAsync(Digest(user.Id, new DateOnly(2026, 9, 20)));
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var list = await client.GetFromJsonAsync<List<DigestListItem>>("/api/digests", Json);

        // Assert
        list!.Should().ContainSingle(d => d.DigestDate == new DateOnly(2026, 9, 20));
    }

    [Fact]
    public async Task Should_ReturnDetailWithSafeError_When_DigestFailed()
    {
        // Arrange – w bazie surowy komunikat ("SendGrid 503"), do użytkownika trafia tylko bezpieczny opis
        var user = await SeedUserAsync();
        var failed = new DigestBuilder().ForUser(user.Id).ForDate(new DateOnly(2026, 10, 20)).Failed().WithoutAi().Build();
        await SeedAsync(failed);
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var detail = await client.GetFromJsonAsync<DigestDetailResponse>($"/api/digests/{failed.Id}", Json);

        // Assert
        detail!.Status.Should().Be(DigestStatus.Failed);
        detail.Error.Should().Be(DigestHistoryRules.GenericFailureMessage);
        detail.AiUsed.Should().BeFalse();
        detail.AttemptCount.Should().Be(1);
        detail.TimeZoneId.Should().Be("Europe/Warsaw");
        detail.RecipientEmail.Should().Be("octocat@example.com");
        detail.HasHtml.Should().BeTrue();
    }

    [Fact]
    public async Task Should_Return404_When_DigestOutsideWindow()
    {
        // Arrange – stary digest czeka na RetentionJob, ale nie jest już widoczny
        var user = await SeedUserAsync();
        var old = Digest(user.Id, new DateOnly(2026, 9, 1));
        await SeedAsync(old);
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var detail = await client.GetAsync($"/api/digests/{old.Id}");
        var html = await client.GetAsync($"/api/digests/{old.Id}/html");

        // Assert
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_Return404AndHideList_When_DigestBelongsToAnotherUser()
    {
        // Arrange
        var owner = await SeedUserAsync();
        var other = await SeedUserAsync();
        var digest = Digest(owner.Id, new DateOnly(2026, 10, 21));
        await SeedAsync(digest);
        using var client = await SignedInClientAsync(other.Id);

        // Act
        var list = await client.GetFromJsonAsync<List<DigestListItem>>("/api/digests", Json);
        var detail = await client.GetAsync($"/api/digests/{digest.Id}");
        var html = await client.GetAsync($"/api/digests/{digest.Id}/html");

        // Assert
        list.Should().BeEmpty();
        detail.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await detail.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.NotFound);
        html.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task Should_ReturnHtmlWithSandboxCspAndImmutableCache_When_PreviewRequested()
    {
        // Arrange
        var user = await SeedUserAsync();
        var digest = Digest(user.Id, new DateOnly(2026, 10, 21));
        await SeedAsync(digest);
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var response = await client.GetAsync($"/api/digests/{digest.Id}/html");

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/html");
        response.Content.Headers.ContentType.CharSet.Should().Be("utf-8");
        response.Headers.GetValues("Content-Security-Policy").Should().ContainSingle()
            .Which.Should().Be(DigestsController.HtmlContentSecurityPolicy);
        response.Headers.GetValues("X-Content-Type-Options").Should().ContainSingle().Which.Should().Be("nosniff");
        // HttpClient serializuje Cache-Control we własnej kolejności – sprawdzamy dyrektywy.
        var cacheControl = response.Headers.CacheControl!;
        cacheControl.Private.Should().BeTrue();
        cacheControl.MaxAge.Should().Be(TimeSpan.FromDays(1));
        cacheControl.Extensions.Should().ContainSingle(e => e.Name == "immutable");
        (await response.Content.ReadAsStringAsync()).Should().Be(digest.HtmlBody);
    }

    [Fact]
    public async Task Should_Return404WithoutImmutableCache_When_DigestHasNoHtml()
    {
        // Arrange – Pending (przed renderem) nie ma treści
        var user = await SeedUserAsync();
        var pending = new DigestBuilder().ForUser(user.Id).ForDate(new DateOnly(2026, 10, 21)).Pending().Build();
        await SeedAsync(pending);
        using var client = await SignedInClientAsync(user.Id);

        // Act
        var detail = await client.GetFromJsonAsync<DigestDetailResponse>($"/api/digests/{pending.Id}", Json);
        var html = await client.GetAsync($"/api/digests/{pending.Id}/html");

        // Assert
        detail!.HasHtml.Should().BeFalse();
        detail.Error.Should().BeNull();
        html.StatusCode.Should().Be(HttpStatusCode.NotFound);
        html.Headers.CacheControl?.ToString().Should().NotContain("immutable");
    }

    [Theory]
    [InlineData("/api/digests")]
    [InlineData("/api/digests/0192f0c4-0000-7000-8000-000000000001")]
    [InlineData("/api/digests/0192f0c4-0000-7000-8000-000000000001/html")]
    public async Task Should_Return401Unauthenticated_When_NoSession(string path)
    {
        // Arrange
        using var client = App.CreateAnonymousClient();

        // Act
        var response = await client.GetAsync(path);

        // Assert
        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await response.Content.ReadFromJsonAsync<ProblemDetails>())!.Type.Should().Be(ProblemTypes.Unauthenticated);
    }

    private static Digest Digest(Guid userId, DateOnly date) =>
        new DigestBuilder().ForUser(userId).ForDate(date).Build();

    private async Task<User> SeedUserAsync(string timeZoneId = "UTC")
    {
        var user = new UserBuilder().Build();
        await SeedAsync(user, new UserSettingsBuilder().ForUser(user.Id).WithSchedule(7, timeZoneId).Build());
        return user;
    }

    private async Task SeedAsync(params object[] entities)
    {
        await using var db = factory.CreateDbContext();
        db.AddRange(entities);
        await db.SaveChangesAsync();
    }

    private async Task<HttpClient> SignedInClientAsync(Guid userId)
    {
        var client = App.CreateAnonymousClient();
        await client.SignInAsync(userId);
        return client;
    }
}
