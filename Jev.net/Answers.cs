using System.Globalization;
using System.Text.Json.Serialization;

namespace Jev.net;

/// <summary>Base class for the three JEV answer types.</summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(NoulAnswer), "noul")]
[JsonDerivedType(typeof(ChoiceAnswer), "choice")]
[JsonDerivedType(typeof(ScoreAnswer), "score")]
public abstract class Answer
{
}

/// <summary>Answer to a <see cref="NoulQuestion"/>.</summary>
public sealed class NoulAnswer : Answer
{
    /// <summary>The yes/no answer from 0 (no) to 1 (yes).</summary>
    [JsonPropertyName("noul")]
    public double Noul { get; init; }

    /// <summary>Alias for <see cref="Noul"/>: the probability that the answer is yes.</summary>
    [JsonIgnore]
    public double Probability => Noul;

    /// <summary>True when the probability is at or above <paramref name="threshold"/>.</summary>
    public bool IsYes(double threshold = 0.5) => Noul >= threshold;

    public override string ToString() => Noul.ToString("0.###", CultureInfo.InvariantCulture);
}

/// <summary>Answer to a <see cref="ChoiceQuestion"/>.</summary>
public sealed class ChoiceAnswer : Answer
{
    /// <summary>The highest-probability option.</summary>
    [JsonPropertyName("choice")]
    public required string Choice { get; init; }

    /// <summary>Every option mapped to its probability (sums to 1).</summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double> Probabilities { get; init; } = new();

    /// <summary>How certain the model is, 0 to 1.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>Options ordered from most to least likely.</summary>
    [JsonIgnore]
    public IEnumerable<KeyValuePair<string, double>> Ranked =>
        Probabilities.OrderByDescending(kv => kv.Value);

    public override string ToString() => Choice;
}

/// <summary>Answer to a <see cref="ScoreQuestion"/>.</summary>
public sealed class ScoreAnswer : Answer
{
    /// <summary>Probability-weighted score across the levels; may land between levels.</summary>
    [JsonPropertyName("score")]
    public double Score { get; init; }

    /// <summary>Level number (as string) mapped back to its description.</summary>
    [JsonPropertyName("legend")]
    public Dictionary<string, string> Legend { get; init; } = new();

    /// <summary>Level number (as string) mapped to its probability (sums to 1).</summary>
    [JsonPropertyName("probabilities")]
    public Dictionary<string, double> Probabilities { get; init; } = new();

    /// <summary>How certain the model is, 0 to 1.</summary>
    [JsonPropertyName("confidence")]
    public double Confidence { get; init; }

    /// <summary>The single most likely level index.</summary>
    [JsonIgnore]
    public int MostLikelyLevel =>
        Probabilities.Count == 0
            ? (int)Math.Round(Score)
            : int.Parse(Probabilities.MaxBy(kv => kv.Value).Key, CultureInfo.InvariantCulture);

    /// <summary>Description of the most likely level, if the legend contains it.</summary>
    [JsonIgnore]
    public string? MostLikelyLabel =>
        Legend.TryGetValue(MostLikelyLevel.ToString(CultureInfo.InvariantCulture), out var s) ? s : null;

    public override string ToString() => Score.ToString("0.###", CultureInfo.InvariantCulture);
}
