using System;
using System.Collections.Generic;
using System.Text;
using GamePatchKit.Core.Json;
using GamePatchKit.Core.Paths;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.Json;

public class TestCanonicalJsonWriter
{
    [Fact]
    public void SortsObjectPropertiesByUtf16CodeUnit()
    {
        var obj = new JObject { ["b"] = 1, ["a"] = 2, ["A"] = 3 };

        byte[] bytes = CanonicalJsonWriter.Write(obj);

        Assert.Equal("{\"A\":3,\"a\":2,\"b\":1}", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void JcsKeySortAndUtf8ByteSortGenuinelyDisagreeForSupplementaryVsPrivateUseCharacters()
    {
        // U+1F600 (supplementary plane; UTF-16 surrogate pair D83D DE00) sorts BEFORE U+E000 (BMP
        // private-use area; single code unit E000) under UTF-16 code-unit order (JCS object-key sort,
        // StringComparer.Ordinal) - but AFTER it under UTF-8 byte order (domain array sort,
        // Utf8OrdinalStringComparer) - because a surrogate *code unit* (D83D) is numerically lower than
        // E000 even though the *code point* it helps represent (128512) is far higher than E000 (57344).
        // This is the concrete case the two-comparer split (see Utf8OrdinalStringComparer's own comment)
        // exists for; ASCII-only golden vectors never exercise it.
        const string supplementaryPlaneChar = "😀"; // U+1F600
        const string privateUseChar = ""; // U+E000

        var obj = new JObject { [privateUseChar] = 1, [supplementaryPlaneChar] = 2 };

        byte[] bytes = CanonicalJsonWriter.Write(obj);
        string json = Encoding.UTF8.GetString(bytes);

        Assert.Equal($"{{\"{supplementaryPlaneChar}\":2,\"{privateUseChar}\":1}}", json);

        var domainOrder = new List<string> { supplementaryPlaneChar, privateUseChar };
        domainOrder.Sort(Utf8OrdinalStringComparer.Instance);

        Assert.Equal(new[] { privateUseChar, supplementaryPlaneChar }, domainOrder);
    }

    [Fact]
    public void PreservesArrayElementOrder()
    {
        var array = new JArray(3, 1, 2);

        byte[] bytes = CanonicalJsonWriter.Write(array);

        Assert.Equal("[3,1,2]", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void EscapesOnlyQuoteBackslashAndControlCharacters()
    {
        var value = new JValue("a\"b\\c\bd\fe\nf\rg\thi/j");

        byte[] bytes = CanonicalJsonWriter.Write(value);

        Assert.Equal("\"a\\\"b\\\\c\\bd\\fe\\nf\\rg\\th\\u0001i/j\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void WritesNonAsciiCharactersAsLiteralUtf8()
    {
        var value = new JValue("한글🎮");

        byte[] bytes = CanonicalJsonWriter.Write(value);

        Assert.Equal("\"한글🎮\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void RejectsUnpairedSurrogateInsteadOfSilentlyReplacingIt()
    {
        // Newtonsoft accepts "\ud800" (a lone high surrogate) as valid JSON per RFC 8259, but it is not
        // a well-formed Unicode scalar value. Encoding.UTF8's default replacement fallback would silently
        // turn it into the same bytes as a literal U+FFFD, letting two different manifests collide on the
        // same canonical bytes (and hash). The writer must reject it instead.
        var value = new JValue("\ud800");

        Assert.Throws<FormatException>(() => CanonicalJsonWriter.Write(value));
    }

    [Fact]
    public void RejectsUnpairedSurrogateInObjectKey()
    {
        var obj = new JObject { ["\ud800"] = 1 };

        Assert.Throws<FormatException>(() => CanonicalJsonWriter.Write(obj));
    }

    [Fact]
    public void WritesLiteralReplacementCharacterDistinctlyFromAnUnpairedSurrogate()
    {
        var value = new JValue("�");

        byte[] bytes = CanonicalJsonWriter.Write(value);

        Assert.Equal("\"�\"", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void WritesIntegersAsPlainDecimal()
    {
        var value = new JValue(-42L);

        byte[] bytes = CanonicalJsonWriter.Write(value);

        Assert.Equal("-42", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void RejectsNonIntegerNumbers()
    {
        var value = new JValue(1.5);

        Assert.Throws<NotSupportedException>(() => CanonicalJsonWriter.Write(value));
    }

    [Fact]
    public void RejectsIntegersOutsideSafeRange()
    {
        var value = new JValue(JsonNumbers.MaxSafeInteger + 1);

        Assert.Throws<NotSupportedException>(() => CanonicalJsonWriter.Write(value));
    }

    [Fact]
    public void WritesNestedStructuresWithNoWhitespaceOrTrailingNewline()
    {
        var obj = new JObject
        {
            ["z"] = new JArray("x", "y"),
            ["a"] = new JObject { ["nested"] = true, ["missing"] = null },
        };

        byte[] bytes = CanonicalJsonWriter.Write(obj);

        Assert.Equal("{\"a\":{\"missing\":null,\"nested\":true},\"z\":[\"x\",\"y\"]}", Encoding.UTF8.GetString(bytes));
        Assert.DoesNotContain((byte)'\n', bytes);
        Assert.False(bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF, "must not emit a UTF-8 BOM");
    }
}
