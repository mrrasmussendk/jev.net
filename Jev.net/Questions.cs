using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;

namespace Jev.net;

/// <summary>Base class for the three JEV question types.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulQuestion), "noul")]
[JsonDerivedType(typeof(ChoiceQuestion), "choice")]
[JsonDerivedType(typeof(ScoreQuestion), "score")]
public abstract class Question
{
    /// <summary>
    /// What to evaluate. A string, or a structured object/array (for example an anonymous object
    /// with the question in one field and referenced data in others).
    /// </summary>
    [JsonPropertyName("instructions")]
    public required object Instructions { get; init; }
}

/// <summary>Optional descriptions of what a yes and a no mean for a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulCriteria
{
    [JsonPropertyName("true")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? True { get; init; }

    [JsonPropertyName("false")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public object? False { get; init; }
}

/// <summary>A yes/no question. Answered with the probability that the answer is yes.</summary>
public sealed class NoulQuestion : Question
{
    [JsonPropertyName("criteria")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public NoulCriteria? Criteria { get; init; }

    public NoulQuestion() { }

    [SetsRequiredMembers]
    public NoulQuestion(object instructions, object? yes = null, object? no = null)
    {
        Instructions = instructions;
        if (yes is not null || no is not null)
            Criteria = new NoulCriteria { True = yes, False = no };
    }
}

/// <summary>Picks one option from a set you define (max 255 options).</summary>
public sealed class ChoiceQuestion : Question
{
    /// <summary>Option name to rubric description. Use <c>null</c> when an option needs no detail.</summary>
    [JsonPropertyName("criteria")]
    public required IDictionary<string, object?> Criteria { get; init; }

    public ChoiceQuestion() { }

    /// <summary>Options without descriptions.</summary>
    [SetsRequiredMembers]
    public ChoiceQuestion(object instructions, params string[] options)
    {
        Instructions = instructions;
        Criteria = options.ToDictionary(o => o, _ => (object?)null);
    }

    /// <summary>Options with descriptions (string, object or array; null for none).</summary>
    [SetsRequiredMembers]
    public ChoiceQuestion(object instructions, IDictionary<string, object?> criteria)
    {
        Instructions = instructions;
        Criteria = criteria;
    }

    /// <summary>Options with string descriptions.</summary>
    [SetsRequiredMembers]
    public ChoiceQuestion(object instructions, IDictionary<string, string> criteria)
    {
        Instructions = instructions;
        Criteria = criteria.ToDictionary(kv => kv.Key, kv => (object?)kv.Value);
    }
}

/// <summary>Rates the state along an ordered rubric of 2 to 10 levels.</summary>
public sealed class ScoreQuestion : Question
{
    /// <summary>Ordered level descriptions (index 0 is the lowest level).</summary>
    [JsonPropertyName("criteria")]
    public required IList<object> Criteria { get; init; }

    public ScoreQuestion() { }

    [SetsRequiredMembers]
    public ScoreQuestion(object instructions, params object[] levels)
    {
        Instructions = instructions;
        Criteria = levels.ToList();
    }
}
