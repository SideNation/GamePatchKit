using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Json
{
    // Hand-written RFC 8785 (JSON Canonicalization Scheme) serializer. Never uses JToken.ToString() or any
    // Newtonsoft serializer: object keys are re-sorted by UTF-16 code unit (JCS), strings use JCS minimal
    // escaping, numbers are I-JSON safe integers only, and output is UTF-8 with no BOM/whitespace/trailing
    // newline. Array element order is never touched here; callers must pre-sort arrays per domain rules
    // before building the JToken tree (see docs/plan/02-schema-canonicalization.md canonical array order).
    public static class CanonicalJsonWriter
    {
        public static byte[] Write(JToken value)
        {
            using (var stream = new MemoryStream())
            {
                Write(stream, value);
                return stream.ToArray();
            }
        }

        public static void Write(Stream destination, JToken value)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if (value == null)
            {
                throw new ArgumentNullException(nameof(value));
            }

            WriteValue(destination, value);
        }

        private static void WriteValue(Stream destination, JToken value)
        {
            switch (value.Type)
            {
                case JTokenType.Object:
                    WriteObject(destination, (JObject)value);
                    return;
                case JTokenType.Array:
                    WriteArray(destination, (JArray)value);
                    return;
                case JTokenType.String:
                    WriteString(destination, (string)value!);
                    return;
                case JTokenType.Integer:
                    WriteInteger(destination, value);
                    return;
                case JTokenType.Boolean:
                    WriteAscii(destination, (bool)value ? "true" : "false");
                    return;
                case JTokenType.Null:
                    WriteAscii(destination, "null");
                    return;
                default:
                    throw new NotSupportedException($"JTokenType.{value.Type} is not a canonical JSON value.");
            }
        }

        private static void WriteObject(Stream destination, JObject obj)
        {
            WriteAscii(destination, "{");

            JProperty[] properties = obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).ToArray();

            for (int i = 0; i < properties.Length; i++)
            {
                if (i > 0)
                {
                    WriteAscii(destination, ",");
                }

                WriteString(destination, properties[i].Name);
                WriteAscii(destination, ":");
                WriteValue(destination, properties[i].Value);
            }

            WriteAscii(destination, "}");
        }

        private static void WriteArray(Stream destination, JArray array)
        {
            WriteAscii(destination, "[");

            for (int i = 0; i < array.Count; i++)
            {
                if (i > 0)
                {
                    WriteAscii(destination, ",");
                }

                WriteValue(destination, array[i]);
            }

            WriteAscii(destination, "]");
        }

        private static void WriteInteger(Stream destination, JToken value)
        {
            if (!JsonNumbers.TryGetSafeInteger(value, out long safeInteger))
            {
                throw new NotSupportedException("Canonical JSON numbers must be integers within +/-(2^53-1).");
            }

            WriteAscii(destination, safeInteger.ToString(CultureInfo.InvariantCulture));
        }

        private static void WriteString(Stream destination, string value)
        {
            WriteAscii(destination, "\"");

            int chunkStart = 0;

            for (int i = 0; i < value.Length; i++)
            {
                string? escape = GetJcsEscape(value[i]);
                if (escape == null)
                {
                    continue;
                }

                if (i > chunkStart)
                {
                    WriteUtf8(destination, value.Substring(chunkStart, i - chunkStart));
                }

                WriteAscii(destination, escape);
                chunkStart = i + 1;
            }

            if (chunkStart < value.Length)
            {
                WriteUtf8(destination, value.Substring(chunkStart));
            }

            WriteAscii(destination, "\"");
        }

        // JCS minimal escaping: only '"', '\\', and control characters (U+0000-U+001F) are escaped. '/' and
        // every character above U+001F, including all non-ASCII text, are written as literal UTF-8 bytes.
        private static string? GetJcsEscape(char c)
        {
            switch (c)
            {
                case '"':
                    return "\\\"";
                case '\\':
                    return "\\\\";
                case '\b':
                    return "\\b";
                case '\f':
                    return "\\f";
                case '\n':
                    return "\\n";
                case '\r':
                    return "\\r";
                case '\t':
                    return "\\t";
                default:
                    return c < 0x20 ? "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture) : null;
            }
        }

        private static void WriteAscii(Stream destination, string asciiText)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(asciiText);
            destination.Write(bytes, 0, bytes.Length);
        }

        private static void WriteUtf8(Stream destination, string text)
        {
            byte[] bytes = Encoding.UTF8.GetBytes(text);
            destination.Write(bytes, 0, bytes.Length);
        }
    }
}
