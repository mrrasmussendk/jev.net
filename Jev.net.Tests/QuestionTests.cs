using System.Text.Json;

namespace Jev.net.Tests;

[TestClass]
public class QuestionTests
{
    private static JsonElement Serialize(Question q)
        => JsonDocument.Parse(JsonSerializer.Serialize(q, JevClient.JsonOptions)).RootElement;

    [TestMethod]
    public void Noul_SerializesTypeAndInstructions()
    {
        var el = Serialize(new NoulQuestion("Does this convey urgency?"));

        Assert.AreEqual("noul", el.GetProperty("type").GetString());
        Assert.AreEqual("Does this convey urgency?", el.GetProperty("instructions").GetString());
        Assert.IsFalse(el.TryGetProperty("criteria", out _), "criteria must be omitted when not set");
    }

    [TestMethod]
    public void Noul_SerializesCriteriaWithTrueFalseKeys()
    {
        var el = Serialize(new NoulQuestion("Urgent?", yes: "Explicitly time-sensitive", no: "No urgency expressed"));

        var criteria = el.GetProperty("criteria");
        Assert.AreEqual("Explicitly time-sensitive", criteria.GetProperty("true").GetString());
        Assert.AreEqual("No urgency expressed", criteria.GetProperty("false").GetString());
    }

    [TestMethod]
    public void Noul_OmitsMissingHalfOfCriteria()
    {
        var el = Serialize(new NoulQuestion("Urgent?", yes: "Time-sensitive"));

        var criteria = el.GetProperty("criteria");
        Assert.AreEqual("Time-sensitive", criteria.GetProperty("true").GetString());
        Assert.IsFalse(criteria.TryGetProperty("false", out _));
    }

    [TestMethod]
    public void Noul_ObjectInitializerWorks()
    {
        var q = new NoulQuestion { Instructions = "x", Criteria = new NoulCriteria { True = "a", False = "b" } };
        var el = Serialize(q);
        Assert.AreEqual("a", el.GetProperty("criteria").GetProperty("true").GetString());
    }

    [TestMethod]
    public void Instructions_StructuredObjectIsSerializedVerbatim()
    {
        var q = new NoulQuestion(new
        {
            potential_duplicate = new { name = "John Smith", location = "Oakland, California", CamelCase = 1 },
            question = "Is the resume for the same person as `potential_duplicate`?",
        });

        var ins = Serialize(q).GetProperty("instructions");
        Assert.AreEqual("John Smith", ins.GetProperty("potential_duplicate").GetProperty("name").GetString());
        Assert.AreEqual(1, ins.GetProperty("potential_duplicate").GetProperty("CamelCase").GetInt32(), "property names must not be renamed");
        StringAssert.Contains(ins.GetProperty("question").GetString(), "`potential_duplicate`");
    }

    [TestMethod]
    public void Instructions_ArrayIsSupported()
    {
        var el = Serialize(new NoulQuestion(new object[] { "Is it urgent?", new { hint = "look at dates" } }));
        Assert.AreEqual(JsonValueKind.Array, el.GetProperty("instructions").ValueKind);
        Assert.AreEqual(2, el.GetProperty("instructions").GetArrayLength());
    }

    [TestMethod]
    public void Choice_ParamsOptionsProduceNullCriteria()
    {
        var el = Serialize(new ChoiceQuestion("Which team?", "billing", "technical", "sales"));

        Assert.AreEqual("choice", el.GetProperty("type").GetString());
        var criteria = el.GetProperty("criteria");
        Assert.AreEqual(JsonValueKind.Null, criteria.GetProperty("billing").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, criteria.GetProperty("technical").ValueKind);
        Assert.AreEqual(JsonValueKind.Null, criteria.GetProperty("sales").ValueKind);
        Assert.AreEqual(3, criteria.EnumerateObject().Count());
    }

    [TestMethod]
    public void Choice_StringDescriptions()
    {
        var el = Serialize(new ChoiceQuestion("Which team?", new Dictionary<string, string>
        {
            ["billing"] = "Payments, invoicing, refunds",
            ["technical"] = "Bugs, outages, integrations",
        }));

        Assert.AreEqual("Payments, invoicing, refunds", el.GetProperty("criteria").GetProperty("billing").GetString());
    }

    [TestMethod]
    public void Choice_MixedDescriptions()
    {
        var el = Serialize(new ChoiceQuestion("Which?", new Dictionary<string, object?>
        {
            ["a"] = null,
            ["b"] = "text",
            ["c"] = new { detail = "structured" },
            ["d"] = new[] { "x", "y" },
        }));

        var criteria = el.GetProperty("criteria");
        Assert.AreEqual(JsonValueKind.Null, criteria.GetProperty("a").ValueKind);
        Assert.AreEqual("text", criteria.GetProperty("b").GetString());
        Assert.AreEqual("structured", criteria.GetProperty("c").GetProperty("detail").GetString());
        Assert.AreEqual(2, criteria.GetProperty("d").GetArrayLength());
    }

    [TestMethod]
    public void Choice_PreservesOptionOrder()
    {
        var el = Serialize(new ChoiceQuestion("Which?", "z", "a", "m"));
        CollectionAssert.AreEqual(new[] { "z", "a", "m" }, el.GetProperty("criteria").EnumerateObject().Select(p => p.Name).ToArray());
    }

    [TestMethod]
    public void Score_SerializesOrderedLevels()
    {
        var el = Serialize(new ScoreQuestion("How frustrated?", "Calm", "Frustrated", "Very angry"));

        Assert.AreEqual("score", el.GetProperty("type").GetString());
        var levels = el.GetProperty("criteria").EnumerateArray().Select(x => x.GetString()).ToArray();
        CollectionAssert.AreEqual(new[] { "Calm", "Frustrated", "Very angry" }, levels);
    }

    [TestMethod]
    public void Score_SupportsStructuredLevels()
    {
        var el = Serialize(new ScoreQuestion("Rate", new { label = "low", detail = "..." }, "high"));
        var arr = el.GetProperty("criteria");
        Assert.AreEqual("low", arr[0].GetProperty("label").GetString());
        Assert.AreEqual("high", arr[1].GetString());
    }

    [TestMethod]
    public void PolymorphicDictionary_EmitsCorrectDiscriminatorPerEntry()
    {
        var questions = new Dictionary<string, Question>
        {
            ["n"] = new NoulQuestion("a"),
            ["c"] = new ChoiceQuestion("b", "x", "y"),
            ["s"] = new ScoreQuestion("c", "lo", "hi"),
        };

        var el = JsonDocument.Parse(JsonSerializer.Serialize(questions, JevClient.JsonOptions)).RootElement;
        Assert.AreEqual("noul", el.GetProperty("n").GetProperty("type").GetString());
        Assert.AreEqual("choice", el.GetProperty("c").GetProperty("type").GetString());
        Assert.AreEqual("score", el.GetProperty("s").GetProperty("type").GetString());
    }

    [TestMethod]
    public void Questions_RoundTripThroughJson()
    {
        var json = """
        {
          "n": { "type": "noul", "instructions": "a", "criteria": { "true": "t", "false": "f" } },
          "c": { "type": "choice", "instructions": "b", "criteria": { "x": "desc", "y": null } },
          "s": { "type": "score", "instructions": "c", "criteria": ["lo", "hi"] }
        }
        """;

        var questions = JsonSerializer.Deserialize<Dictionary<string, Question>>(json, JevClient.JsonOptions)!;
        Assert.IsInstanceOfType<NoulQuestion>(questions["n"]);
        Assert.IsInstanceOfType<ChoiceQuestion>(questions["c"]);
        Assert.IsInstanceOfType<ScoreQuestion>(questions["s"]);
        Assert.AreEqual(2, ((ChoiceQuestion)questions["c"]).Criteria.Count);
        Assert.AreEqual(2, ((ScoreQuestion)questions["s"]).Criteria.Count);
    }

    [TestMethod]
    public void Request_SerializesTopLevelShape()
    {
        var request = new JevRequest
        {
            State = new[] { new { role = "user", content = "hi" } },
            Questions = { ["q"] = new NoulQuestion("x") },
        };

        var el = JsonDocument.Parse(JsonSerializer.Serialize(request, JevClient.JsonOptions)).RootElement;
        Assert.AreEqual("jev-latest", el.GetProperty("model").GetString());
        Assert.AreEqual(JsonValueKind.Array, el.GetProperty("state").ValueKind);
        Assert.AreEqual("hi", el.GetProperty("state")[0].GetProperty("content").GetString());
        Assert.AreEqual("noul", el.GetProperty("questions").GetProperty("q").GetProperty("type").GetString());
    }
}
