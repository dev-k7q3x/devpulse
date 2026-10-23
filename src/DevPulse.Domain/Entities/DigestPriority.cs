using DevPulse.Domain.Enums;

namespace DevPulse.Domain.Entities;

/// <summary>Jeden priorytet digestu: jedno zdanie z odwołaniem do repo (jsonb, maks. <see cref="Digest.MaxPriorities"/>).</summary>
public sealed class DigestPriority
{
    public DigestPriority(string repoFullName, string text, PrioritySeverity severity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repoFullName);
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        RepoFullName = repoFullName;
        Text = text;
        Severity = severity;
    }

    public string RepoFullName { get; private set; }

    public string Text { get; private set; }

    public PrioritySeverity Severity { get; private set; }
}
