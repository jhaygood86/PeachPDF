using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core
{
    /// <summary>
    /// Unit tests for <see cref="RegisteredProperty"/> — the <c>@property</c> registration model: rule
    /// validity (CSS Properties &amp; Values API §3) and the <c>syntax</c> matcher used to validate a
    /// value against a registered property's declared type at computed-value time.
    /// </summary>
    public class RegisteredPropertyTests
    {
        private static readonly CssValueParser ValueParser = new(new PdfSharpAdapter());

        private static RegisteredProperty? Register(string body)
        {
            var sheet = CssParser.ParseStyleSheet($"@property --p {{ {body} }}");
            var rule = (PropertyRule)sheet.Rules[0]!;
            return RegisteredProperty.FromRule(rule, ValueParser);
        }

        // ── Rule validity ────────────────────────────────────────────────────────

        [Fact]
        public void TypedSyntax_WithMatchingInitial_IsRegistered()
        {
            var reg = Register("syntax: \"<length>\"; inherits: true; initial-value: 4px;");
            Assert.NotNull(reg);
            Assert.Equal("<length>", reg!.Syntax);
            Assert.Equal("4px", reg.InitialValue);
            Assert.True(reg.Inherits);
        }

        [Fact]
        public void TypedSyntax_MissingInitial_IsInvalid()
        {
            Assert.Null(Register("syntax: \"<color>\"; inherits: false;"));
        }

        [Fact]
        public void TypedSyntax_InitialNotMatchingSyntax_IsInvalid()
        {
            Assert.Null(Register("syntax: \"<length>\"; inherits: false; initial-value: red;"));
        }

        [Fact]
        public void MissingSyntax_IsInvalid()
        {
            Assert.Null(Register("inherits: false; initial-value: 4px;"));
        }

        [Fact]
        public void MissingInherits_IsInvalid()
        {
            // `inherits` is a required descriptor (§3) — a rule without it is invalid and ignored.
            Assert.Null(Register("syntax: \"<length>\"; initial-value: 4px;"));
        }

        [Fact]
        public void InvalidInheritsValue_IsInvalid()
        {
            Assert.Null(Register("syntax: \"*\"; inherits: maybe;"));
        }

        [Fact]
        public void InitialValueContainingVar_IsInvalid()
        {
            // initial-value must be computationally independent — a var() reference invalidates the rule
            // (and would otherwise open an infinite-recursion path through the resolver's fallback).
            Assert.Null(Register("syntax: \"*\"; inherits: false; initial-value: var(--x);"));
            Assert.Null(Register("syntax: \"<color>\"; inherits: false; initial-value: var(--x);"));
        }

        [Fact]
        public void RatioSyntax_WithMatchingInitial_IsRegistered()
        {
            // <ratio> = <number [0,∞]> [ / <number [0,∞]> ]? (CSS Values 4 §11).
            var reg = Register("syntax: \"<ratio>\"; inherits: false; initial-value: 16 / 9;");
            Assert.NotNull(reg);
            Assert.Equal("<ratio>", reg!.Syntax);
            Assert.Equal("16/9", reg.InitialValue); // normalized (whitespace stripped) at parse time
        }

        [Fact]
        public void RatioSyntax_AutoInitial_IsInvalid()
        {
            // <ratio> notably does NOT permit `auto` (that belongs to the aspect-ratio grammar, not the
            // data type), so this initial-value fails syntax validation and the rule is dropped.
            Assert.Null(Register("syntax: \"<ratio>\"; inherits: false; initial-value: auto;"));
        }

        [Fact]
        public void UniversalSyntax_MayOmitInitial()
        {
            var reg = Register("syntax: \"*\"; inherits: false;");
            Assert.NotNull(reg);
            Assert.Equal("*", reg!.Syntax);
            Assert.Null(reg.InitialValue);
            Assert.False(reg.Inherits);
        }

        // ── Syntax matcher ─────────────────────────────────────────────────────────

        [Theory]
        [InlineData("<length>", "10px", true)]
        [InlineData("<length>", "2em", true)]
        [InlineData("<length>", "calc(1px + 2px)", true)]
        [InlineData("<length>", "red", false)]
        [InlineData("<length>", "50%", false)]
        [InlineData("<percentage>", "50%", true)]
        [InlineData("<percentage>", "10px", false)]
        [InlineData("<length-percentage>", "50%", true)]
        [InlineData("<length-percentage>", "10px", true)]
        [InlineData("<number>", "3.5", true)]
        [InlineData("<number>", "10px", false)]
        [InlineData("<integer>", "42", true)]
        [InlineData("<integer>", "4.2", false)]
        [InlineData("<angle>", "45deg", true)]
        [InlineData("<angle>", "1turn", true)]
        [InlineData("<angle>", "5", false)]
        [InlineData("<ratio>", "16 / 9", true)]
        [InlineData("<ratio>", "3", true)]
        [InlineData("<ratio>", "auto", false)]
        [InlineData("<ratio>", "16px / 9", false)]
        [InlineData("<color>", "rebeccapurple", true)]
        [InlineData("<color>", "#abc", true)]
        [InlineData("<color>", "notacolor", false)]
        [InlineData("<custom-ident>", "my-ident", true)]
        [InlineData("<custom-ident>", "10px", false)]
        [InlineData("<custom-ident>", "a.b", false)]
        [InlineData("<string>", "\"hello\"", true)]
        [InlineData("<string>", "hello", false)]
        [InlineData("auto", "auto", true)]
        [InlineData("auto", "none", false)]
        [InlineData("auto", "AUTO", false)] // ident literals match case-sensitively
        public void Accepts_SingleComponent(string syntax, string value, bool expected)
        {
            // A universal '*' registration is used only to build the RegisteredProperty; Accepts is called with
            // the component syntax under test directly (via a rule whose syntax IS that component).
            var reg = Register($"syntax: \"{syntax}\"; inherits: false; initial-value: {InitialFor(syntax)};");
            Assert.NotNull(reg);
            Assert.Equal(expected, reg!.Accepts(value, ValueParser));
        }

        [Theory]
        [InlineData("<length> | <color>", "10px", true)]
        [InlineData("<length> | <color>", "red", true)]
        [InlineData("<length> | <color>", "3", false)]
        public void Accepts_Alternation(string syntax, string value, bool expected)
        {
            var reg = Register($"syntax: \"{syntax}\"; inherits: false; initial-value: 1px;");
            Assert.NotNull(reg);
            Assert.Equal(expected, reg!.Accepts(value, ValueParser));
        }

        [Theory]
        [InlineData("<length>+", "1px 2px 3px", true)]
        [InlineData("<length>+", "1px red", false)]
        [InlineData("<color>#", "red, blue, green", true)]
        [InlineData("<color>#", "red, 5px", false)]
        [InlineData("<color>#", "rgb(1, 2, 3), rgb(4, 5, 6)", true)] // commas inside rgb() don't split the list
        public void Accepts_ListMultipliers(string syntax, string value, bool expected)
        {
            var reg = Register($"syntax: \"{syntax}\"; inherits: false; initial-value: {(syntax.EndsWith('#') ? "red" : "1px")};");
            Assert.NotNull(reg);
            Assert.Equal(expected, reg!.Accepts(value, ValueParser));
        }

        [Fact]
        public void Accepts_ListWithQuotedSeparator_DoesNotSplitInsideQuotes()
        {
            // A comma inside a quoted <string> item must not split the '#' list.
            var reg = Register("syntax: \"<string>#\"; inherits: false; initial-value: \"x\";");
            Assert.NotNull(reg);
            Assert.True(reg!.Accepts("\"a,b\", \"c\"", ValueParser));
        }

        [Fact]
        public void Universal_AcceptsAnything()
        {
            var reg = Register("syntax: \"*\"; inherits: false;");
            Assert.NotNull(reg);
            Assert.True(reg!.Accepts("literally anything <here>", ValueParser));
        }

        // A modeled data type not in our grammar (<image>) degrades to "accept" rather than wrongly reject.
        [Fact]
        public void UnmodeledType_DegradesToAccept()
        {
            var reg = Register("syntax: \"<image>\"; inherits: false; initial-value: whatever;");
            Assert.NotNull(reg);
            Assert.True(reg!.Accepts("url(x.png)", ValueParser));
        }

        private static string InitialFor(string syntax) => syntax switch
        {
            "<length>" => "0px",
            "<percentage>" => "0%",
            "<length-percentage>" => "0px",
            "<number>" => "0",
            "<integer>" => "0",
            "<angle>" => "0deg",
            "<ratio>" => "1",
            "<color>" => "black",
            "<custom-ident>" => "x",
            "<string>" => "\"x\"",
            _ => "auto",
        };
    }
}
