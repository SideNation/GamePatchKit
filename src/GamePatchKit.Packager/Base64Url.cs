namespace GamePatchKit.Packager;

// Unpadded base64url, the encoding the PRD fixes for signature and public-key values.
//
// Decoding routes through Convert.FromBase64String on purpose: it rejects trailing padding bits that are not
// zero, so an encoding that is not the canonical one for its bytes fails here rather than silently
// round-tripping into a different string than the one that was validated.
internal static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static bool TryDecode(string value, out byte[] bytes)
    {
        if (value.AsSpan().IndexOfAny('+', '/', '=') >= 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);

        try
        {
            bytes = Convert.FromBase64String(base64);
            return true;
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }
    }
}
