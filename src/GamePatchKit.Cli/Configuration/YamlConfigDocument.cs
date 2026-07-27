using System.Globalization;
using Newtonsoft.Json.Linq;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;

namespace GamePatchKit.Cli.Configuration;

// Reads gamepatchkit.yml into the JSON-compatible data package-config.schema.json is written against, and
// rejects everything docs/contracts/gamepatchkit-yml.md forbids while doing it.
//
// Driven off YamlDotNet's event stream rather than its object model on purpose. A deserializer resolves
// anchors and merge keys before anyone can object to them and silently keeps the last of a set of duplicate
// keys; the events still carry the anchor, the alias, the '<<' key and every repeated key, which is the only
// place those rules can still be enforced.
internal static class YamlConfigDocument
{
    private const string Stage = "gamepatchkit-yml";
    private const string MergeKey = "<<";

    // The only explicit tags the configuration's value model can mean anything by: YAML's own tags for the
    // JSON-compatible types. Any other tag - an application tag like !Foo, or a built-in like !!float or
    // !!binary that canonical JSON cannot carry - is a tag this contract does not define.
    private const string StringTag = "tag:yaml.org,2002:str";
    private const string IntegerTag = "tag:yaml.org,2002:int";
    private const string BooleanTag = "tag:yaml.org,2002:bool";
    private const string NullTag = "tag:yaml.org,2002:null";
    private const string MappingTag = "tag:yaml.org,2002:map";
    private const string SequenceTag = "tag:yaml.org,2002:seq";

    private static readonly string[] _scalarTags = { StringTag, IntegerTag, BooleanTag, NullTag };

    // Decimal integers only. YAML's core schema also resolves 0x/0o forms, but canonical JSON carries plain
    // I-JSON integers, and anything else here falls through to a string and is rejected by the schema with a
    // type error rather than becoming a number nobody wrote.
    private static readonly System.Text.RegularExpressions.Regex _integer =
        new System.Text.RegularExpressions.Regex(@"^[-+]?[0-9]+$", System.Text.RegularExpressions.RegexOptions.Compiled);

    public static JObject Parse(string yamlText, string relativePath)
    {
        var reader = new EventReader(new Parser(new StringReader(yamlText)), relativePath);

        reader.Expect<StreamStart>();

        if (reader.Peek() is StreamEnd)
        {
            throw Failure(CliErrorCodes.YamlEmptyDocument, "The file contains no YAML document.", relativePath);
        }

        reader.Expect<DocumentStart>();
        ParsingEvent root = reader.Peek();

        // '---' with nothing after it parses as an empty plain scalar. That is an empty document, not a
        // document whose root happens to be the wrong kind of node.
        if (root is Scalar { Value: "", Style: ScalarStyle.Plain })
        {
            throw Failure(CliErrorCodes.YamlEmptyDocument, "The YAML document is empty.", relativePath);
        }

        if (root is not MappingStart)
        {
            throw Failure(CliErrorCodes.YamlNonMappingRoot, "The YAML document root must be a mapping.", relativePath);
        }

        var mapping = (JObject)ReadNode(reader, relativePath);
        reader.Expect<DocumentEnd>();

        if (reader.Peek() is DocumentStart)
        {
            throw Failure(CliErrorCodes.YamlMultipleDocuments, "The file contains more than one YAML document.", relativePath);
        }

        reader.Expect<StreamEnd>();
        return mapping;
    }

    private static JToken ReadNode(EventReader reader, string relativePath)
    {
        ParsingEvent current = reader.Read();
        RejectForbiddenNodeFeatures(current, relativePath);

        switch (current)
        {
            case MappingStart mapping:
                RejectUndefinedTag(mapping, relativePath, MappingTag);
                return ReadMapping(reader, relativePath);
            case SequenceStart sequence:
                RejectUndefinedTag(sequence, relativePath, SequenceTag);
                return ReadSequence(reader, relativePath);
            case Scalar scalar:
                RejectUndefinedTag(scalar, relativePath, _scalarTags);
                return ReadScalar(scalar, relativePath);
            default:
                throw Failure(CliErrorCodes.YamlSyntax, $"Unexpected YAML node '{current.GetType().Name}'.", relativePath);
        }
    }

    private static JObject ReadMapping(EventReader reader, string relativePath)
    {
        var mapping = new JObject();

        while (reader.Peek() is not MappingEnd)
        {
            ParsingEvent keyEvent = reader.Read();
            RejectForbiddenNodeFeatures(keyEvent, relativePath);

            if (keyEvent is not Scalar key)
            {
                throw Failure(CliErrorCodes.YamlNonScalarKey, "A mapping key must be a scalar.", relativePath);
            }

            RejectUndefinedTag(key, relativePath, _scalarTags);

            if (key.Value == MergeKey)
            {
                throw Failure(CliErrorCodes.YamlMergeKey, "Merge keys ('<<') are not allowed.", relativePath);
            }

            if (mapping.ContainsKey(key.Value))
            {
                throw Failure(
                    CliErrorCodes.YamlDuplicateKey,
                    $"The mapping key '{key.Value}' appears more than once in the same mapping.",
                    relativePath);
            }

            mapping[key.Value] = ReadNode(reader, relativePath);
        }

        reader.Expect<MappingEnd>();
        return mapping;
    }

    private static JArray ReadSequence(EventReader reader, string relativePath)
    {
        var sequence = new JArray();

        while (reader.Peek() is not SequenceEnd)
        {
            sequence.Add(ReadNode(reader, relativePath));
        }

        reader.Expect<SequenceEnd>();
        return sequence;
    }

    private static JToken ReadScalar(Scalar scalar, string relativePath)
    {
        // An explicit tag is a type the author asserted, so it decides - both over the spelling and over the
        // quoting. `!!str 1` is the string "1"; resolving it by spelling would silently hand the schema an
        // integer and reject a document that says exactly what it means.
        if (!scalar.Tag.IsEmpty)
        {
            return ReadTaggedScalar(scalar, relativePath);
        }

        // A quoted scalar is a string no matter what it spells, which is what keeps a packageId of "true" or
        // a version pinned as "1" from turning into a boolean or a number.
        if (scalar.Style != ScalarStyle.Plain)
        {
            return new JValue(scalar.Value);
        }

        if (TryReadNull(scalar.Value, out JToken? nullValue))
        {
            return nullValue!;
        }

        if (TryReadBoolean(scalar.Value, out JToken? booleanValue))
        {
            return booleanValue!;
        }

        if (TryReadInteger(scalar.Value, out JToken? integerValue))
        {
            return integerValue!;
        }

        return new JValue(scalar.Value);
    }

    private static JToken ReadTaggedScalar(Scalar scalar, string relativePath)
    {
        switch (scalar.Tag.Value)
        {
            case StringTag:
                return new JValue(scalar.Value);
            case NullTag when TryReadNull(scalar.Value, out JToken? nullValue):
                return nullValue!;
            case BooleanTag when TryReadBoolean(scalar.Value, out JToken? booleanValue):
                return booleanValue!;
            case IntegerTag when TryReadInteger(scalar.Value, out JToken? integerValue):
                return integerValue!;
            default:
                // The tag is one of the four, or RejectUndefinedTag would already have thrown; getting here
                // means the value cannot be read as the type it was tagged with.
                throw Failure(
                    CliErrorCodes.YamlInvalidTaggedScalar,
                    $"A scalar tagged '{scalar.Tag.Value}' does not hold a value of that type.",
                    relativePath);
        }
    }

    private static bool TryReadNull(string value, out JToken? token)
    {
        token = value is "" or "~" or "null" or "Null" or "NULL" ? JValue.CreateNull() : null;
        return token != null;
    }

    private static bool TryReadBoolean(string value, out JToken? token)
    {
        token = value switch
        {
            "true" or "True" or "TRUE" => new JValue(true),
            "false" or "False" or "FALSE" => new JValue(false),
            _ => null,
        };

        return token != null;
    }

    private static bool TryReadInteger(string value, out JToken? token)
    {
        token = null;

        if (!_integer.IsMatch(value)
            || !long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long integer))
        {
            return false;
        }

        // Compared against both bounds rather than through Math.Abs: Math.Abs(long.MinValue) throws, so a
        // configuration containing that exact literal would crash the process instead of being rejected.
        if (integer < -JsonSafeIntegerLimit || integer > JsonSafeIntegerLimit)
        {
            return false;
        }

        token = new JValue(integer);
        return true;
    }

    // I-JSON's safe integer range. A larger literal stays a string and is rejected by the schema, rather than
    // becoming a number the canonical writer would later refuse to serialize.
    private const long JsonSafeIntegerLimit = 9007199254740991L;

    private static void RejectForbiddenNodeFeatures(ParsingEvent parsingEvent, string relativePath)
    {
        if (parsingEvent is AnchorAlias)
        {
            throw Failure(CliErrorCodes.YamlAlias, "Aliases ('*name') are not allowed.", relativePath);
        }

        if (parsingEvent is not NodeEvent node)
        {
            return;
        }

        if (!node.Anchor.IsEmpty)
        {
            throw Failure(CliErrorCodes.YamlAnchor, "Anchors ('&name') are not allowed.", relativePath);
        }

    }

    // An unspecified tag reads as empty here. Anything else has to be one of the tags this contract defines
    // for that kind of node - `!!str` on a scalar, `!!seq` on a sequence - which also rules out the
    // non-specific '!' and built-ins like `!!float` that canonical JSON cannot carry.
    private static void RejectUndefinedTag(NodeEvent node, string relativePath, params string[] allowedTags)
    {
        if (node.Tag.IsEmpty || allowedTags.Contains(node.Tag.Value, StringComparer.Ordinal))
        {
            return;
        }

        throw Failure(CliErrorCodes.YamlCustomTag, $"The tag '{node.Tag.Value}' is not allowed here.", relativePath);
    }

    private static CliException Failure(string code, string message, string relativePath)
    {
        return new CliException(code, message, relativePath);
    }

    // One-token lookahead over IParser, which only exposes Current after MoveNext. Every YamlException the
    // underlying scanner raises is turned into one syntax error here so no caller has to know the library.
    private sealed class EventReader
    {
        private readonly IParser _parser;
        private readonly string _relativePath;
        private ParsingEvent? _pending;

        public EventReader(IParser parser, string relativePath)
        {
            _parser = parser;
            _relativePath = relativePath;
        }

        public ParsingEvent Peek()
        {
            return _pending ??= MoveNext();
        }

        public ParsingEvent Read()
        {
            ParsingEvent current = Peek();
            _pending = null;
            return current;
        }

        public void Expect<TEvent>()
            where TEvent : ParsingEvent
        {
            ParsingEvent current = Read();

            if (current is not TEvent)
            {
                throw Failure(
                    CliErrorCodes.YamlSyntax,
                    $"Expected {typeof(TEvent).Name} but found {current.GetType().Name}.",
                    _relativePath);
            }
        }

        private ParsingEvent MoveNext()
        {
            try
            {
                if (!_parser.MoveNext())
                {
                    throw Failure(CliErrorCodes.YamlSyntax, "The YAML stream ended unexpectedly.", _relativePath);
                }
            }
            catch (YamlException exception)
            {
                throw Failure(CliErrorCodes.YamlSyntax, "The file is not valid YAML: " + exception.Message, _relativePath);
            }

            return _parser.Current!;
        }
    }
}
