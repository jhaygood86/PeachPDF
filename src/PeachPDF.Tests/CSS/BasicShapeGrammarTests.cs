using PeachPDF.CSS;
using PeachPDF.Html.Core.Parse;
using System.Linq;
using Xunit;

namespace PeachPDF.Tests.CSS
{
    /// <summary>
    /// Tests for the shared <see cref="BasicShapeGrammar"/> (Layer-agnostic parse of the
    /// <c>polygon()/inset()/circle()/ellipse()</c> basic-shape grammar) and the <c>clip-path</c>
    /// Layer-A converter (<see cref="ClipPathValueConverter"/>) that accepts/rejects and preserves it.
    /// </summary>
    public class BasicShapeGrammarTests : CssConstructionFunctions
    {
        private static BasicShapeGrammar.ParsedBasicShape Parse(string value) =>
            BasicShapeGrammar.TryParse(CssValueParser.GetCssTokens(value));

        // ─── polygon() ─────────────────────────────────────────────────────────

        [Fact]
        public void Polygon_ThreePoints_ParsesWithDefaultNonzeroFill()
        {
            var shape = Parse("polygon(0 0, 100% 0, 50% 100%)");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.BasicShapeKind.Polygon, shape.Kind);
            Assert.Equal(BasicShapeGrammar.FillRule.Nonzero, shape.PolygonFillRule);
            Assert.Equal(3, shape.PolygonPoints.Count);
            Assert.Equal(("0", "0"), (shape.PolygonPoints[0].X, shape.PolygonPoints[0].Y));
            Assert.Equal(("100%", "0"), (shape.PolygonPoints[1].X, shape.PolygonPoints[1].Y));
            Assert.Equal(("50%", "100%"), (shape.PolygonPoints[2].X, shape.PolygonPoints[2].Y));
        }

        [Theory]
        [InlineData("polygon(evenodd, 0 0, 10px 0, 0 10px)", true)]
        [InlineData("polygon(nonzero, 0 0, 10px 0, 0 10px)", false)]
        public void Polygon_ExplicitFillRule_IsCaptured(string value, bool expectEvenodd)
        {
            var shape = Parse(value);

            Assert.NotNull(shape);
            var expected = expectEvenodd ? BasicShapeGrammar.FillRule.Evenodd : BasicShapeGrammar.FillRule.Nonzero;
            Assert.Equal(expected, shape.PolygonFillRule);
            Assert.Equal(3, shape.PolygonPoints.Count);
        }

        [Fact]
        public void Polygon_SinglePoint_IsValid()
        {
            var shape = Parse("polygon(50% 50%)");

            Assert.NotNull(shape);
            Assert.Single(shape.PolygonPoints);
        }

        [Theory]
        [InlineData("polygon()")]                      // no points
        [InlineData("polygon(0)")]                     // odd token count in a pair
        [InlineData("polygon(0 0, 100%)")]             // second pair incomplete
        [InlineData("polygon(banana, 0 0)")]           // bad fill-rule ident
        [InlineData("polygon(evenodd)")]               // fill-rule but no points
        [InlineData("polygon(0 0,, 10px 10px)")]       // empty middle group
        [InlineData("polygon(red 0, 10px 0)")]         // non-length component
        public void Polygon_Malformed_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        // ─── inset() ───────────────────────────────────────────────────────────

        [Theory]
        [InlineData("inset(10px)", "10px", "10px", "10px", "10px")]
        [InlineData("inset(10px 20px)", "10px", "20px", "10px", "20px")]
        [InlineData("inset(10px 20px 30px)", "10px", "20px", "30px", "20px")]
        [InlineData("inset(1px 2px 3px 4px)", "1px", "2px", "3px", "4px")]
        public void Inset_ShorthandFill_ExpandsToTopRightBottomLeft(string value, string top, string right, string bottom, string left)
        {
            var shape = Parse(value);

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.BasicShapeKind.Inset, shape.Kind);
            Assert.Equal([top, right, bottom, left], shape.InsetEdges);
            Assert.False(shape.InsetHasRound);
        }

        [Fact]
        public void Inset_WithRound_CapturesRadiusButFlagsRound()
        {
            var shape = Parse("inset(10px round 5px)");

            Assert.NotNull(shape);
            Assert.Equal(["10px", "10px", "10px", "10px"], shape.InsetEdges);
            Assert.True(shape.InsetHasRound);
            Assert.NotEmpty(shape.InsetRoundRadius);
        }

        [Fact]
        public void Inset_PercentageEdges_AreValid()
        {
            var shape = Parse("inset(10% 20%)");

            Assert.NotNull(shape);
            Assert.Equal(["10%", "20%", "10%", "20%"], shape.InsetEdges);
        }

        [Theory]
        [InlineData("inset()")]                    // no offsets
        [InlineData("inset(1px 2px 3px 4px 5px)")] // more than 4 offsets
        [InlineData("inset(10px round)")]          // round with no radius
        [InlineData("inset(red)")]                 // non-length offset
        public void Inset_Malformed_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        // ─── circle() ──────────────────────────────────────────────────────────

        [Fact]
        public void Circle_Empty_DefaultsClosestSideAndCenter()
        {
            var shape = Parse("circle()");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.BasicShapeKind.Circle, shape.Kind);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.ClosestSide, shape.RadiusX.Kind);
            Assert.Equal("50%", shape.CenterX);
            Assert.Equal("50%", shape.CenterY);
        }

        [Fact]
        public void Circle_FarthestSide_Keyword()
        {
            var shape = Parse("circle(farthest-side)");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.FarthestSide, shape.RadiusX.Kind);
        }

        [Fact]
        public void Circle_ExplicitRadius_AndPosition()
        {
            var shape = Parse("circle(50px at 10px 20px)");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.LengthPercentage, shape.RadiusX.Kind);
            Assert.Equal("50px", shape.RadiusX.Length);
            Assert.Equal("10px", shape.CenterX);
            Assert.Equal("20px", shape.CenterY);
        }

        [Fact]
        public void Circle_PositionKeywords_ResolveToPercentages()
        {
            var shape = Parse("circle(closest-side at left top)");

            Assert.NotNull(shape);
            Assert.Equal("0%", shape.CenterX);
            Assert.Equal("0%", shape.CenterY);
        }

        [Fact]
        public void Circle_PositionRightBottom_ResolveToHundredPercent()
        {
            var shape = Parse("circle(at right bottom)");

            Assert.NotNull(shape);
            Assert.Equal("100%", shape.CenterX);
            Assert.Equal("100%", shape.CenterY);
        }

        [Theory]
        [InlineData("circle(10px 20px)")]   // two radii (that's ellipse)
        [InlineData("circle(at)")]          // "at" with no position
        [InlineData("circle(banana)")]      // junk radius
        [InlineData("circle(-5px)")]        // negative <shape-radius> is invalid
        [InlineData("circle(-10% at center)")]
        [InlineData("ellipse(-5px 10px)")]  // a negative axis radius invalidates the whole shape
        public void Circle_Malformed_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        // ─── ellipse() ─────────────────────────────────────────────────────────

        [Fact]
        public void Ellipse_Empty_DefaultsBothRadiiClosestSide()
        {
            var shape = Parse("ellipse()");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.BasicShapeKind.Ellipse, shape.Kind);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.ClosestSide, shape.RadiusX.Kind);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.ClosestSide, shape.RadiusY.Kind);
        }

        [Fact]
        public void Ellipse_TwoRadii_AndPosition()
        {
            var shape = Parse("ellipse(40px 20% at 25% 75%)");

            Assert.NotNull(shape);
            Assert.Equal("40px", shape.RadiusX.Length);
            Assert.Equal("20%", shape.RadiusY.Length);
            Assert.Equal("25%", shape.CenterX);
            Assert.Equal("75%", shape.CenterY);
        }

        [Fact]
        public void Ellipse_MixedKeywordRadii()
        {
            var shape = Parse("ellipse(closest-side farthest-side at right bottom)");

            Assert.NotNull(shape);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.ClosestSide, shape.RadiusX.Kind);
            Assert.Equal(BasicShapeGrammar.ShapeRadiusKind.FarthestSide, shape.RadiusY.Kind);
            Assert.Equal("100%", shape.CenterX);
            Assert.Equal("100%", shape.CenterY);
        }

        [Theory]
        [InlineData("ellipse(40px)")]           // exactly one radius is invalid
        [InlineData("ellipse(40px 20px 10px)")] // three radii
        public void Ellipse_Malformed_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        // ─── top-level rejection ────────────────────────────────────────────────

        [Theory]
        [InlineData("none")]
        [InlineData("banana")]
        [InlineData("url(#clip)")]
        [InlineData("rect(0 0 0 0)")]
        public void NonBasicShape_ReturnsNull(string value)
        {
            Assert.Null(Parse(value));
        }

        // ─── Layer A: converter accept/reject via the parser ────────────────────

        [Theory]
        [InlineData("none")]
        [InlineData("polygon(0 0, 100% 0, 50% 100%)")]
        [InlineData("inset(10px 20px round 4px)")]
        [InlineData("circle(50px at center)")]
        [InlineData("ellipse(closest-side farthest-side)")]
        public void ClipPath_ValidValue_SurvivesParsing(string value)
        {
            var property = ParseDeclaration($"clip-path: {value}");

            Assert.IsType<ClipPathProperty>(property);
            var concrete = (ClipPathProperty)property;
            Assert.True(concrete.HasValue);
            // Authored text is preserved verbatim for the render layer to re-parse.
            Assert.Equal(value, concrete.Value);
        }

        [Theory]
        [InlineData("banana")]
        [InlineData("polygon(0)")]
        [InlineData("circle(10px 20px)")]
        public void ClipPath_InvalidValue_IsDropped(string value)
        {
            var property = ParseDeclaration($"clip-path: {value}");

            Assert.IsType<ClipPathProperty>(property);
            Assert.False(((ClipPathProperty)property).HasValue);
        }

        [Fact]
        public void ClipPath_InStyleSheet_ValidSurvives_InvalidDropped()
        {
            var valid = ParseStyleSheet("div{clip-path:polygon(0 0, 100% 0, 50% 100%)}");
            var validRule = valid.Rules.OfType<StyleRule>().Single();
            Assert.Equal("polygon(0 0, 100% 0, 50% 100%)", validRule.Style.GetPropertyValue("clip-path"));

            var invalid = ParseStyleSheet("div{clip-path:banana}");
            var invalidRule = invalid.Rules.OfType<StyleRule>().Single();
            Assert.Equal(string.Empty, invalidRule.Style.GetPropertyValue("clip-path"));
        }
    }
}
