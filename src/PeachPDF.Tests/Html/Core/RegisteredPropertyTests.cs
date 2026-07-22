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
        [InlineData("<url>", "url(a.png)", true)]
        [InlineData("<url>", "url(https://x/y.svg)", true)]
        [InlineData("<url>", "red", false)]
        [InlineData("<url>", "linear-gradient(red, blue)", false)] // a gradient is an <image>, not a <url>
        [InlineData("<image>", "url(a.png)", true)]
        [InlineData("<image>", "linear-gradient(red, blue)", true)]
        [InlineData("<image>", "radial-gradient(red, blue)", true)]
        [InlineData("<image>", "conic-gradient(red, blue)", true)]
        [InlineData("<image>", "red", false)]
        [InlineData("<time>", "5s", true)]
        [InlineData("<time>", "250ms", true)]
        [InlineData("<time>", "10px", false)]
        [InlineData("<time>", "5", false)]
        [InlineData("<resolution>", "96dpi", true)]
        [InlineData("<resolution>", "2dppx", true)]
        [InlineData("<resolution>", "5", false)]
        [InlineData("<transform-function>", "rotate(45deg)", true)]
        [InlineData("<transform-function>", "translate(1px, 2px)", true)]
        [InlineData("<transform-function>", "matrix(1, 0, 0, 1, 0, 0)", true)]
        [InlineData("<transform-function>", "red", false)]
        [InlineData("<transform-function>", "translate(1px) rotate(1deg)", false)] // a list, not one function
        [InlineData("<transform-list>", "translate(1px) rotate(1deg)", true)]
        [InlineData("<transform-list>", "scale(2)", true)]
        [InlineData("<transform-list>", "red", false)]
        [InlineData("<transform-list>", "rotate(1deg) red", false)]
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

        // A modeled data type whose initial-value doesn't match is now invalid (the rule drops), instead of
        // degrading to "accept" — so an <image> with a garbage initial-value no longer registers.
        [Fact]
        public void ModeledType_InitialNotMatching_IsInvalid()
        {
            Assert.Null(Register("syntax: \"<image>\"; inherits: false; initial-value: whatever;"));
            Assert.Null(Register("syntax: \"<url>\"; inherits: false; initial-value: red;"));
            Assert.Null(Register("syntax: \"<transform-list>\"; inherits: false; initial-value: nope;"));
        }

        // A genuinely non-standard/future <foo> data type still degrades to "accept" (safety fallback).
        [Fact]
        public void NonStandardType_DegradesToAccept()
        {
            var reg = Register("syntax: \"<future-thing>\"; inherits: false; initial-value: whatever;");
            Assert.NotNull(reg);
            Assert.True(reg!.Accepts("anything at all", ValueParser));
        }

        // ── Computational independence of initial-value calc() (§3) ───────────────────

        [Theory]
        [InlineData("<length>", "calc(1px + 2px)", true)]   // absolute-only → independent
        [InlineData("<length>", "calc(2px * 3)", true)]
        [InlineData("<length>", "calc(1em + 2px)", false)]  // em is font-relative
        [InlineData("<length>", "calc(1rem)", false)]
        [InlineData("<length-percentage>", "calc(50% + 1px)", false)] // percentage term
        [InlineData("<number>", "calc(2 * 3)", true)]
        [InlineData("<angle>", "calc(45deg + 10deg)", true)]
        public void InitialValueCalc_ComputationalIndependence(string syntax, string initial, bool valid)
        {
            var reg = Register($"syntax: \"{syntax}\"; inherits: false; initial-value: {initial};");
            Assert.Equal(valid, reg is not null);
        }

        [Fact]
        public void ComputationalIndependence_AppliesOnlyToInitialValue()
        {
            // A registered property still ACCEPTS a font-relative calc() at computed-value time — the
            // independence rule constrains the initial-value only, not values set later.
            var reg = Register("syntax: \"<length>\"; inherits: false; initial-value: 0px;");
            Assert.NotNull(reg);
            Assert.True(reg!.Accepts("calc(1em + 2px)", ValueParser));
        }

        // ── BuildRegistry (shared by the HTML cascade and the standalone-SVG loader) ──

        private static CssData StyleData(string css)
        {
            var data = new CssData();
            data.Stylesheets.Add(CssParser.ParseStyleSheet(css));
            return data;
        }

        [Fact]
        public void BuildRegistry_RegistersValidRules_DropsInvalid_LastDuplicateWins()
        {
            var registry = RegisteredProperty.BuildRegistry(StyleData(
                "@property --a { syntax: \"<color>\"; inherits: false; initial-value: red; }" +
                "@property --bad { syntax: \"<length>\"; inherits: false; initial-value: red; }" + // typed w/ mismatched initial → dropped
                "@property --a { syntax: \"<color>\"; inherits: true; initial-value: blue; }"), ValueParser);

            Assert.False(registry.ContainsKey("--bad"));
            Assert.True(registry.TryGetValue("--a", out var a));
            Assert.Equal("blue", a!.InitialValue); // later duplicate wins
            Assert.True(a.Inherits);
        }

        [Fact]
        public void BuildRegistry_NoPropertyRules_IsEmpty()
        {
            Assert.Empty(RegisteredProperty.BuildRegistry(StyleData("rect { fill: red; }"), ValueParser));
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
            "<url>" => "url(x)",
            "<image>" => "url(x)",
            "<time>" => "0s",
            "<resolution>" => "96dpi",
            "<transform-function>" => "scale(1)",
            "<transform-list>" => "scale(1)",
            "<custom-ident>" => "x",
            "<string>" => "\"x\"",
            _ => "auto",
        };
    }
}
