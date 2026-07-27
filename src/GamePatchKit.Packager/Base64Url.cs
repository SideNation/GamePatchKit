namespace GamePatchKit.Packager;

// Unpadded base64url, the encoding the PRD fixes for signature and public-key values.
internal static class Base64Url
{
    public static string Encode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    // Convert.FromBase64String does NOT reject non-zero unused bits in the final base64 group (e.g. both
    // "AA==" and "AP==" decode to the same single zero byte), so re-encoding the decoded bytes and comparing
    // back to the original string is what actually enforces "there is exactly one valid encoding for these
    // bytes" rather than accepting any of several strings that happen to decode to the same value.
    public static bool TryDecode(string value, out byte[] bytes)
    {
        if (value.AsSpan().IndexOfAny('+', '/', '=') >= 0)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        string base64 = value.Replace('-', '+').Replace('_', '/');
        base64 += new string('=', (4 - (base64.Length % 4)) % 4);

        byte[] decoded;

        try
        {
            decoded = Convert.FromBase64String(base64);
        }
        catch (FormatException)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        if (Encode(decoded) != value)
        {
            bytes = Array.Empty<byte>();
            return false;
        }

        bytes = decoded;
        return true;
    }
}
