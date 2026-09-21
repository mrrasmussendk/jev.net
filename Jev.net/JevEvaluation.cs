namespace Jev.net;

/// <summary>
/// Fluent builder for a multi-question evaluation of one state.
/// </summary>
/// <example>
/// var r = await jev.Evaluate("Help! My payouts have been failing for 3 days.")
///     .Noul("is_urgent", "Does this convey urgency?")
///     .Choice("department", "Which team should handle this?", new Dictionary&lt;string, string&gt; {
///         ["billing"] = "Payments, invoicing, refunds",
///         ["technical"] = "Bugs, outages, integrations" })
///     .Score("frustration", "How frustrated is the customer?", "Calm", "Frustrated", "Very angry")
///     .SendAsync();
///
/// r.Noul("is_urgent").Noul;        // 0.95
/// r.Choice("department").Choice;   // "billing"
/// r.Score("frustration").Score;    // 1.05
/// </example>
public sealed class JevEvaluation
{
    private readonly IJevClient _client;
    private readonly object _state;
    private readonly Dictionary<string, Question> _questions = new();
    private string? _model;

    internal JevEvaluation(IJevClient client, object state)
    {
        _client = client;
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    /// <summary>Override the model for this request (default: the client's configured model).</summary>
    public JevEvaluation Model(string model)
    {
        _model = model;
        return this;
    }

    /// <summary>Add any question under <paramref name="key"/>.</summary>
    public JevEvaluation Add(string key, Question question)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(key);
        ArgumentNullException.ThrowIfNull(question);
        _questions[key] = question;
        return this;
    }

    /// <summary>Add a yes/no question.</summary>
    public JevEvaluation Noul(string key, object instructions, object? yes = null, object? no = null)
        => Add(key, new NoulQuestion(instructions, yes, no));

    /// <summary>Add a choice question with undescribed options.</summary>
    public JevEvaluation Choice(string key, object instructions, params string[] options)
        => Add(key, new ChoiceQuestion(instructions, options));

    /// <summary>Add a choice question with described options.</summary>
    public JevEvaluation Choice(string key, object instructions, IDictionary<string, string> criteria)
        => Add(key, new ChoiceQuestion(instructions, criteria));

    /// <summary>Add a choice question with described options (string, object, array or null).</summary>
    public JevEvaluation Choice(string key, object instructions, IDictionary<string, object?> criteria)
        => Add(key, new ChoiceQuestion(instructions, criteria));

    /// <summary>Add a score question with ordered levels (2 to 10).</summary>
    public JevEvaluation Score(string key, object instructions, params object[] levels)
        => Add(key, new ScoreQuestion(instructions, levels));

    /// <summary>The request that will be sent, for inspection or reuse.</summary>
    public JevRequest ToRequest(string? defaultModel = null) => new()
    {
        State = _state,
        Model = _model ?? defaultModel ?? JevOptions.DefaultModel,
        Questions = new Dictionary<string, Question>(_questions),
    };

    /// <summary>Send the evaluation.</summary>
    public Task<JevResponse> SendAsync(CancellationToken cancellationToken = default)
        => _client.EvaluateAsync(_state, _questions, _model, cancellationToken);
}
