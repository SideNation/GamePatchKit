using System;
using System.Text;
using GamePatchKit.Core.Json;
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
