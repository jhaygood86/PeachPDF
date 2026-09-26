using PeachDrawing.Text.Shaping;
using PeachPDF.Adapters;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Svg;
using PeachDrawing.Text.Internal.Text;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PeachPDF.Tests.Svg
{
    /// <summary>
    /// Exercises <see cref="SvgFilterEvaluator"/>'s graph-resolution logic (which tile each primitive's
    /// <c>in</c>/<c>in2</c>/<c>Inputs</c> actually resolves to - named non-adjacent results, the implicit
    /// "previous result" default, lazy <c>SourceAlpha</c> materialization) against a lightweight
    /// <see cref="FakeFilterGraphics"/> that labels every tile with what was drawn into it, rather than a
    /// real PDF stack - this is a structural/adjacency proof of the RESOLUTION logic specifically (which
    /// tile feeds which primitive), a different concern from
    /// <see cref="SvgFilterPaintIntegrationTests"/>'s proof that each primitive KIND reaches the correct
    /// real PDF construct (/TR, /Alpha soft mask, /BM, /Luminosity).
    /// </summary>
    public class SvgFilterEvaluatorTests
    {
        private static readonly PdfSharpAdapter Adapter = new();

        private static SvgRectElement NewElement() => new() { Width = 50, Height = 50 };

        [Fact]
        public void SingleFeFlood_DrawsFloodTileOntoPage()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder().AddFlood(RColor.FromArgb(255, 0, 0), 1.0).Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var drawn = Assert.Single(page.PageLog);
            var tile = page.TilesById[FakeFilterGraphics.ExtractId(drawn)];
            Assert.Contains("Flood", tile.Ops);
        }

        [Fact]
        public void ImplicitInput_FirstPrimitive_UsesSourceGraphic()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            // feOffset with no `in` - the first (and only) primitive, so its input must be SourceGraphic.
            var filter = new SvgFilterBuilder().AddOffset(dx: 5, dy: 5).Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), tg => tg.DrawRectangle(tg.GetSolidBrush(RColor.Black), 0, 0, 10, 10));

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            var draw = Assert.Single(finalTile.Ops);
            var sourceGraphicId = FakeFilterGraphics.ExtractId(draw);
            // SourceGraphic is the very first tile Render() creates and the only one the test's own
            // paintSourceGraphic callback ever painted into (a plain flood fill) - if feOffset resolved
            // its implicit input to anything else, this tile's own Ops would be empty instead.
            Assert.Contains("Flood", page.TilesById[sourceGraphicId].Ops);
        }

        [Fact]
        public void ImplicitInput_LaterPrimitive_UsesPreviousResult_NotSourceGraphic()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            // feFlood(result="a") then feOffset with no `in` - must chain off "a" (the previous result),
            // not fall back to SourceGraphic.
            var filter = new SvgFilterBuilder()
                .AddFlood(RColor.FromArgb(255, 0, 0), 1.0, result: "a")
                .AddOffset(dx: 1, dy: 1)
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            var offsetInputId = FakeFilterGraphics.ExtractId(Assert.Single(finalTile.Ops));
            Assert.Contains("Flood", page.TilesById[offsetInputId].Ops);
        }

        [Fact]
        public void NamedResult_ReferencedNonAdjacently_ResolvesCorrectly()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            // feFlood(result="a") -> feFlood(result="b", unrelated) -> feMerge[a, b]: "a" is TWO
            // primitives back, not the immediately-previous one - proves the resolution dictionary holds
            // every earlier result, not just `lastResult`.
            var filter = new SvgFilterBuilder()
                .AddFlood(RColor.FromArgb(255, 0, 0), 1.0, result: "a")
                .AddFlood(RColor.FromArgb(0, 0, 255), 1.0, result: "b")
                .AddMerge(["a", "b"])
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            Assert.Equal(2, finalTile.Ops.Count);
            Assert.All(finalTile.Ops, op => Assert.StartsWith("Draw(", op));
        }

        [Fact]
        public void SourceAlpha_MaterializesOnce_AndIsSharedAcrossReferences()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            // Two primitives both reference SourceAlpha - it must be built exactly once (one AlphaMask op
            // against a fresh black tile), not once per reference.
            var filter = new SvgFilterBuilder()
                .AddComposite("SourceAlpha", "SourceGraphic", "in", result: "a")
                .AddComposite("SourceAlpha", "a", "over")
                .Build();

            // SourceGraphic itself is left empty (not flood-filled) here, so the ONLY "Flood"-only tile
            // that can appear is BuildSourceAlpha's own internal black tile below.
            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            // BuildSourceAlpha's own internal "black tile" is a bare, untouched-by-anything-else flood
            // fill (this filter graph has no feFlood primitives of its own, so nothing else could
            // produce a tile whose only op is "Flood"). It's created exactly once regardless of how many
            // primitives reference SourceAlpha, because the evaluator caches the built result the first
            // time and every later reference reads the cache instead of calling BuildSourceAlpha again.
            var sourceAlphaBlackTiles = page.TilesById.Values.Count(t => t.Ops is ["Flood"]);
            Assert.Equal(1, sourceAlphaBlackTiles);
        }

        [Fact]
        public void UnsupportedFilter_IsNeverRegistered_SoRenderIsNeverInvoked()
        {
            // Whole-filter rejection is proven at the SvgTreeBuilder level (SvgFilterTreeBuilderTests) -
            // this just documents the contract SvgFilterEvaluator relies on: it is only ever called with
            // an already-fully-supported SvgFilter, never asked to partially evaluate one itself.
            var filter = new SvgFilterBuilder().Build();
            Assert.Empty(filter.Primitives);
        }

        [Theory]
        [InlineData("over", 2)]
        [InlineData("in", 1)]
        [InlineData("out", 1)]
        [InlineData("atop", 2)]
        [InlineData("xor", 2)]
        public void FeComposite_EachOperator_ProducesExpectedOpCount(string op, int expectedOps)
        {
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder()
                .AddFlood(RColor.FromArgb(255, 0, 0), 1.0, result: "a")
                .AddFlood(RColor.FromArgb(0, 0, 255), 1.0, result: "b")
                .AddComposite("a", "b", op)
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            Assert.Equal(expectedOps, finalTile.Ops.Count);
        }

        [Fact]
        public void FeColorMatrix_LuminanceToAlpha_UsesLuminosityMask_NotAlphaMask()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder().AddLuminanceToAlpha().Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), tg => tg.DrawRectangle(tg.GetSolidBrush(RColor.Black), 0, 0, 10, 10));

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            var op = Assert.Single(finalTile.Ops);
            Assert.StartsWith("LumMask(", op);
        }

        [Fact]
        public void FeBlend_ForwardsBlendMode()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder()
                .AddFlood(RColor.FromArgb(255, 0, 0), 1.0, result: "a")
                .AddFlood(RColor.FromArgb(0, 0, 255), 1.0, result: "b")
                .AddBlend("a", "b", RBlendMode.Multiply)
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            Assert.Contains("Multiply", Assert.Single(finalTile.Ops));
        }

        [Fact]
        public void FeColorMatrix_And_FeComponentTransfer_BothRouteThroughDrawImageWithColorMatrix()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            var brightness = new ColorMatrix(Matrix4x4.CreateScale(1.5f), Vector4.Zero);
            var filter = new SvgFilterBuilder()
                .AddColorMatrix(brightness, result: "a")
                .AddComponentTransfer(brightness)
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), tg => tg.DrawRectangle(tg.GetSolidBrush(RColor.Black), 0, 0, 10, 10));

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            Assert.StartsWith("Matrix(", Assert.Single(finalTile.Ops));
        }

        [Fact]
        public void FeTile_CopiesItsInput_GivenNoSubregionSupport()
        {
            // See EvaluateFeTile's own remarks: without per-primitive subregions, feTile's cell IS the
            // whole filter region, so it degenerates to a single untiled copy of its input.
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder()
                .AddFlood(RColor.FromArgb(255, 0, 0), 1.0, result: "a")
                .AddTile("a")
                .Build();

            SvgFilterEvaluator.Render(page, filter, NewElement(), _ => { });

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            var op = Assert.Single(finalTile.Ops);
            Assert.StartsWith("Draw(", op);
            Assert.Contains("Flood", page.TilesById[FakeFilterGraphics.ExtractId(op)].Ops);
        }

        [Fact]
        public void FeOffset_ObjectBoundingBoxPrimitiveUnits_ScalesDxDyByTheElementsBoundingBox()
        {
            // With primitiveUnits="objectBoundingBox", dx/dy are fractions of the element's own bounding
            // box, not literal user-space units - this only changes what rect feOffset draws its input
            // into, which this FakeFilterGraphics mock doesn't record (destRect isn't logged) - so this
            // test's job is only to prove evaluation reaches and completes that branch without throwing,
            // producing the same single-draw shape a literal user-space offset would.
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilterBuilder(primitiveUnitsUserSpaceOnUse: false)
                .AddOffset(dx: 0.1, dy: 0.2)
                .Build();
            var element = new SvgRectElement { Width = 50, Height = 50 };

            SvgFilterEvaluator.Render(page, filter, element, tg => tg.DrawRectangle(tg.GetSolidBrush(RColor.Black), 0, 0, 10, 10));

            var finalTile = page.TilesById[FakeFilterGraphics.ExtractId(Assert.Single(page.PageLog))];
            Assert.StartsWith("Draw(", Assert.Single(finalTile.Ops));
        }

        [Fact]
        public void ObjectBoundingBoxRegion_ElementWithNoBoundingBox_FallsBackToLiteralRegionValues()
        {
            // SvgGeometryBounds.GetBoundingBox returns null for an element kind it doesn't statically
            // bound (e.g. <text>, which needs font measurement) - ResolveFilterRect must still produce a
            // usable (non-empty) region rather than propagating the null, by falling back to the filter's
            // own literal X/Y/Width/Height fraction values unscaled.
            var page = new FakeFilterGraphics(Adapter, null);
            var filter = new SvgFilter { FilterUnitsUserSpaceOnUse = false, X = 0, Y = 0, Width = 1, Height = 1, Primitives = [new FeFlood { Color = RColor.Black, Opacity = 1 }] };
            var element = new SvgTextElement();

            SvgFilterEvaluator.Render(page, filter, element, _ => { });

            Assert.Single(page.PageLog);
        }

        [Fact]
        public void EmptyRegion_ProducesNoDraw()
        {
            var page = new FakeFilterGraphics(Adapter, null);
            // A zero-size userSpaceOnUse region resolves literally to zero width - no bounding box
            // involved (a zero-size SvgRectElement has no reportable bounding box at all per
            // SvgGeometryBounds, which would instead fall back to the filter's own -10%/120% DEFAULTS -
            // not the empty region this test means to exercise).
            var filter = new SvgFilter { FilterUnitsUserSpaceOnUse = true, X = 0, Y = 0, Width = 0, Height = 50 };
            var element = new SvgRectElement { Width = 50, Height = 50 };

            SvgFilterEvaluator.Render(page, filter, element, _ => { });

            Assert.Empty(page.PageLog);
        }

        /// <summary>Small fluent builder for a hand-built <see cref="SvgFilter"/> graph, keeping each test's arrange step to one expression.</summary>
        private sealed class SvgFilterBuilder(bool primitiveUnitsUserSpaceOnUse = true)
        {
            private readonly List<FilterPrimitive> _primitives = [];

            public SvgFilterBuilder AddFlood(RColor color, double opacity, string? result = null) =>
                Add(new FeFlood { Color = color, Opacity = opacity, Result = result });

            public SvgFilterBuilder AddOffset(double dx, double dy, string? result = null) =>
                Add(new FeOffset { Dx = dx, Dy = dy, Result = result });

            public SvgFilterBuilder AddTile(string? in1, string? result = null) =>
                Add(new FeTile { In = in1, Result = result });

            public SvgFilterBuilder AddMerge(IReadOnlyList<string?> inputs, string? result = null) =>
                Add(new FeMerge { Inputs = inputs, Result = result });

            public SvgFilterBuilder AddComposite(string? in1, string? in2, string op, string? result = null) =>
                Add(new FeComposite { In = in1, In2 = in2, Operator = op, Result = result });

            public SvgFilterBuilder AddBlend(string? in1, string? in2, RBlendMode mode, string? result = null) =>
                Add(new FeBlend { In = in1, In2 = in2, Mode = mode, Result = result });

            public SvgFilterBuilder AddColorMatrix(ColorMatrix matrix, string? result = null) =>
                Add(new FeColorMatrix { Matrix = matrix, IsLuminanceToAlpha = false, Result = result });

            public SvgFilterBuilder AddLuminanceToAlpha(string? result = null) =>
                Add(new FeColorMatrix { Matrix = ColorMatrix.Identity, IsLuminanceToAlpha = true, Result = result });

            public SvgFilterBuilder AddComponentTransfer(ColorMatrix matrix, string? result = null) =>
                Add(new FeComponentTransfer { Matrix = matrix, Result = result });

            private SvgFilterBuilder Add(FilterPrimitive primitive)
            {
                _primitives.Add(primitive);
                return this;
            }

            public SvgFilter Build() => new()
            {
                FilterUnitsUserSpaceOnUse = false,
                PrimitiveUnitsUserSpaceOnUse = primitiveUnitsUserSpaceOnUse,
                Primitives = _primitives,
            };
        }
    }

    /// <summary>
    /// A tile-label recording <see cref="RGraphics"/> for testing <see cref="SvgFilterEvaluator"/>'s
    /// resolution logic without a real PDF stack. Every <see cref="CreateTile"/> call returns a fresh
    /// <see cref="FakeImage"/> with a unique <see cref="FakeImage.Id"/>; every draw call into a tile's own
    /// <see cref="FakeFilterGraphics"/> instance appends a short description (naming any source
    /// <see cref="FakeImage"/> ids it drew) to that tile's own <see cref="FakeImage.Ops"/> log, so a test
    /// can trace exactly which tile fed which primitive by walking <see cref="TilesById"/> from the final
    /// <see cref="PageLog"/> entry.
    /// </summary>
    internal sealed class FakeFilterGraphics : RGraphics
    {
        private static int _nextId;
        private readonly FakeImage? _owner;

        /// <summary>Every tile ever created, keyed by id - lets a test look up any tile reached from the graph, not just the outermost one.</summary>
        public Dictionary<int, FakeImage> TilesById { get; }

        /// <summary>Draw calls made directly on this instance when it is the "page" (not a tile) - <see cref="SvgFilterEvaluator.Render"/>'s final <c>DrawImage</c> lands here.</summary>
        public List<string> PageLog { get; }

        public FakeFilterGraphics(RAdapter adapter, FakeImage? owner)
            : this(adapter, owner, new Dictionary<int, FakeImage>(), []) { }

        private FakeFilterGraphics(RAdapter adapter, FakeImage? owner, Dictionary<int, FakeImage> tilesById, List<string> pageLog)
            : base(adapter, new RRect(0, 0, double.MaxValue, double.MaxValue))
        {
            _owner = owner;
            TilesById = tilesById;
            PageLog = pageLog;
        }

        /// <summary>Pulls the leading tile id out of a logged op string like <c>"Draw(3)"</c>/<c>"AlphaMask(3,4,False)"</c>.</summary>
        public static int ExtractId(string op)
        {
            var start = op.IndexOf('(') + 1;
            var end = op.IndexOfAny([',', ')'], start);
            return int.Parse(op[start..end]);
        }

        private void Log(string op)
        {
            if (_owner is { } owner) owner.Ops.Add(op);
            else PageLog.Add(op);
        }

        private static string Id(RImage image) => image is FakeImage f ? f.Id.ToString() : "?";

        public override (RGraphics Graphics, RImage Image)? CreateTile(double width, double height)
        {
            var image = new FakeImage(_nextId++);
            TilesById[image.Id] = image;
            return (new FakeFilterGraphics(_adapter, image, TilesById, PageLog), image);
        }

        public override void DrawRectangle(RBrush brush, double x, double y, double width, double height) => Log("Flood");

        public override void DrawImage(RImage image, RRect destRect, RRect srcRect) => Log($"Draw({Id(image)})");
        public override void DrawImage(RImage image, RRect destRect) => Log($"Draw({Id(image)})");

        public override void DrawImageAlphaMasked(RImage image, RImage maskImage, RRect destRect, bool invert = false) =>
            Log($"AlphaMask({Id(image)},{Id(maskImage)},{invert})");

        public override void DrawImageMasked(RImage image, RImage maskImage, RRect destRect) =>
            Log($"LumMask({Id(image)},{Id(maskImage)})");

        public override void DrawImageBlendedOver(RImage top, RImage bottom, RRect destRect, RBlendMode blendMode) =>
            Log($"Blend({Id(top)},{Id(bottom)},{blendMode})");

        public override void DrawImageWithColorMatrix(RImage image, RRect destRect, ColorMatrix matrix) =>
            Log($"Matrix({Id(image)})");

        public override void DrawImageWithOpacity(RImage image, RRect destRect, double opacity, RBlendMode blendMode = RBlendMode.Normal) { }

        public override void PushClip(RRect rect) { }
        public override void PushClip(RGraphicsPath path) { }
        public override void PushClipExclude(RRect rect) { }
        public override void PopClip() { }
        public override void PushTransform(RMatrix matrix) { }
        public override void PopTransform() { }
        public override void PushBlendMode(RBlendMode mode) { }
        public override void PopBlendMode() { }
        public override object SetAntiAliasSmoothingMode() => new object();
        public override void ReturnPreviousSmoothingMode(object? prevMode) { }
        public override RGraphicsPath GetGraphicsPath() => throw new NotSupportedException();
        public override void BeginMarkedContent(string structureType, int mcid) { }
        public override void EndMarkedContent() { }
        public override void BeginArtifact() { }
        public override void BeginVariableText() { }
        public override void EndVariableText() { }
        public override RSize MeasureString(string str, RFont font, ShapeSettings? features = null) => new(0, 0);
        public override int CountShapedGlyphs(string str, RFont font, ShapeSettings? features = null) => 0;
        public override void MeasureString(string str, RFont font, double maxWidth, out int charFit, out double charFitWidth) { charFit = 0; charFitWidth = 0; }
        public override void DrawString(string str, RFont font, RColor color, RPoint point, RSize size, double letterSpacing = 0, RFontPalette? fontPalette = null, ShapeSettings? features = null) { }
        public override void DrawGlyphs(IReadOnlyList<GlyphPlacement> glyphs, RFont font, RColor color) { }
        public override RGraphicsPath? GetTextOutline(string str, RFont font, RPoint baselineOrigin, double letterSpacing = 0, ShapeSettings? features = null) => null;
        public override void DrawLine(RPen pen, double x1, double y1, double x2, double y2) { }
        public override void DrawRectangle(RPen pen, double x, double y, double width, double height) { }
        public override void DrawPath(RPen pen, RGraphicsPath path) { }
        public override void DrawPath(RBrush brush, RGraphicsPath path) { }
        public override void DrawPolygon(RBrush brush, RPoint[] points) { }
        public override void Dispose() { }
    }

    internal sealed class FakeImage(int id) : RImage
    {
        public int Id { get; } = id;
        public List<string> Ops { get; } = [];

        public override double Width => 1;
        public override double Height => 1;
        public override bool Interpolate { get; set; } = true;
        public override void Dispose() { }
    }
}
