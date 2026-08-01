using System;
using System.Globalization;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Runtime
{
    // Some object stores answer a request for a missing object with a status other than 404 and put the real
    // status in the response body. Supabase Storage returns HTTP 400 with
    // {"statusCode":"404","error":"not_found","message":"Object not found"}.
    //
    // Classifying that as an unconfirmed failure makes every unsigned release on such a host unusable:
    // ReadManifestSignatureBytesAsync only tolerates a missing manifest.sig when the adapter positively
    // confirms absence, so a 400 that nobody recognizes fails the whole install with runtime.transport-failed.
    //
    // Keying on the store's own statement of the status - rather than on the bare 400, which also covers
    // genuinely malformed requests - is what keeps this inside the IsNotFound contract. It grants nothing new
    // to an attacker either: whatever can inject this body can inject a real 404 just as easily, which is the
    // threat requireSignature exists to answer.
    public static class ArtifactTransportResponseBody
    {
        // Error bodies of this shape are a few hundred bytes. Anything larger is not one of them, and an
        // adapter must not buffer an unbounded body just to classify a failure it is already going to throw.
        public const int MaximumInspectedBytes = 4096;

        private const int NotFoundStatusCode = 404;
        private const string StatusCodePropertyName = "statusCode";

        private static readonly UTF8Encoding _strictUtf8 =
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

        // True only when the body is a single JSON object whose "statusCode" is 404. An empty body, HTML, JSON
        // without that property, or anything over MaximumInspectedBytes is not a confirmation and must leave
        // IsNotFound false.
        public static bool ConfirmsNotFound(byte[] responseBody)
        {
            if (responseBody == null
                || responseBody.Length == 0
                || responseBody.Length > MaximumInspectedBytes)
            {
                return false;
            }

            return TryReadObject(responseBody, out JObject? root)
                && TryGetStatusCode(root!, out int statusCode)
                && statusCode == NotFoundStatusCode;
        }

        private static bool TryReadObject(byte[] bytes, out JObject? root)
        {
            root = null;

            try
            {
                string json = _strictUtf8.GetString(bytes);
                using var stringReader = new StringReader(json);
                using var reader = new JsonTextReader(stringReader)
                {
                    DateParseHandling = DateParseHandling.None,
                    FloatParseHandling = FloatParseHandling.Decimal,
                    SupportMultipleContent = false,
                };
                JToken token = JToken.ReadFrom(
                    reader,
                    new JsonLoadSettings
                    {
                        CommentHandling = CommentHandling.Ignore,
                        DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error,
                    });

                if (token.Type != JTokenType.Object || reader.Read())
                {
                    return false;
                }

                root = (JObject)token;
                return true;
            }
            catch (Exception exception) when (
                exception is DecoderFallbackException
                || exception is JsonException
                || exception is InvalidOperationException)
            {
                return false;
            }
        }

        private static bool TryGetStatusCode(JObject root, out int statusCode)
        {
            statusCode = 0;

            if (!root.TryGetValue(StatusCodePropertyName, StringComparison.Ordinal, out JToken? token))
            {
                return false;
            }

            // Supabase quotes the status; accepting a bare number too costs nothing and covers stores that
            // send it unquoted. Anything else - null, a nested object, a boolean - is not a status.
            if (token!.Type != JTokenType.String && token.Type != JTokenType.Integer)
            {
                return false;
            }

            string? text = token.Value<string>();

            // NumberStyles.None rejects " 404", "+404" and "404.0", so only an exact status matches.
            return text != null
                && int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out statusCode);
        }
    }
}
