namespace Jet.net;

/// <summary>
/// Client for the TypeSafe JEV evaluation endpoint. Inject this in your services; see
/// <see cref="JevServiceCollectionExtensions.AddJev(Microsoft.Extensions.DependencyInjection.IServiceCollection, string)"/>.
/// </summary>
public interface IJevClient
{
    /// <summary>Send a fully specified request and get the raw typed response.</summary>
    Task<JevResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken = default);

    /// <summary>Evaluate <paramref name="state"/> against a set of keyed questions.</summary>
    Task<JevResponse> EvaluateAsync(object state, IDictionary<string, Question> questions, string? model = null, CancellationToken cancellationToken = default);

    /// <summary>Start a fluent multi-question evaluation of <paramref name="state"/>.</summary>
    JevEvaluation Evaluate(object state);

    // ---- quick single-question helpers ----

    /// <summary>Yes/no question. Returns the probability (0..1) that the answer is yes.</summary>
    Task<double> AskAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default);

    /// <summary>Yes/no question. Returns true when the probability is at or above <see cref="JevOptions.YesThreshold"/>.</summary>
    Task<bool> YesNoAsync(object state, object question, object? yes = null, object? no = null, CancellationToken cancellationToken = default);

    /// <summary>Pick one of <paramref name="options"/>. Returns the chosen option.</summary>
    Task<string> ChooseAsync(object state, object question, params string[] options);

    /// <summary>Pick one of <paramref name="options"/> with cancellation. Returns the chosen option.</summary>
    Task<string> ChooseAsync(object state, object question, IEnumerable<string> options, CancellationToken cancellationToken = default);

    /// <summary>Pick one option, each with a rubric description. Returns the chosen option.</summary>
    Task<string> ChooseAsync(object state, object question, IDictionary<string, string> criteria, CancellationToken cancellationToken = default);

    /// <summary>Pick one option and get the full answer (probabilities and confidence).</summary>
    Task<ChoiceAnswer> ChooseFullAsync(object state, object question, IDictionary<string, object?> criteria, CancellationToken cancellationToken = default);

    /// <summary>Rate along ordered <paramref name="levels"/>. Returns the probability-weighted score (0 = first level).</summary>
    Task<double> ScoreAsync(object state, object question, params object[] levels);

    /// <summary>Rate along ordered <paramref name="levels"/> with cancellation. Returns the probability-weighted score.</summary>
    Task<double> ScoreAsync(object state, object question, IEnumerable<object> levels, CancellationToken cancellationToken = default);

    /// <summary>Rate along ordered <paramref name="levels"/> and get the full answer (legend, probabilities, confidence).</summary>
    Task<ScoreAnswer> ScoreFullAsync(object state, object question, IEnumerable<object> levels, CancellationToken cancellationToken = default);
}
