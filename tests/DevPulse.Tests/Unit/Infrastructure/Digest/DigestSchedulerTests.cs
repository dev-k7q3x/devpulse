using System.Diagnostics;
using DevPulse.Infrastructure.Jobs;
using DevPulse.Tests.Builders;
using FluentAssertions;
using Hangfire;
using Hangfire.Common;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Moq;

namespace DevPulse.Tests.Unit.Infrastructure.Digest;

/// <summary>DigestScheduler (hosted service): rejestracja recurring joba ponawiana, dopóki storage Hangfire nie odpowie.</summary>
[Trait("Category", "Unit")]
public class DigestSchedulerTests
{
    private readonly FakeTimeProvider _timeProvider = new(TestClock.Now);
    private readonly Mock<IRecurringJobManager> _manager = new();
    private int _calls;

    [Fact]
    public async Task Should_RegisterHourlyRecurringJob_When_StorageAvailable()
    {
        // Arrange
        _manager.Setup(m => m.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
            .Callback(() => Interlocked.Increment(ref _calls));
        using var scheduler = CreateScheduler();

        // Act
        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref _calls) >= 1);
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        _manager.Verify(m => m.AddOrUpdate(
            DigestSchedulerJob.RecurringJobId,
            It.Is<Job>(job => job.Type == typeof(DigestSchedulerJob) && job.Method.Name == nameof(DigestSchedulerJob.RunAsync)),
            DigestSchedulerJob.Cron,
            It.IsAny<RecurringJobOptions>()), Times.Once);
    }

    [Fact]
    public async Task Should_RetryRegistration_When_StorageUnavailableAtStartup()
    {
        // Arrange – pierwsza próba: baza nie odpowiada
        _manager.Setup(m => m.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
            .Callback(() =>
            {
                if (Interlocked.Increment(ref _calls) == 1)
                {
                    throw new InvalidOperationException("Brak połączenia z bazą.");
                }
            });
        using var scheduler = CreateScheduler();

        // Act
        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref _calls) >= 1);
        await AdvanceUntilAsync(() => Volatile.Read(ref _calls) >= 2);
        await scheduler.StopAsync(CancellationToken.None);

        // Assert
        Volatile.Read(ref _calls).Should().Be(2);
    }

    [Fact]
    public async Task Should_StopQuietly_When_HostStopsWhileWaitingForRetry()
    {
        // Arrange – storage cały czas niedostępny
        _manager.Setup(m => m.AddOrUpdate(It.IsAny<string>(), It.IsAny<Job>(), It.IsAny<string>(), It.IsAny<RecurringJobOptions>()))
            .Callback(() =>
            {
                Interlocked.Increment(ref _calls);
                throw new InvalidOperationException("Brak połączenia z bazą.");
            });
        using var scheduler = CreateScheduler();
        await scheduler.StartAsync(CancellationToken.None);
        await WaitUntilAsync(() => Volatile.Read(ref _calls) >= 1);

        // Act
        var act = () => scheduler.StopAsync(CancellationToken.None);

        // Assert
        await act.Should().NotThrowAsync();
    }

    private DigestScheduler CreateScheduler()
    {
        var services = new ServiceCollection().AddSingleton(_manager.Object).BuildServiceProvider();
        return new DigestScheduler(services, _timeProvider, NullLogger<DigestScheduler>.Instance);
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "warunek powinien spełnić się od razu");
            await Task.Delay(10);
        }
    }

    /// <summary>Przesuwa zegar o opóźnienie ponowienia, aż warunek się spełni (timer mógł nie być jeszcze zarejestrowany).</summary>
    private async Task AdvanceUntilAsync(Func<bool> condition)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!condition())
        {
            stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5), "ponowienie powinno nastąpić po upływie RetryDelay");
            _timeProvider.Advance(DigestScheduler.RetryDelay);
            await Task.Delay(10);
        }
    }
}
