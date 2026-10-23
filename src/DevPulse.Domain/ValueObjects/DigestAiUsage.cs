namespace DevPulse.Domain.ValueObjects;

/// <summary>Metadane wywołania AI dla digestu – monitoring kosztów. Brak = priorytety z fallbacku regułowego.</summary>
public sealed record DigestAiUsage
{
    public const int ModelMaxLength = 64;

    public DigestAiUsage(string model, int inputTokens, int outputTokens)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(model);
        ArgumentOutOfRangeException.ThrowIfNegative(inputTokens);
        ArgumentOutOfRangeException.ThrowIfNegative(outputTokens);

        if (model.Length > ModelMaxLength)
        {
            throw new ArgumentException($"Nazwa modelu ma maksymalnie {ModelMaxLength} znaków.", nameof(model));
        }

        Model = model;
        InputTokens = inputTokens;
        OutputTokens = outputTokens;
    }

    public string Model { get; }

    public int InputTokens { get; }

    public int OutputTokens { get; }
}
