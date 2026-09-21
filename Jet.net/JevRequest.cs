using System.Text.Json.Serialization;

namespace Jet.net;

/// <summary>The full request body for the evaluation endpoint.</summary>
public sealed class JevRequest
{
    /// <summary>A plain string, or structured data (object/array) such as a chat log or record.</summary>
    [JsonPropertyName("state")]
    public required object State { get; init; }

    [JsonPropertyName("model")]
    public string Model { get; init; } = JevOptions.DefaultModel;

    /// <summary>Keyed questions; answers come back under the same keys.</summary>
    [JsonPropertyName("questions")]
    public Dictionary<string, Question> Questions { get; init; } = new();
}

/// <summary>Token usage for one request.</summary>
public sealed class JevUsage
{
    [JsonPropertyName("input_tokens")]
    public int InputTokens { get; init; }

    [JsonPropertyName("output_tokens")]
    public int OutputTokens { get; init; }
}

/// <summary>The full response body from the evaluation endpoint.</summary>
public sealed class JevResponse
{
    /// <summary>The concrete model that performed the evaluation (for example "jev-1.13.0").</summary>
    [JsonPropertyName("model")]
    public string Model { get; init; } = "";

    [JsonPropertyName("answers")]
    public Dictionary<string, Answer> Answers { get; init; } = new();

    [JsonPropertyName("usage")]
    public JevUsage Usage { get; init; } = new();

    /// <summary>Gets the answer under <paramref name="key"/> as <typeparamref name="T"/>.</summary>
    public T Get<T>(string key) where T : Answer
    {
        if (!Answers.TryGetValue(key, out var answer))
            throw new KeyNotFoundException($"No answer with key '{key}'. Keys: {string.Join(", ", Answers.Keys)}");
        return answer as T
               ?? throw new InvalidCastException($"Answer '{key}' is a {answer.GetType().Name}, not a {typeof(T).Name}.");
    }

    public NoulAnswer Noul(string key) => Get<NoulAnswer>(key);
    public ChoiceAnswer Choice(string key) => Get<ChoiceAnswer>(key);
    public ScoreAnswer Score(string key) => Get<ScoreAnswer>(key);

    public Answer this[string key] => Get<Answer>(key);
}
