using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A single CSS token - a discriminated union keyed by <see cref="Type"/>, covering every shape the
    /// former <c>Token</c> class hierarchy (<c>UnitToken</c>, <c>NumberToken</c>, <c>KeywordToken</c>,
    /// <c>StringToken</c>, <c>ColorToken</c>, <c>RangeToken</c>, <c>UrlToken</c>, <c>CommentToken</c>,
    /// <c>FunctionToken</c>) used to model as separate sealed subclasses. A single <see langword="readonly"/>
    /// <see langword="struct"/> instead of a class hierarchy means producing a token never allocates a
    /// wrapper object for the common case - see the exact <see cref="TokenType"/>-set each former
    /// subclass covered, recorded next to every call site that pattern-matched on a concrete subtype.
    /// <para>
    /// Only four fields live directly on <see cref="Token"/>: <see cref="Type"/>, <see cref="Position"/>,
    /// a slice-backed <c>Data</c>, and a single <see cref="TokenExtra"/> reference that is
    /// <see langword="null"/> for every token that doesn't need one. Measured across the full showcase
    /// corpus (issue #922 follow-up), only <see cref="TokenType.String"/>/<see cref="TokenType.Url"/>/
    /// <see cref="TokenType.Comment"/>/<see cref="TokenType.Function"/>/<see cref="TokenType.Range"/>/
    /// <see cref="TokenType.Dimension"/> tokens ever need <see cref="TokenExtra"/> - 5.71% of all tokens
    /// constructed - so the other ~94% (idents, numbers, whitespace, punctuation, percentages, EOF, ...)
    /// never carry storage for fields they don't use. This replaced an earlier design where all of
    /// <see cref="TokenExtra"/>'s fields lived directly on <see cref="Token"/> behind
    /// <c>[StructLayout(LayoutKind.Explicit)]</c> field-offset overlaps (64 bytes, then 48 after
    /// overlapping the mutually-exclusive fields) - that design still paid the union's size on every
    /// token regardless of kind, which a full-showcase-suite allocation comparison against a pre-struct
    /// baseline showed was still a net regression (`Token` is stored *inline* in
    /// <see cref="List{Token}"/>'s backing array, not as an 8-byte reference, so every list resize copies
    /// the struct's own size per element). See
    /// .claude/recent-fixes/2026-09-08-css-token-list-pooling-and-ireadonlylist-value-converter-pipeline.md
    /// and .claude/recent-fixes/2026-09-08-token-struct-shrunk-via-explicit-layout-field-overlap.md for
    /// that earlier design's own history.
    /// </para>
    /// </summary>
    internal readonly struct Token
    {
        public static readonly Token Whitespace = new(TokenType.Whitespace, " ".AsMemory(), TextPosition.Empty);
        public static readonly Token Comma = new(TokenType.Comma, ",".AsMemory(), TextPosition.Empty);

        private static readonly char[] FloatIndicators = { '.', 'e', 'E' };

        public readonly TokenType Type;

        // TextPosition is a pure value type (no reference inside), so it only needs 4-byte alignment.
        public readonly TextPosition Position;

        // Either a slice of a shared, already-alive TextSource buffer (the common, allocation-free
        // case - see Lexer's BeginContentAt/AppendLiteral/EndContent) or a directly owned string
        // wrapped whole via .AsMemory() (the escape/CRLF/line-continuation exception, where the
        // content genuinely diverges from a literal source range). ReadOnlyMemory<char>.ToString()
        // returns the same string instance with zero extra allocation whenever it spans an entire
        // string unchanged, which is exactly the owned-string case - so no separate "owned" field is
        // needed to get that for free.
        private readonly ReadOnlyMemory<char> _data;
        public string Data => _data.ToString();
        public ReadOnlySpan<char> DataSpan => _data.Span;

        // Null for every token that isn't one of the six kinds documented on TokenExtra itself - see
        // the class comment there for the current measured proportion and rationale.
        private readonly TokenExtra? _extra;

        public bool IsValid => Type == TokenType.Color
            ? DataSpan.Length != 3 && DataSpan.Length != 4 && DataSpan.Length != 6 && DataSpan.Length != 8
            : _extra?.IsValid ?? false;

        public Token(TokenType type, ReadOnlyMemory<char> data, TextPosition position)
        {
            Type = type;
            _data = data;
            Position = position;
            _extra = null;
        }

        private Token(TokenType type, ReadOnlyMemory<char> data, TextPosition position, TokenExtra? extra)
        {
            Type = type;
            _data = data;
            Position = position;
            _extra = extra;
        }

        public static Token NewString(ReadOnlyMemory<char> value, bool valid, char quote, TextPosition position) =>
            new(TokenType.String, value, position, new TokenExtra { IsValid = valid, Quote = quote });

        public static Token NewUrl(string functionName, ReadOnlyMemory<char> data, bool valid, TextPosition position) =>
            new(TokenType.Url, data, position, new TokenExtra { IsValid = valid, ExtraOrRangeStart = functionName });

        public static Token NewColor(ReadOnlyMemory<char> data, TextPosition position) =>
            new(TokenType.Color, data, position);

        public static Token NewComment(ReadOnlyMemory<char> data, bool valid, TextPosition position) =>
            new(TokenType.Comment, data, position, new TokenExtra { IsValid = valid });

        public static Token NewKeyword(TokenType type, ReadOnlyMemory<char> data, TextPosition position) =>
            new(type, data, position);

        public static Token NewFunction(ReadOnlyMemory<char> data, TextPosition position) =>
            new(TokenType.Function, data, position, new TokenExtra { Arguments = new List<Token>() });

        public static Token NewNumber(ReadOnlyMemory<char> number, TextPosition position) =>
            new(TokenType.Number, number, position);

        // Percentage tokens are also routed through here (see Lexer.cs), but their unit is always the
        // literal "%" - reconstructed from Type in the Unit property below instead of stored, so a
        // Percentage token needs no TokenExtra at all.
        public static Token NewUnit(TokenType type, ReadOnlyMemory<char> value, string unit, TextPosition position) =>
            new(type, value, position, type == TokenType.Percentage ? null : new TokenExtra { ExtraOrRangeStart = unit });

        public static Token NewRange(string range, TextPosition position) =>
            new(TokenType.Range, range.AsMemory(), position, new TokenExtra
            {
                ExtraOrRangeStart = range.Replace(Symbols.QuestionMark, '0'),
                RangeEnd = range.Replace(Symbols.QuestionMark, 'F')
            });

        public static Token NewRange(string start, string end, TextPosition position) =>
            new(TokenType.Range, string.Concat(start, "-", end).AsMemory(), position, new TokenExtra
            {
                ExtraOrRangeStart = start,
                RangeEnd = end
            });

        // --- UnitToken / NumberToken ---
        public float Value => float.Parse(DataSpan, CultureInfo.InvariantCulture);
        public bool IsInteger => DataSpan.IndexOfAny(FloatIndicators) == -1;

        public int IntegerValue
        {
            get
            {
                var parsed = int.TryParse(DataSpan, out var result);

                if (parsed)
                {
                    return result;
                }

                if (Data.All(char.IsDigit))
                {
                    return int.MaxValue;
                }

                throw new ParseException($"Unrecognized integer value '{Data}.'");
            }
        }

        // --- StringToken ---
        public char Quote => _extra?.Quote ?? default;

        // --- UnitToken.Unit / UrlToken.FunctionName ---
        public string Unit => Type == TokenType.Percentage ? "%" : _extra?.ExtraOrRangeStart ?? "";
        public string FunctionName => _extra?.ExtraOrRangeStart ?? "";

        // --- RangeToken --- (only ever null for a non-Range token, a caller bug per the fields above)
        public string RangeStart => _extra!.ExtraOrRangeStart!;
        public string RangeEnd => _extra!.RangeEnd!;

        // --- FunctionToken --- (only ever null for a non-Function token, same caller-bug contract)
        public IReadOnlyList<Token>? Arguments => _extra?.Arguments;

        public IReadOnlyList<Token> ArgumentTokens
        {
            get
            {
                var args = _extra!.Arguments!;
                var count = args.Count;

                if (count > 0 && args[count - 1].Type == TokenType.RoundBracketClose)
                {
                    count--;
                }

                return args.GetRange(0, count);
            }
        }

        public void AddArgumentToken(Token token)
        {
            _extra!.Arguments!.Add(token);
        }

        public string ToValue() => Type switch
        {
            TokenType.Hash => "#" + Data,
            TokenType.AtKeyword => "@" + Data,
            TokenType.Function => string.Concat(Data, "(", _extra!.Arguments.ToText()),
            TokenType.String => Data.StylesheetString(),
            TokenType.Color => "#" + Data,
            TokenType.Range => "U+" + Data,
            TokenType.Url => FunctionName.StylesheetFunction(Data.StylesheetString()),
            TokenType.Comment => string.Concat("/*", Data, IsValid ? string.Empty : "*/"),
            TokenType.Dimension or TokenType.Percentage => Data + Unit,
            _ => Data,
        };
    }
}
