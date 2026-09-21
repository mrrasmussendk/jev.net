using System.Text.Json;

namespace Jet.net.Tests;

[TestClass]
public class AnswerTests
{
    private const string DocExampleResponse = """
    {
      "model": "jev-1.13.0",
      "answers": {
        "is_urgent": { "type": "noul", "noul": 0.95 },
        "department": {
          "type": "choice",
          "choice": "billing",
          "probabilities": { "billing": 0.88, "technical": 0.12, "sales": 0.0 },
          "confidence": 0.81
        },
        "frustration": {
          "type": "score",
          "score": 1.05,
          "legend": { "0": "Calm", "1": "Frustrated", "2": "Very angry" },
          "probabilities": { "0": 0.0, "1": 0.95, "2": 0.05 },
          "confidence": 0.92
        }
      },
      "usage": { "input_tokens": 304, "output_tokens": 18 }
    }
    """;

    private static JevResponse Parse(string json) => JsonSerializer.Deserialize<JevResponse>(json, JevClient.JsonOptions)!;

    [TestMethod]
    public void Response_ParsesModelAndUsage()
    {
        var r = Parse(DocExampleResponse);
        Assert.AreEqual("jev-1.13.0", r.Model);
        Assert.AreEqual(304, r.Usage.InputTokens);
        Assert.AreEqual(18, r.Usage.OutputTokens);
        Assert.AreEqual(3, r.Answers.Count);
    }

    [TestMethod]
    public void NoulAnswer_Parses()
    {
        var a = Parse(DocExampleResponse).Noul("is_urgent");
        Assert.AreEqual(0.95, a.Noul, 1e-9);
        Assert.AreEqual(0.95, a.Probability, 1e-9);
        Assert.IsTrue(a.IsYes());
        Assert.IsFalse(a.IsYes(0.99));
        Assert.AreEqual("0.95", a.ToString());
    }

    [TestMethod]
    public void ChoiceAnswer_Parses()
    {
        var a = Parse(DocExampleResponse).Choice("department");
        Assert.AreEqual("billing", a.Choice);
        Assert.AreEqual(0.81, a.Confidence, 1e-9);
        Assert.AreEqual(0.88, a.Probabilities["billing"], 1e-9);
        Assert.AreEqual(0.0, a.Probabilities["sales"], 1e-9);
        CollectionAssert.AreEqual(new[] { "billing", "technical", "sales" }, a.Ranked.Select(kv => kv.Key).ToArray());
        Assert.AreEqual("billing", a.ToString());
    }

    [TestMethod]
    public void ScoreAnswer_Parses()
    {
        var a = Parse(DocExampleResponse).Score("frustration");
        Assert.AreEqual(1.05, a.Score, 1e-9);
        Assert.AreEqual(0.92, a.Confidence, 1e-9);
        Assert.AreEqual("Calm", a.Legend["0"]);
        Assert.AreEqual(0.95, a.Probabilities["1"], 1e-9);
        Assert.AreEqual(1, a.MostLikelyLevel);
        Assert.AreEqual("Frustrated", a.MostLikelyLabel);
        Assert.AreEqual("1.05", a.ToString());
    }

    [TestMethod]
    public void ScoreAnswer_MostLikelyLevelFallsBackToRoundedScoreWithoutProbabilities()
    {
        var a = new ScoreAnswer { Score = 1.6 };
        Assert.AreEqual(2, a.MostLikelyLevel);
        Assert.IsNull(a.MostLikelyLabel);
    }

    [TestMethod]
    public void Answers_TypeDiscriminatorMayAppearOutOfOrder()
    {
        var r = Parse("""
        { "model": "m", "answers": { "x": { "choice": "a", "probabilities": { "a": 1.0 }, "confidence": 1.0, "type": "choice" } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
        """);
        Assert.AreEqual("a", r.Choice("x").Choice);
    }

    [TestMethod]
    public void Answers_UnknownExtraFieldsAreIgnored()
    {
        var r = Parse("""
        { "model": "m", "answers": { "x": { "type": "noul", "noul": 0.5, "future_field": 123 } }, "usage": { "input_tokens": 1, "output_tokens": 1 }, "extra": true }
        """);
        Assert.AreEqual(0.5, r.Noul("x").Noul, 1e-9);
    }

    [TestMethod]
    public void Answers_IntegerProbabilitiesParseAsDoubles()
    {
        var r = Parse("""
        { "model": "m", "answers": { "x": { "type": "noul", "noul": 1 } }, "usage": { "input_tokens": 1, "output_tokens": 1 } }
        """);
        Assert.AreEqual(1.0, r.Noul("x").Noul, 1e-9);
    }

    [TestMethod]
    public void Response_GetThrowsHelpfulErrorForMissingKey()
    {
        var r = Parse(DocExampleResponse);
        var ex = Assert.ThrowsExactly<KeyNotFoundException>(() => r.Noul("nope"));
        StringAssert.Contains(ex.Message, "nope");
        StringAssert.Contains(ex.Message, "is_urgent");
    }

    [TestMethod]
    public void Response_GetThrowsForWrongType()
    {
        var r = Parse(DocExampleResponse);
        var ex = Assert.ThrowsExactly<InvalidCastException>(() => r.Choice("is_urgent"));
        StringAssert.Contains(ex.Message, "NoulAnswer");
        StringAssert.Contains(ex.Message, "ChoiceAnswer");
    }

    [TestMethod]
    public void Response_IndexerAndGenericGetReturnBaseAnswer()
    {
        var r = Parse(DocExampleResponse);
        Assert.IsInstanceOfType<ScoreAnswer>(r["frustration"]);
        Assert.IsInstanceOfType<NoulAnswer>(r.Get<Answer>("is_urgent"));
    }
}
