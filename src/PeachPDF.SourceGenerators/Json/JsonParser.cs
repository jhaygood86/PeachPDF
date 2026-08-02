using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachPDF.SourceGenerators.Json
{
    /// <summary>
    /// A minimal, strict, hand-written JSON reader (objects/arrays/strings/numbers/booleans/null,
    /// standard escapes) for <c>css-properties.json</c>. Not a general-purpose library - this project
    /// is an analyzer (netstandard2.0, loaded directly into csc's process with none of its
    /// PackageReferences available - see PeachPDF.SourceGenerators.csproj's remarks), so
    /// System.Text.Json is not an option here. Strict on purpose: no comments, no trailing commas -
    /// the schema is entirely under this repo's control, so leniency would only hide authoring
    /// mistakes the diagnostics below exist to catch.
    /// </summary>
    internal static class JsonParser
    {
        public static bool TryParse(string text, out JsonValue? root, out List<JsonError> errors)
        {
            var scanner = new Scanner(text);
            errors = new List<JsonError>();

            try
            {
                scanner.SkipWhitespace();
                root = scanner.ParseValue(errors);
                scanner.SkipWhitespace();

                if (errors.Count == 0 && !scanner.IsAtEnd)
                {
                    errors.Add(new JsonError("Unexpected trailing content after the JSON document's root value.",
                        scanner.Line, scanner.Column));
                }
            }
            catch (JsonScanException ex)
            {
                root = null;
                errors.Add(new JsonError(ex.Message, ex.Line, ex.Column));
            }

            return errors.Count == 0 && root is not null;
        }

        private sealed class JsonScanException : System.Exception
        {
            public int Line { get; }
            public int Column { get; }

            public JsonScanException(string message, int line, int column) : base(message)
            {
                Line = line;
                Column = column;
            }
        }

        private sealed class Scanner
        {
            private readonly string _text;
            private int _index;

            public int Line { get; private set; } = 1;
            public int Column { get; private set; } = 1;

            public Scanner(string text)
            {
                _text = text;
            }

            public bool IsAtEnd => _index >= _text.Length;

            private char Current => _text[_index];

            private void Advance()
            {
                if (_text[_index] == '\n')
                {
                    Line++;
                    Column = 1;
                }
                else
                {
                    Column++;
                }

                _index++;
            }

            private void Fail(string message) => throw new JsonScanException(message, Line, Column);

            public void SkipWhitespace()
            {
                while (!IsAtEnd && (Current is ' ' or '\t' or '\r' or '\n'))
                    Advance();
            }

            public JsonValue ParseValue(List<JsonError> errors)
            {
                if (IsAtEnd) Fail("Unexpected end of input; expected a JSON value.");

                return Current switch
                {
                    '{' => ParseObject(errors),
                    '[' => ParseArray(errors),
                    '"' => ParseStringValue(),
                    't' => ParseLiteral("true", JsonValueKind.True),
                    'f' => ParseLiteral("false", JsonValueKind.False),
                    'n' => ParseLiteral("null", JsonValueKind.Null),
                    '-' => ParseNumber(),
                    >= '0' and <= '9' => ParseNumber(),
                    _ => FailValue($"Unexpected character '{Current}'.")
                };
            }

            private JsonValue FailValue(string message)
            {
                Fail(message);
                return null!; // unreachable — Fail always throws
            }

            private JsonValue ParseLiteral(string literal, JsonValueKind kind)
            {
                var (line, column) = (Line, Column);
                foreach (var expected in literal)
                {
                    if (IsAtEnd || Current != expected)
                        Fail($"Expected literal '{literal}'.");
                    Advance();
                }

                return new JsonValue(kind, line, column);
            }

            private JsonValue ParseObject(List<JsonError> errors)
            {
                var (line, column) = (Line, Column);
                Advance(); // '{'
                var members = new List<JsonMember>();

                SkipWhitespace();
                if (!IsAtEnd && Current == '}')
                {
                    Advance();
                    return new JsonValue(JsonValueKind.Object, line, column, objectMembers: members);
                }

                while (true)
                {
                    SkipWhitespace();
                    if (IsAtEnd || Current != '"')
                        Fail("Expected a string property name.");

                    var name = ParseStringValue().StringValue!;
                    SkipWhitespace();

                    if (IsAtEnd || Current != ':')
                        Fail("Expected ':' after a property name.");
                    Advance();

                    SkipWhitespace();
                    var value = ParseValue(errors);
                    members.Add(new JsonMember(name, value));

                    SkipWhitespace();
                    if (IsAtEnd) Fail("Unexpected end of input inside an object.");

                    if (Current == ',')
                    {
                        Advance();
                        SkipWhitespace();
                        if (!IsAtEnd && Current == '}')
                            Fail("Trailing comma is not allowed before '}'.");
                        continue;
                    }

                    if (Current == '}')
                    {
                        Advance();
                        break;
                    }

                    Fail("Expected ',' or '}' in object.");
                }

                return new JsonValue(JsonValueKind.Object, line, column, objectMembers: members);
            }

            private JsonValue ParseArray(List<JsonError> errors)
            {
                var (line, column) = (Line, Column);
                Advance(); // '['
                var items = new List<JsonValue>();

                SkipWhitespace();
                if (!IsAtEnd && Current == ']')
                {
                    Advance();
                    return new JsonValue(JsonValueKind.Array, line, column, arrayItems: items);
                }

                while (true)
                {
                    SkipWhitespace();
                    items.Add(ParseValue(errors));
                    SkipWhitespace();

                    if (IsAtEnd) Fail("Unexpected end of input inside an array.");

                    if (Current == ',')
                    {
                        Advance();
                        SkipWhitespace();
                        if (!IsAtEnd && Current == ']')
                            Fail("Trailing comma is not allowed before ']'.");
                        continue;
                    }

                    if (Current == ']')
                    {
                        Advance();
                        break;
                    }

                    Fail("Expected ',' or ']' in array.");
                }

                return new JsonValue(JsonValueKind.Array, line, column, arrayItems: items);
            }

            private JsonValue ParseStringValue()
            {
                var (line, column) = (Line, Column);
                Advance(); // opening quote
                var sb = new StringBuilder();

                while (true)
                {
                    if (IsAtEnd) Fail("Unterminated string literal.");

                    var c = Current;
                    if (c == '"')
                    {
                        Advance();
                        break;
                    }

                    if (c == '\\')
                    {
                        Advance();
                        if (IsAtEnd) Fail("Unterminated escape sequence.");
                        var escape = Current;
                        switch (escape)
                        {
                            case '"': sb.Append('"'); Advance(); break;
                            case '\\': sb.Append('\\'); Advance(); break;
                            case '/': sb.Append('/'); Advance(); break;
                            case 'b': sb.Append('\b'); Advance(); break;
                            case 'f': sb.Append('\f'); Advance(); break;
                            case 'n': sb.Append('\n'); Advance(); break;
                            case 'r': sb.Append('\r'); Advance(); break;
                            case 't': sb.Append('\t'); Advance(); break;
                            case 'u':
                                Advance();
                                sb.Append(ParseUnicodeEscape());
                                break;
                            default:
                                Fail($"Invalid escape sequence '\\{escape}'.");
                                break;
                        }

                        continue;
                    }

                    if (c < 0x20)
                        Fail("Unescaped control character in string literal.");

                    sb.Append(c);
                    Advance();
                }

                return new JsonValue(JsonValueKind.String, line, column, stringValue: sb.ToString());
            }

            private char ParseUnicodeEscape()
            {
                if (_index + 4 > _text.Length)
                    Fail("Truncated \\u escape sequence.");

                var hex = _text.Substring(_index, 4);
                if (!ushort.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var code))
                    Fail($"Invalid \\u escape sequence '\\u{hex}'.");

                for (var i = 0; i < 4; i++) Advance();
                return (char)code;
            }

            private JsonValue ParseNumber()
            {
                var (line, column) = (Line, Column);
                var start = _index;

                if (!IsAtEnd && Current == '-') Advance();

                if (IsAtEnd || Current is < '0' or > '9') Fail("Expected a digit.");
                if (Current == '0')
                {
                    Advance();
                }
                else
                {
                    while (!IsAtEnd && Current is >= '0' and <= '9') Advance();
                }

                if (!IsAtEnd && Current == '.')
                {
                    Advance();
                    if (IsAtEnd || Current is < '0' or > '9') Fail("Expected a digit after decimal point.");
                    while (!IsAtEnd && Current is >= '0' and <= '9') Advance();
                }

                if (!IsAtEnd && (Current == 'e' || Current == 'E'))
                {
                    Advance();
                    if (!IsAtEnd && (Current == '+' || Current == '-')) Advance();
                    if (IsAtEnd || Current is < '0' or > '9') Fail("Expected a digit in exponent.");
                    while (!IsAtEnd && Current is >= '0' and <= '9') Advance();
                }

                var text = _text.Substring(start, _index - start);
                var value = double.Parse(text, CultureInfo.InvariantCulture);
                return new JsonValue(JsonValueKind.Number, line, column, numberValue: value);
            }
        }
    }
}
