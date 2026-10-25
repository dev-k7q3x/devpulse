using System.Collections.Concurrent;
using Hangfire;
using Hangfire.Common;
using Hangfire.States;

namespace DevPulse.Tests.Integration.TestSupport;

/// <summary>Zapisuje enqueue'owane joby zamiast wysyłać je do Hangfire – testy endpointów nie uruchamiają jobów.</summary>
public sealed class RecordingBackgroundJobClient : IBackgroundJobClient
{
    private readonly ConcurrentQueue<Job> _jobs = new();

    public IReadOnlyCollection<Job> Jobs => _jobs;

    public string Create(Job job, IState state)
    {
        _jobs.Enqueue(job);
        return Guid.NewGuid().ToString();
    }

    public bool ChangeState(string jobId, IState state, string expectedState) => true;
}
