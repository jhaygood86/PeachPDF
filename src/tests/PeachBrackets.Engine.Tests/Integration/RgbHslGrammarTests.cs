using PeachBrackets.Html.Parse;
using PeachDrawing.Core;
using PeachPDF.Rendering;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// The grammar of <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c> and <c>hsla()</c> (CSS Color 4): the legacy comma form and the modern space form with a slash alpha, never a
    /// mix of the two; hex of 3, 4, 6 and 8 digits; out-of-range components clamped. The quick readers for the common spelling must take only what the grammar takes.
    /// </summary>
    public class RgbHslGrammarTests
    {
        private static CssValueParser Parser() => new(new HtmlRenderContext());

        private static (int R, int G, int B, int A) Parse(string value)
        {
            var color = Parser().GetActualColor(value);
            return (color.R, color.G, color.B, color.A);
        }

        [Theory]
        [InlineData("#0f0", 0, 255, 0, 255)]
        [InlineData("#0f08", 0, 255, 0, 136)]
        [InlineData("#00ff00", 0, 255, 0, 255)]
        [InlineData("#00ff0080", 0, 255, 0, 128)]
        [InlineData("rgb(1, 2, 3)", 1, 2, 3, 255)]
        [InlineData("rgb(1,2,3)", 1, 2, 3, 255)]
        [InlineData("rgb(1 2 3)", 1, 2, 3, 255)]
        [InlineData("rgba(1, 2, 3, 0.5)", 1, 2, 3, 128)]
        [InlineData("rgba(  0  ,  255  ,  0  ,  1  )", 0, 255, 0, 255)]
        [InlineData("rgb(1 2 3 / 50%)", 1, 2, 3, 128)]
        [InlineData("rgb(10%, 20%, 30%)", 26, 51, 77, 255)]
        [InlineData("rgb(300, -5, 128)", 255, 0, 128, 255)]
        [InlineData("rgba(0, 255, 0, -2)", 0, 255, 0, 0)]
        [InlineData("rgba(0, 255, 0, 2)", 0, 255, 0, 255)]
        [InlineData("hsl(120, 100%, 50%)", 0, 255, 0, 255)]
        [InlineData("hsl(120 100% 50% / 0.5)", 0, 255, 0, 128)]
        [InlineData("hsl(480, 100%, 50%)", 0, 255, 0, 255)]
        [InlineData("hsl(-240, 100%, 50%)", 0, 255, 0, 255)]
        [InlineData("hsl(360120, 100%, 50%)", 0, 255, 0, 255)]
        [InlineData("hsl(-360240, 100%, 50%)", 0, 255, 0, 255)]
        [InlineData("hsl(120, -200%, 50%)", 128, 128, 128, 255)]
        public void AValidColor_IsRead(string value, int r, int g, int b, int a)
        {
            Assert.True(Parser().IsColorValid(value));
            Assert.Equal((r, g, b, a), Parse(value));
        }

        [Theory]
        [InlineData("rgb(255, 0, 0 / 1)")]
        [InlineData("rgba(255, 0, 0 / 1)")]
        [InlineData("rgb(1 2 3 /)")]
        [InlineData("rgb(1 2 3 / 1 2)")]
        [InlineData("rgb(1, 2, 3,)")]
        [InlineData("rgb(, 1, 2, 3)")]
        [InlineData("rgb(1, 2)")]
        [InlineData("rgb(1, 2, 3, 4, 5)")]
        [InlineData("rgb(1 2 3 0.5)")]
        [InlineData("rgb(1 2, 3)")]
        [InlineData("rgba(100%, 0, 0, 1)")]
        [InlineData("rgb(50%, 0, 0)")]
        [InlineData("rgb(0, 0, 0 0.5)")]
        [InlineData("rgb(none, 0, 0)")]
        [InlineData("hsl(0, 100%, 50%,)")]
        [InlineData("hsl(0, 100%, 50% / 1)")]
        [InlineData("hsla(0, 100%, 50% / 1)")]
        [InlineData("hsl(0, 100, 50)")]
        [InlineData("hsl(0 100% 50% 0.5)")]
        [InlineData("#0f")]
        [InlineData("#0f0f0")]
        [InlineData("#0f0f0f0")]
        [InlineData("#0f0f0f0f0")]
        [InlineData("#ggg")]
        public void WhatTheGrammarRejects_IsRejected(string value)
        {
            Assert.False(Parser().IsColorValid(value));
        }
    }
}
