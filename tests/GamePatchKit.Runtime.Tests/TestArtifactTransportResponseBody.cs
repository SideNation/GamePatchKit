using System.Text;

namespace GamePatchKit.Runtime.Tests;

// Adapters call this to decide whether an object store's error body positively confirms absence. Getting it
// wrong in either direction is costly: too strict and an unsigned release on such a host cannot install at
// all, too loose and a transport failure can be mistaken for "this release was never signed".
public class TestArtifactTransportResponseBody
{
    // The document Supabase Storage returns for a missing object, alongside the shapes another store could
    // reasonably use for the same statement.
    [Theory]
    [InlineData("{\"statusCode\":\"404\",\"error\":\"not_found\",\"message\":\"Object not found\"}")]
    [InlineData("{\"statusCode\":\"404\"}")]
    [InlineData("{\"statusCode\":404}")]
    [InlineData("{\"error\":\"not_found\",\"statusCode\":\"404\"}")]
    public void BodyStatingStatus404_ConfirmsNotFound(string body)
    {
        Assert.True(ArtifactTransportResponseBody.ConfirmsNotFound(Encoding.UTF8.GetBytes(body)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not json")]
    [InlineData("<html><body>Bad Request</body></html>")]
    [InlineData("null")]
    [InlineData("[{\"statusCode\":\"404\"}]")]
    [InlineData("\"404\"")]
    [InlineData("{\"statusCode\":\"400\"}")]
    [InlineData("{\"statusCode\":\"403\",\"error\":\"not_found\"}")]
    [InlineData("{\"error\":\"not_found\",\"message\":\"Object not found\"}")]
    [InlineData("{\"status\":\"404\"}")]
    [InlineData("{\"StatusCode\":\"404\"}")]
    [InlineData("{\"statusCode\":null}")]
    [InlineData("{\"statusCode\":true}")]
    [InlineData("{\"statusCode\":{\"value\":\"404\"}}")]
    [InlineData("{\"statusCode\":\" 404\"}")]
    [InlineData("{\"statusCode\":\"+404\"}")]
    [InlineData("{\"statusCode\":\"404.0\"}")]
    [InlineData("{\"statusCode\":\"4040\"}")]
    [InlineData("{\"statusCode\":\"404\"} trailing")]
    [InlineData("{\"statusCode\":\"404\"}{\"statusCode\":\"404\"}")]
    [InlineData("{\"statusCode\":\"400\",\"statusCode\":\"404\"}")]
    public void BodyNotStatingStatus404_DoesNotConfirmNotFound(string body)
    {
        Assert.False(ArtifactTransportResponseBody.ConfirmsNotFound(Encoding.UTF8.GetBytes(body)));
    }

    [Fact]
    public void MissingBody_DoesNotConfirmNotFound()
    {
        Assert.False(ArtifactTransportResponseBody.ConfirmsNotFound(null!));
    }

    // Invalid UTF-8 is not a document this can read, and a decoder fault must classify as "unconfirmed"
    // rather than escape into the caller, which is already throwing a transport exception.
    [Fact]
    public void BodyThatIsNotValidUtf8_DoesNotConfirmNotFound()
    {
        Assert.False(ArtifactTransportResponseBody.ConfirmsNotFound(new byte[] { 0xC3, 0x28, 0x7B, 0x7D }));
    }

    [Fact]
    public void BodyAtTheInspectionLimit_IsStillRead()
    {
        byte[] body = PaddedNotFoundBody(ArtifactTransportResponseBody.MaximumInspectedBytes);

        Assert.Equal(ArtifactTransportResponseBody.MaximumInspectedBytes, body.Length);
        Assert.True(ArtifactTransportResponseBody.ConfirmsNotFound(body));
    }

    [Fact]
    public void BodyPastTheInspectionLimit_DoesNotConfirmNotFound()
    {
        byte[] body = PaddedNotFoundBody(ArtifactTransportResponseBody.MaximumInspectedBytes + 1);

        Assert.False(ArtifactTransportResponseBody.ConfirmsNotFound(body));
    }

    // A well-formed confirmation padded out to exactly totalBytes, so the only thing separating the two limit
    // tests is the length itself.
    private static byte[] PaddedNotFoundBody(int totalBytes)
    {
        const string prefix = "{\"statusCode\":\"404\",\"message\":\"";
        const string suffix = "\"}";
        return Encoding.UTF8.GetBytes(
            prefix + new string('x', totalBytes - prefix.Length - suffix.Length) + suffix);
    }
}
