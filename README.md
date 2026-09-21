# Jev.net

A small, dependency-light .NET client for the TypeSafe **JEV** evaluation endpoint
(`POST https://api.typesafe.ai/v1/systemone`). Built on `HttpClient` and `System.Text.Json`;
the only package is `Microsoft.Extensions.DependencyInjection.Abstractions` so `services.AddJev(...)` works.

## Set your API key

Pick whichever fits:

```csharp
// 1. Dependency injection (ASP.NET Core, Worker, etc.)
builder.Services.AddJev("sk-...");
builder.Services.AddJev(o => { o.ApiKey = builder.Configuration["Jev:ApiKey"]; o.MaxRetries = 3; });
builder.Services.AddJev();                       // reads JEV_API_KEY or TYPESAFE_API_KEY

// 2. A client instance
var jev = new JevClient("sk-...");
var jev = new JevClient(new JevOptions { ApiKey = "sk-...", Model = "jev-latest" });

// 3. Static, zero-setup
Jev.ApiKey = "sk-...";                           // or just set the JEV_API_KEY env var
```

Inject `IJevClient` wherever you need it:

```csharp
public class TriageService(IJevClient jev)
{
    public Task<string> RouteAsync(string ticket) =>
        jev.ChooseAsync(ticket, "Which team should handle this?", "billing", "technical", "sales");
}
```

## Ask simple questions (one line each)

```csharp
string text = "Help! My payouts have been failing for 3 days.";

double p      = await jev.AskAsync(text, "Does this convey urgency?");          // 0.95 (probability of yes)
bool urgent   = await jev.YesNoAsync(text, "Does this convey urgency?");        // true  (p >= 0.5)
string team   = await jev.ChooseAsync(text, "Which team?", "billing", "technical", "sales");   // "billing"
double anger  = await jev.ScoreAsync(text, "How frustrated is the customer?", "Calm", "Frustrated", "Very angry"); // 1.05
```

Same thing through the static facade: `await Jev.AskAsync(text, "Is this urgent?")`.

Need the full answer (probabilities, confidence, legend)?

```csharp
ChoiceAnswer c = await jev.ChooseFullAsync(text, "Which team?", new Dictionary<string, object?> {
    ["billing"] = "Payments, invoicing, refunds",
    ["technical"] = "Bugs, outages, integrations",
    ["sales"] = null });
c.Choice; c.Probabilities["billing"]; c.Confidence; c.Ranked;

ScoreAnswer s = await jev.ScoreFullAsync(text, "How frustrated?", ["Calm", "Frustrated", "Very angry"]);
s.Score; s.MostLikelyLabel; s.Legend; s.Probabilities; s.Confidence;
```

## Several questions in one request

```csharp
var r = await jev.Evaluate(text)
    .Noul("is_urgent", "Does this convey urgency?", yes: "Explicitly time-sensitive", no: "No urgency expressed")
    .Choice("department", "Which team should handle this?", new Dictionary<string, string> {
        ["billing"] = "Payments, invoicing, refunds",
        ["technical"] = "Bugs, outages, integrations",
        ["sales"] = "Pricing, upgrades, new accounts" })
    .Score("frustration", "How frustrated is the customer?", "Calm", "Frustrated", "Very angry")
    .SendAsync();

r.Noul("is_urgent").Noul;        // 0.95
r.Choice("department").Choice;   // "billing"
r.Score("frustration").Score;    // 1.05
r.Model; r.Usage.InputTokens;
```

## Structured state and instructions

`state` and `instructions` accept any object or array and are serialized verbatim:

```csharp
var state = new[] { new { role = "user", content = "..." }, new { role = "assistant", content = "..." } };

double same = await jev.AskAsync(resumeText, new {
    potential_duplicate = new { name = "John Smith", location = "Oakland, California" },
    question = "Is the resume for the same person as `potential_duplicate`?" });
```

For full control build a `JevRequest` yourself and call `EvaluateAsync(request)`.

## Errors and retries

- `429` and `529` (plus `502/503/504`, timeouts and connection errors) are retried with exponential backoff and jitter,
  honouring `Retry-After`. Tune with `JevOptions.MaxRetries`, `InitialRetryDelay`, `MaxRetryDelay`, `Timeout`.
- Everything else throws `JevApiException` with `StatusCode`, `ResponseBody` and helpers
  `IsUnauthorized` (401), `IsValidationError` (422), `IsRateLimited` (429), `IsOverloaded` (529).

## Tests

```
dotnet test
```

## Releasing to NuGet

The package id is `Jev.net`. The version is never hard-coded in the csproj for a release; it comes from the tag or the command line.

**Option 1: tag and let GitHub Actions publish (recommended).**
No secrets are stored: the workflow uses NuGet Trusted Publishing. A trusted publisher policy on nuget.org (Account > API keys > Trusted publishing) is bound to repository `mrrasmussendk/jev.net`, workflow `release.yml` and environment `production`; the `NuGet/login` action exchanges the job's OIDC token for a short-lived API key. Then:

```
git tag v1.2.3
git push origin v1.2.3
```

The `Release to NuGet` workflow builds, tests, packs `Jev.net.1.2.3.nupkg` plus a `.snupkg` symbol package, pushes both to nuget.org, and creates a GitHub release with generated notes and the packages attached. You can also start it by hand from the Actions tab with a version number.

Note: the policy's scope must allow creating new packages for the very first release ("Push new packages and package versions"). "Push only new package versions" is enough once `Jev.net` exists on nuget.org.

**Option 2: from your machine.**

```powershell
./release.ps1 -Version 1.2.3                  # build, test, pack into ./artifacts
./release.ps1 -Version 1.2.3 -Push            # also push, using $env:NUGET_API_KEY
./release.ps1 -Version 1.2.3-beta.1 -Push     # pre-release
```

**Option 3: plain dotnet CLI.**

```
dotnet pack Jev.net/Jev.net.csproj -c Release -p:Version=1.2.3 -o artifacts
dotnet nuget push artifacts/Jev.net.1.2.3.nupkg --api-key <key> --source https://api.nuget.org/v3/index.json
```

**Publishing to GitHub Packages instead** (private or pre-release feed):

```
dotnet nuget push artifacts/Jev.net.1.2.3.nupkg --api-key <github token with write:packages> --source https://nuget.pkg.github.com/mrrasmussendk/index.json
```

Every push to `main`/`master` and every pull request also runs the `CI` workflow, which builds, tests and packs (without publishing) so packaging breakage is caught early.

