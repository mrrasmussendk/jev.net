using System.Net;

namespace Jev.net.Tests;

[TestClass]
public class JevApiExceptionTests
{
    [TestMethod]
    public void StatusFlags()
    {
        Assert.IsTrue(new JevApiException(HttpStatusCode.Unauthorized, "").IsUnauthorized);
        Assert.IsTrue(new JevApiException(HttpStatusCode.UnprocessableEntity, "").IsValidationError);
        Assert.IsTrue(new JevApiException(HttpStatusCode.TooManyRequests, "").IsRateLimited);
        Assert.IsTrue(new JevApiException((HttpStatusCode)529, "").IsOverloaded);

        var ok = new JevApiException(HttpStatusCode.InternalServerError, "");
        Assert.IsFalse(ok.IsUnauthorized);
        Assert.IsFalse(ok.IsValidationError);
        Assert.IsFalse(ok.IsRateLimited);
        Assert.IsFalse(ok.IsOverloaded);
    }

    [TestMethod]
    public void DefaultMessage_IncludesStatusAndBody()
    {
        var ex = new JevApiException(HttpStatusCode.TooManyRequests, """{"error":"slow down"}""");
        StringAssert.Contains(ex.Message, "429");
        StringAssert.Contains(ex.Message, "TooManyRequests");
        StringAssert.Contains(ex.Message, "slow down");
        Assert.AreEqual("""{"error":"slow down"}""", ex.ResponseBody);
    }

    [TestMethod]
    public void CustomMessage_IsUsedVerbatim()
    {
        var ex = new JevApiException(HttpStatusCode.OK, "body", "custom");
        Assert.AreEqual("custom", ex.Message);
        Assert.AreEqual("body", ex.ResponseBody);
    }

    [TestMethod]
    public void IsAnException()
    {
        Assert.IsInstanceOfType<Exception>(new JevApiException(HttpStatusCode.OK, ""));
    }
}
