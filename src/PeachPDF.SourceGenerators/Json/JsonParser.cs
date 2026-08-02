using System.Collections.Generic;
using System.Text;
using System.Text.Json;

namespace PeachPDF.SourceGenerators.Json
{
    /// <summary>
    /// Parses <c>css-properties.json</c> into a <see cref="JsonValue"/> tree using
    /// <see cref="Utf8JsonReader"/> as the tokenizer (strings/escapes, number grammar, structural
    /// validity - trailing commas and comments rejected, matching <see cref="JsonReaderOptions"/>'s
    /// defaults, made explicit below since the schema is entirely under this repo's control and
    /// leniency would only hide authoring mistakes the PPGxxx diagnostics exist to catch). Not a thin
    /// wrapper over <see cref="JsonDocument"/>/<see cref="JsonElement"/>, because neither retains a
    /// parsed node's source position - PPG002 etc. need to point a diagnostic at the specific JSON
    /// entry that's wrong, not just "somewhere in this file", so this builds its own minimal DOM
    /// (<see cref="JsonValue"/>) that records each node's line/column as it reads.
    /// </summary>
    internal static class JsonParser
    {
        private static readonly JsonReaderOptions Options = new()
        {
            AllowTrailingCommas = false,
            CommentHandling = JsonCommentHandling.Disallow,
        };

        public static bool TryParse(string text, out JsonValue? root, out List<JsonError> errors)
        {
            errors = new List<JsonError>();
            var bytes = Encoding.UTF8.GetBytes(text);
            var reader = new Utf8JsonReader(bytes, Options);

            try
            {
                root = ReadNextValue(ref reader, bytes);

                if (reader.Read())
                {
                    var (line, column) = LocationOf(bytes, reader.TokenStartIndex);
                    errors.Add(new JsonError("Unexpected trailing content after the JSON document's root value.", line, column));
                }
            }
            catch (JsonException ex)
            {
                root = null;
                var line = (int)(ex.LineNumber ?? 0) + 1;
                var column = (int)(ex.BytePositionInLine ?? 0) + 1;
                errors.Add(new JsonError(ex.Message, line, column));
            }

            return errors.Count == 0 && root is not null;
        }

        private static JsonValue ReadNextValue(ref Utf8JsonReader reader, byte[] bytes)
        {
            if (!reader.Read())
                throw new JsonException("Unexpected end of input; expected a JSON value.");

            return InterpretCurrentToken(ref reader, bytes);
        }

        private static JsonValue InterpretCurrentToken(ref Utf8JsonReader reader, byte[] bytes)
        {
            var (line, column) = LocationOf(bytes, reader.TokenStartIndex);

            switch (reader.TokenType)
            {
                case JsonTokenType.StartObject: return ReadObject(ref reader, bytes, line, column);
                case JsonTokenType.StartArray: return ReadArray(ref reader, bytes, line, column);
                case JsonTokenType.String: return new JsonValue(JsonValueKind.String, line, column, stringValue: reader.GetString());
                case JsonTokenType.Number: return new JsonValue(JsonValueKind.Number, line, column, numberValue: reader.GetDouble());
                case JsonTokenType.True: return new JsonValue(JsonValueKind.True, line, column);
                case JsonTokenType.False: return new JsonValue(JsonValueKind.False, line, column);
                case JsonTokenType.Null: return new JsonValue(JsonValueKind.Null, line, column);
                default: throw new JsonException($"Unexpected token '{reader.TokenType}'.");
            }
        }

        private static JsonValue ReadObject(ref Utf8JsonReader reader, byte[] bytes, int line, int column)
        {
            var members = new List<JsonMember>();

            while (true)
            {
                if (!reader.Read()) throw new JsonException("Unexpected end of input inside an object.");
                if (reader.TokenType == JsonTokenType.EndObject)
                    return new JsonValue(JsonValueKind.Object, line, column, objectMembers: members);
                if (reader.TokenType != JsonTokenType.PropertyName)
                    throw new JsonException("Expected a string property name.");

                var name = reader.GetString()!;
                var value = ReadNextValue(ref reader, bytes);
                members.Add(new JsonMember(name, value));
            }
        }

        private static JsonValue ReadArray(ref Utf8JsonReader reader, byte[] bytes, int line, int column)
        {
            var items = new List<JsonValue>();

            while (true)
            {
                if (!reader.Read()) throw new JsonException("Unexpected end of input inside an array.");
                if (reader.TokenType == JsonTokenType.EndArray)
                    return new JsonValue(JsonValueKind.Array, line, column, arrayItems: items);

                items.Add(InterpretCurrentToken(ref reader, bytes));
            }
        }

        /// <summary>
        /// Converts a UTF-8 byte offset (<see cref="Utf8JsonReader.TokenStartIndex"/>) into a 1-based
        /// (line, column), by decoding the byte prefix up to that offset and counting characters -
        /// correct for multi-byte UTF-8 sequences too, since a token always starts on a codepoint
        /// boundary.
        /// </summary>
        private static (int Line, int Column) LocationOf(byte[] bytes, long byteOffset)
        {
            var prefix = Encoding.UTF8.GetString(bytes, 0, (int)byteOffset);
            var line = 1;
            var lastNewlineIndex = -1;

            for (var i = 0; i < prefix.Length; i++)
            {
                if (prefix[i] == '\n')
                {
                    line++;
                    lastNewlineIndex = i;
                }
            }

            var column = prefix.Length - lastNewlineIndex;
            return (line, column);
        }
    }
}
