using PeachDrawing.Core;
using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Svg;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Xunit;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// <c>text-decoration-thickness</c>, <c>text-underline-offset</c>, <c>text-underline-position</c>, the <c>double</c> style and
    /// <c>text-decoration-skip-ink</c> on SVG text, asserted on the <c>DrawLine</c> calls the renderer makes.
    /// </summary>
    public class SvgTextDecorationGeometryTests
    {
        private static readonly PdfSharpAdapter Adapter = new() { PixelsPerPoint = 1.0 };

        /// <summary>A canvas whose ink-crossing answer is fixed, standing in for a real glyph outline.</summary>
        private sealed class InkCanvas(IReadOnlyList<InkSpan>? crossings) : TestRecordingGraphics
        {
            public override IReadOnlyList<InkSpan>? GetInkCrossings(string str, Font font, PaintPoint origin, double bandTop, double bandBottom,
                double letterSpacing = 0, ShapeSettings? features = null) => crossings;
        }

        private static T Render<T>(T g, string textAttrs, string text = "Hi", string svgAttrs = "") where T : TestRecordingGraphics
        {
            var markup = $"""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 200 100" {svgAttrs}><text x="10" y="50" font-size="20" {textAttrs}>{text}</text></svg>""";
            var document = SvgTreeBuilder.Build(new XElementSvgSourceNode(XDocument.Parse(markup).Root!), Adapter);
            SvgRenderer.RenderInto(g, document, new Rect(0, 0, 200, 100));
            return g;
        }

        private static TestRecordingGraphics.DrawLineCall[] Lines(TestRecordingGraphics g) => g.Log.OfType<TestRecordingGraphics.DrawLineCall>().ToArray();

        [Fact]
        public void Thickness_Length_SetsPenWidth()
        {
            var g = Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-decoration-thickness="3" """);

            Assert.Equal(3, Assert.Single(Lines(g)).Width);
        }

        [Fact]
        public void Thickness_Percentage_IsOfFontSize()
        {
            var g = Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-decoration-thickness="10%" """);

            Assert.Equal(2, Assert.Single(Lines(g)).Width, 3);
        }

        [Fact]
        public void Thickness_AutoAndAbsent_KeepOneUnit()
        {
            Assert.Equal(1, Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """))).Width);
            Assert.Equal(1, Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-decoration-thickness="auto" """))).Width);
        }

        [Fact]
        public void Thickness_ViaShorthand()
        {
            var g = Render(new TestRecordingGraphics(), """style="text-decoration: underline 4px" """);

            Assert.Equal(4, Assert.Single(Lines(g)).Width);
        }

        [Fact]
        public void UnderlineOffset_MovesLineAwayFromText()
        {
            var plain = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """)));
            var offset = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-underline-offset="5" """)));

            Assert.Equal(plain.Y1 + 5, offset.Y1, 3);
        }

        [Fact]
        public void UnderlineOffset_IsInherited()
        {
            var plain = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """)));
            var inherited = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """, svgAttrs: """text-underline-offset="4" """)));

            Assert.Equal(plain.Y1 + 4, inherited.Y1, 3);
        }

        [Fact]
        public void UnderlinePosition_Under_SitsBelowAutoPosition()
        {
            var auto = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """)));
            var under = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-underline-position="under" """)));

            Assert.True(under.Y1 > auto.Y1);
        }

        [Fact]
        public void UnderlinePosition_FromFont_DiffersFromAuto()
        {
            var auto = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """)));
            var fromFont = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-underline-position="from-font" """)));

            Assert.NotEqual(auto.Y1, fromFont.Y1);
        }

        [Fact]
        public void DoubleStyle_DrawsTwoLines()
        {
            var g = Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-decoration-style="double" text-decoration-thickness="3" """);

            var lines = Lines(g);
            Assert.Equal(2, lines.Length);
            Assert.NotEqual(lines[0].Y1, lines[1].Y1);
            Assert.True(lines.All(l => l.Width < 3));
        }

        [Fact]
        public void SkipInk_Auto_BreaksUnderlineAroundInk()
        {
            var g = Render(new InkCanvas([new InkSpan(14, 18)]), """text-decoration-line="underline" """);

            var lines = Lines(g);
            Assert.Equal(2, lines.Length);
            Assert.True(lines[0].X2 < 14 && lines[1].X1 > 18);
        }

        [Fact]
        public void SkipInk_None_KeepsOneUnbrokenLine()
        {
            var g = Render(new InkCanvas([new InkSpan(14, 18)]), """text-decoration-line="underline" text-decoration-skip-ink="none" """);

            Assert.Single(Lines(g));
        }

        [Fact]
        public void SkipInk_NeverAppliesToLineThrough()
        {
            var g = Render(new InkCanvas([new InkSpan(14, 18)]), """text-decoration-line="line-through" """);

            Assert.Single(Lines(g));
        }

        [Fact]
        public void SkipInk_Auto_LeavesCjkAlone_ButAllBreaksIt()
        {
            const string cjk = "中文";
            var auto = Render(new InkCanvas([new InkSpan(14, 18)]), """text-decoration-line="underline" """, cjk);
            Assert.Single(Lines(auto));

            var all = Render(new InkCanvas([new InkSpan(14, 18)]), """text-decoration-line="underline" text-decoration-skip-ink="all" """, cjk);
            Assert.True(Lines(all).Length > 1);
        }

        [Fact]
        public void ShorthandWithoutColor_FollowsTheFill()
        {
            var g = Render(new TestRecordingGraphics(), """fill="rgb(255,0,0)" style="text-decoration: underline" """);

            var line = Assert.Single(Lines(g));
            Assert.Equal(255, line.PaintColor.R);
            Assert.Equal(0, line.PaintColor.G);
        }

        [Theory]
        [InlineData("-2")]
        [InlineData("NaN%")]
        [InlineData("Infinity%")]
        public void InvalidThickness_FallsBackToAuto(string value)
        {
            var g = Render(new TestRecordingGraphics(), $"text-decoration-line=\"underline\" text-decoration-thickness=\"{value}\" ");

            Assert.Equal(1, Assert.Single(Lines(g)).Width);
        }

        [Fact]
        public void InvalidOffset_IsIgnored()
        {
            var plain = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" """)));
            var bad = Assert.Single(Lines(Render(new TestRecordingGraphics(), """text-decoration-line="underline" text-underline-offset="NaN%" """)));

            Assert.Equal(plain.Y1, bad.Y1, 3);
        }
    }
}
