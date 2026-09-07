using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;

namespace PeachPDF.Tests.Html.Core.Parse
{
    /// <summary>
    /// Direct unit tests for <see cref="CssValueParser.IsSyntacticallyValidTransformList"/> — the
    /// span-based (no tokenizer) validator behind css-properties.json's "transform-list" cssDataType,
    /// including an equivalence check against the real cssom round trip
    /// (<c>PropertyFactory.Create("transform")</c> + <c>StylesheetParser.Default.ParseValue</c> +
    /// <c>TrySetValue</c>) it replaces, to prove it doesn't regress accept/reject behavior.
    /// </summary>
    public class CssValueParserIsSyntacticallyValidTransformListTests
    {
        [Theory]
        [InlineData("none")]
        [InlineData("NONE")]
        [InlineData("translate(10px)")]
        [InlineData("translate(10px, 20px)")]
        [InlineData("translate3d(10px, 20px, 5px)")]
        [InlineData("scale(2)")]
        [InlineData("scale(2, 3)")]
        [InlineData("rotate(45deg)")]
        [InlineData("rotate3d(1, 0, 0, 45deg)")]
        [InlineData("skew(10deg, 5deg)")]
        [InlineData("matrix(1, 0, 0, 1, 0, 0)")]
        [InlineData("matrix3d(1,0,0,0, 0,1,0,0, 0,0,1,0, 0,0,0,1)")]
        [InlineData("translate(10px) scale(2)")]
        [InlineData("translate(10px)scale(2)")] // no separator - accepted, matching the real grammar
        [InlineData("perspective(300px)")] // paint-unimplemented but syntactically real
        [InlineData("perspective(300px) rotateY(45deg)")]
        [InlineData("rotateY(45deg) perspective(300px)")]
        [InlineData("translate(10px")] // unclosed paren - CSS Syntax Level 3 closes it at EOF
        public void ValidValues_ReturnTrue(string value)
        {
            Assert.True(CssValueParser.IsSyntacticallyValidTransformList(value));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("banana")]
        [InlineData("banana(1)")] // unrecognized function name
        [InlineData("translate 10px)")] // missing '('
        [InlineData("translate(10px) !")] // junk after a valid function
        [InlineData("!translate(10px)")] // junk before a valid function
        public void InvalidValues_ReturnFalse(string value)
        {
            Assert.False(CssValueParser.IsSyntacticallyValidTransformList(value));
        }

        [Theory]
        [InlineData("none")]
        [InlineData("translate(10px)")]
        [InlineData("translate(10px, 20px) scale(2)")]
        [InlineData("perspective(300px)")]
        [InlineData("perspective(300px) rotateY(45deg)")]
        [InlineData("matrix(1, 0, 0, 1, 0, 0)")]
        [InlineData("")]
        [InlineData("banana")]
        [InlineData("banana(1)")]
        [InlineData("translate(10px")]
        [InlineData("translate(10px)scale(2)")]
        [InlineData("TRANSLATE(10px)")]
        [InlineData("none translate(10px)")] // "none" and a function together - not a valid alternative
        [InlineData("translate(calc(10px + 5px))")] // nested parens
        [InlineData("translate(10px))")] // extra closing paren
        [InlineData("  translate(10px)  ")] // leading/trailing whitespace
        [InlineData("translate(10px)\tscale(2)")] // tab as separator
        [InlineData("matrix(1, 0, 0, 1, 0, 0) translate(10px)")]
        [InlineData("perspective(300px) banana(1)")] // one recognized, one not
        public void AgreesWithRealCssOmRoundTrip(string value)
        {
            var fast = CssValueParser.IsSyntacticallyValidTransformList(value);

            var real = PropertyFactory.Instance.Create("transform") is not { } knownProperty ||
                       (StylesheetParser.Default.ParseValue(value) is { } tokenValue && knownProperty.TrySetValue(tokenValue));

            Assert.Equal(real, fast);
        }

        // ─── Known, deliberate divergences: an argument-shape mismatch inside a recognized function
        // (missing/insufficient arguments) is NOT rejected here, unlike the real per-function argument
        // grammar (e.g. TranslateTransformConverter's LengthOrPercentConverter.Required()). This is safe
        // because BuildFunctionMatrix's own LengthArg/AngleArg helpers already default a missing argument
        // to 0, rendering a malformed translate()/rotate()/etc. as an identity transform rather than
        // throwing - the same graceful degradation the real grammar's rejection would otherwise force at
        // the "invalid declaration dropped" level. Validating argument counts here would mean re-deriving
        // each function's real argument grammar, defeating the point of staying permissive. ───

        [Theory]
        [InlineData("translate(")]
        [InlineData("translate()")]
        [InlineData("rotate()")]
        [InlineData("scale()")]
        public void ArgumentShapeMismatch_IsAcceptedByDesign_UnlikeTheRealGrammar(string value)
        {
            Assert.True(CssValueParser.IsSyntacticallyValidTransformList(value));

            var real = PropertyFactory.Instance.Create("transform") is not { } knownProperty ||
                       (StylesheetParser.Default.ParseValue(value) is { } tokenValue && knownProperty.TrySetValue(tokenValue));
            Assert.False(real); // documents the divergence explicitly, rather than silently relying on it
        }
    }
}
