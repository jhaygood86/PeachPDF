using PeachPDF.Html.Core.Handlers;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Annotations;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// css-transforms-1 "Transform Rendering": a link annotation or form widget over an element that is painted transformed belongs where the
    /// element is drawn, not at the rectangle layout gave it. Each case generates the document twice - the element as laid out, and under a
    /// transform - and checks the annotation moved the way the transform moves the rectangle, worked out independently here from the
    /// untransformed annotation rather than read back from the engine.
    /// </summary>
    public class TransformedAnnotationTests
    {
        private const double Tolerance = 0.5;

        private static Task<PeachPdfDocument> Generate(string body, bool forms = false) =>
            new PdfGenerator().GeneratePdf(
                $"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = forms });

        /// <summary>A 100pt x 40pt block at (100pt, 100pt) whose whole area is one link, inside <paramref name="style"/>.</summary>
        private static string Block(string style, string href = "http://example.test/") =>
            $"<div style='position:absolute;left:100pt;top:100pt;width:100pt;height:40pt;{style}'><a href='{href}' style='display:block;height:40pt'>link</a></div>";

        private static PdfLinkAnnotation OnlyLink(PeachPdfDocument doc, int page = 0) =>
            Assert.IsType<PdfLinkAnnotation>(Assert.Single(doc.Pages[page].Annotations.Cast<PdfAnnotation>()));

        private static (double X1, double Y1, double X2, double Y2) RectOf(PdfAnnotation annotation)
        {
            var r = annotation.Rectangle;
            return (r.X1, r.Y1, r.X2, r.Y2);
        }

        private static void AssertRect((double X1, double Y1, double X2, double Y2) expected, PdfAnnotation annotation)
        {
            var (x1, y1, x2, y2) = RectOf(annotation);
            Assert.Equal(expected.X1, x1, Tolerance);
            Assert.Equal(expected.Y1, y1, Tolerance);
            Assert.Equal(expected.X2, x2, Tolerance);
            Assert.Equal(expected.Y2, y2, Tolerance);
        }

        /// <summary>The corners of <paramref name="rect"/> (PDF coordinates, y up) carried through <paramref name="map"/>, as their bounding box.</summary>
        private static (double X1, double Y1, double X2, double Y2) BoundsAfter(
            (double X1, double Y1, double X2, double Y2) rect, Func<(double X, double Y), (double X, double Y)> map)
        {
            var corners = new[] { (rect.X1, rect.Y1), (rect.X2, rect.Y1), (rect.X2, rect.Y2), (rect.X1, rect.Y2) }.Select(map).ToArray();
            return (corners.Min(c => c.X), corners.Min(c => c.Y), corners.Max(c => c.X), corners.Max(c => c.Y));
        }

        /// <summary>A rotation about the centre of <paramref name="rect"/> that looks clockwise on the page (CSS <c>rotate(+angle)</c>), in y-up coordinates.</summary>
        private static Func<(double X, double Y), (double X, double Y)> RotateClockwiseAboutCentre((double X1, double Y1, double X2, double Y2) rect, double degrees)
        {
            var (cx, cy) = ((rect.X1 + rect.X2) / 2, (rect.Y1 + rect.Y2) / 2);
            var angle = -degrees * Math.PI / 180;
            return p =>
            {
                var (dx, dy) = (p.X - cx, p.Y - cy);
                return (cx + dx * Math.Cos(angle) - dy * Math.Sin(angle), cy + dx * Math.Sin(angle) + dy * Math.Cos(angle));
            };
        }

        private static double[]? QuadPointsOf(PdfAnnotation annotation) =>
            annotation.Elements["/QuadPoints"] is PdfArray array
                ? array.Elements.Select(item => item is PdfReal real ? real.Value : ((PdfInteger)item).Value).ToArray()
                : null;

        // --- Links ---

        [Fact]
        public async Task Link_UnderTranslate_MovesWithTheElement()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var moved = OnlyLink(await Generate(Block("transform:translate(30pt,20pt)")));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
            Assert.Null(QuadPointsOf(moved));
        }

        [Fact]
        public async Task Link_UnderScale_FollowsTheTransformOrigin()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var scaled = OnlyLink(await Generate(Block("transform-origin:0 0;transform:scale(2,1.5)")));

            // The origin is the element's top-left corner: (x1, y2) in y-up coordinates.
            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1, y2 - (y2 - y1) * 1.5, x1 + (x2 - x1) * 2, y2), scaled);
        }

        [Fact]
        public async Task Link_UnderScale_AboutTheCentre_StaysCentred()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var scaled = OnlyLink(await Generate(Block("transform:scale(2)")));

            var (x1, y1, x2, y2) = RectOf(plain);
            var (w, h) = (x2 - x1, y2 - y1);
            AssertRect((x1 - w / 2, y1 - h / 2, x2 + w / 2, y2 + h / 2), scaled);
        }

        [Fact]
        public async Task Link_UnderAQuarterTurn_IsTheTurnedBox_WithNoQuadPoints()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var turned = OnlyLink(await Generate(Block("transform:rotate(90deg)")));

            var rect = RectOf(plain);
            AssertRect(BoundsAfter(rect, RotateClockwiseAboutCentre(rect, 90)), turned);

            // It fills its own bounding box, so the box is the whole click target.
            Assert.Null(QuadPointsOf(turned));
        }

        [Fact]
        public async Task Link_UnderARotation_IsTheBoundingBox_AndCarriesTheQuadrilateral()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var turned = OnlyLink(await Generate(Block("transform:rotate(30deg)")));

            var rect = RectOf(plain);
            var rotate = RotateClockwiseAboutCentre(rect, 30);
            AssertRect(BoundsAfter(rect, rotate), turned);

            // The drawn shape, top-left, top-right, bottom-left, bottom-right of the rectangle as it started.
            var quad = QuadPointsOf(turned);
            Assert.NotNull(quad);
            Assert.Equal(8, quad.Length);

            var expected = new[] { (rect.X1, rect.Y2), (rect.X2, rect.Y2), (rect.X1, rect.Y1), (rect.X2, rect.Y1) }.Select(rotate).ToArray();
            for (var i = 0; i < 4; i++)
            {
                Assert.Equal(expected[i].Item1, quad[i * 2], Tolerance);
                Assert.Equal(expected[i].Item2, quad[i * 2 + 1], Tolerance);
            }
        }

        [Fact]
        public async Task Link_UnderASkew_CarriesTheQuadrilateral()
        {
            var turned = OnlyLink(await Generate(Block("transform:skewX(30deg)")));

            Assert.Equal(8, QuadPointsOf(turned)?.Length);
        }

        [Fact]
        public async Task Link_InsideTwoTransformedAncestors_ComposesThem()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var nested = OnlyLink(await Generate(
                "<div style='position:absolute;left:0;top:0;transform:translate(50pt,0)'>" +
                "<div style='position:relative;left:100pt;top:100pt;width:100pt;height:40pt;transform:rotate(90deg)'>" +
                "<a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div>"));

            var rect = RectOf(plain);
            var turned = BoundsAfter(rect, RotateClockwiseAboutCentre(rect, 90));
            AssertRect((turned.X1 + 50, turned.Y1, turned.X2 + 50, turned.Y2), nested);
        }

        [Fact]
        public async Task Link_OnTheTransformedElementItself_Moves()
        {
            var plain = OnlyLink(await Generate(
                "<a href='http://example.test/' style='display:block;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>link</a>"));
            var moved = OnlyLink(await Generate(
                "<a href='http://example.test/' style='display:block;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt;transform:translate(0,25pt)'>link</a>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1, y1 - 25, x2, y2 - 25), moved);
        }

        [Fact]
        public async Task Link_ToAnAnchor_MovesToo()
        {
            var plain = OnlyLink(await Generate(Block("", "#target") + "<p id='target' style='position:absolute;top:400pt'>here</p>"));
            var moved = OnlyLink(await Generate(Block("transform:translate(30pt,20pt)", "#target") + "<p id='target' style='position:absolute;top:400pt'>here</p>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        [Fact]
        public async Task Link_OnALaterPage_MovesOnThatPage()
        {
            static string OnPageTwo(string style) =>
                "<div style='height:1000pt;background:#eee'></div>" +
                $"<div style='width:100pt;height:40pt;{style}'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div><p>after</p>";

            var plain = OnlyLink(await Generate(OnPageTwo("")), page: 1);
            var moved = OnlyLink(await Generate(OnPageTwo("transform:translate(30pt,20pt)")), page: 1);

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        [Fact]
        public async Task Link_MovedOffThePage_HasNoAnnotation()
        {
            var doc = await Generate(Block("transform:translate(0,5000pt)"));

            Assert.Empty(doc.Pages[0].Annotations);
        }

        [Fact]
        public async Task Link_UnderNoTransform_IsLeftWhereLayoutPutIt()
        {
            // The control: the annotation pass must not touch a link nothing moved.
            var doc = await Generate(Block(""));

            Assert.Null(QuadPointsOf(OnlyLink(doc)));
        }

        [Fact]
        public async Task Link_OnDisplayContents_FollowsItsChildrenUnderATransform()
        {
            const string link = "<a href='http://example.test/' style='display:contents'><span style='display:block;height:40pt'>link</span></a>";
            var plain = OnlyLink(await Generate($"<div style='position:absolute;left:100pt;top:100pt;width:100pt'>{link}</div>"));
            var moved = OnlyLink(await Generate($"<div style='position:absolute;left:100pt;top:100pt;width:100pt;transform:translate(30pt,20pt)'>{link}</div>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        [Fact]
        public async Task Link_InsideAnSvg_FollowsTheTransformOfItsBox()
        {
            const string svg = "<svg width='100' height='100'><a href='http://example.test/'><rect width='100' height='100'/></a></svg>";
            var plain = OnlyLink(await Generate($"<div style='position:absolute;left:100pt;top:100pt'>{svg}</div>"));
            var moved = OnlyLink(await Generate($"<div style='position:absolute;left:100pt;top:100pt;transform:translate(30pt,20pt)'>{svg}</div>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        // --- Perspective and 3D ---

        [Fact]
        public async Task Link_UnderPerspective_IsTheProjectedTrapezoid()
        {
            // rotateY(45deg) of a 100pt x 40pt box about its centre, seen through perspective(300pt): a point (x, y) from the centre lands at
            // (x cos t, y) / w with w = 1 + x sin t / d - the right edge recedes, the left comes forward.
            var plain = OnlyLink(await Generate(Block("")));
            var tilted = OnlyLink(await Generate(Block("transform:perspective(300pt) rotateY(45deg)")));

            var rect = RectOf(plain);
            var (cx, cy) = ((rect.X1 + rect.X2) / 2, (rect.Y1 + rect.Y2) / 2);
            (double X, double Y) Project((double X, double Y) p)
            {
                var (x, y) = (p.X - cx, p.Y - cy);
                var t = Math.PI / 4;
                var w = 1 + x * Math.Sin(t) / 300;
                return (cx + x * Math.Cos(t) / w, cy + y / w);
            }

            AssertRect(BoundsAfter(rect, Project), tilted);

            // Not a rectangle any more, so the shape is carried as well as its box.
            Assert.Equal(8, QuadPointsOf(tilted)?.Length);
        }

        [Fact]
        public async Task Link_OnAPlaneBroughtNearer_IsScaledByThePerspective()
        {
            // A plane parallel to the view, translateZ(100pt) under perspective(300pt): everything about the perspective origin (the parent's
            // centre, which is also the child's) is 1 / (1 - 100/300) = 1.5 times larger. That is affine, so it stays on the vector path.
            var plain = OnlyLink(await Generate(Block("")));
            var nearer = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<div style='width:100pt;height:40pt;transform:translateZ(100pt)'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            var (w, h) = (x2 - x1, y2 - y1);
            AssertRect((x1 - w / 4, y1 - h / 4, x2 + w / 4, y2 + h / 4), nearer);
            Assert.Null(QuadPointsOf(nearer));
        }

        [Fact]
        public async Task Link_InAThreeDimensionalContext_LandsWhereThePlaneDoesWithoutOne()
        {
            // The same element, with and without a preserve-3d parent that makes the context compose it plane by plane.
            var direct = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<div style='width:100pt;height:40pt;transform:rotateY(45deg)'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div>"));
            var inContext = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<div style='width:100pt;height:40pt;transform-style:preserve-3d'>" +
                "<div style='width:100pt;height:40pt;transform:rotateY(45deg)'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div></div>"));

            AssertRect(RectOf(direct), inContext);
            Assert.Equal(8, QuadPointsOf(inContext)?.Length);
        }

        [Fact]
        public async Task Link_ThatIsItselfTheProjectedPlane_LandsWhereTheTransformPutsIt()
        {
            // The same two elements, but with the link as the transformed plane: a projective warp records the plane's own placement,
            // where a link inside the plane records it as a descendant.
            const string tilt = "display:block;width:100pt;height:40pt;transform:rotateY(45deg)";
            var warped = OnlyLink(await Generate(
                $"<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'><a href='http://example.test/' style='{tilt}'>link</a></div>"));
            var composed = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'><div style='width:100pt;height:40pt;transform-style:preserve-3d'>" +
                $"<a href='http://example.test/' style='{tilt}'>link</a></div></div>"));
            var inside = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                $"<div style='{tilt}'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div>"));

            AssertRect(RectOf(inside), warped);
            AssertRect(RectOf(inside), composed);
            Assert.Equal(8, QuadPointsOf(warped)?.Length);
            Assert.Equal(8, QuadPointsOf(composed)?.Length);
        }

        [Fact]
        public async Task Link_ThatIsItselfAPlaneBroughtNearer_IsScaled()
        {
            var plain = OnlyLink(await Generate(Block("")));
            var nearer = OnlyLink(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<a href='http://example.test/' style='display:block;width:100pt;height:40pt;transform:translateZ(100pt)'>link</a></div>"));

            var (x1, y1, x2, y2) = RectOf(plain);
            var (w, h) = (x2 - x1, y2 - y1);
            AssertRect((x1 - w / 4, y1 - h / 4, x2 + w / 4, y2 + h / 4), nearer);
        }

        [Fact]
        public async Task Link_ThatIsPlainInlineText_FollowsTheTransformOfItsBlock()
        {
            // The common case: an ordinary inline <a> in a transformed block, whose box has no rectangle of its own beyond its line's.
            const string inline = "<div style='position:absolute;left:100pt;top:100pt;width:100pt;{0}'><a href='http://example.test/'>link text</a></div>";
            var plain = OnlyLink(await Generate(string.Format(inline, "")));
            var moved = OnlyLink(await Generate(string.Format(inline, "transform:translate(30pt,20pt)")));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        [Theory]
        [InlineData("<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'><a href='http://example.test/' style='display:block;height:40pt;transform:translateX(5000pt) rotateY(20deg)'>link</a></div>")]
        [InlineData("<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'><div style='transform-style:preserve-3d'><a href='http://example.test/' style='display:block;height:40pt;transform:translateX(5000pt) rotateY(20deg)'>link</a></div></div>")]
        [InlineData("<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'><div style='transform:translateX(5000pt) rotateY(20deg)'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div>")]
        public async Task Link_ProjectedOffThePage_HasNoAnnotation(string body)
        {
            // Drawn nowhere, so not left clickable at the layout rectangle either.
            var doc = await Generate(body);

            Assert.Empty(doc.Pages[0].Annotations);
        }

        [Fact]
        public void AnnotationPlacement_CornersClippedBehindTheEye_CarryNoQuad()
        {
            // The two top corners (y = 0) have a negative divisor and are cut off, which leaves four points in an order the quad remap
            // would read as a bow tie; the bounding box alone is written.
            var map = new PeachDrawing.Homography(1, 0, 0, 0, 1, 0, 0, 0.01, -0.2);

            Assert.True(AnnotationPlacement.TryMap(map, new PeachDrawing.Core.Rect(0, 0, 100, 100), new PageFrame(0, 0, 0, 0, 1, 800), out var placed));
            Assert.Null(placed.Quad);
        }

        [Fact]
        public async Task Link_QuadPoints_AreNotWrittenUnderA14Header()
        {
            var doc = await new PdfGenerator().GeneratePdf(
                $"<!DOCTYPE html><html><body style='margin:0'>{Block("transform:rotate(30deg)")}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, PdfVersion = PdfVersion.Pdf17 });

            // /QuadPoints is a PDF 1.6 key: the box alone carries the link in a file with the historical 1.4 header.
            Assert.Null(QuadPointsOf(OnlyLink(doc)));
        }

        // --- Running elements ---

        [Fact]
        public async Task Link_InARunningElement_FollowsItsTransform()
        {
            static Task<PeachPdfDocument> Running(string linkStyle) => new PdfGenerator().GeneratePdf(
                "<!DOCTYPE html><html><head><style>" +
                "@page { size: 400pt 300pt; margin: 60pt; @top-left { content: element(head); } }" +
                $".head {{ position: running(head); }} .head a {{ display:block; width:80pt; height:20pt; {linkStyle} }}" +
                "</style></head><body><div class='head'><a href='http://example.test/'>link</a></div><p>body</p></body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4 });

            var plain = OnlyLink(await Running(""));
            var moved = OnlyLink(await Running("transform:translate(30pt,20pt)"));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);
        }

        [Theory]
        [InlineData("transform:translate(0,5000pt)", false)]
        [InlineData("transform:rotate(30deg)", true)]
        public async Task Link_InARunningElement_FollowsTheShapeOrLeavesThePage(string linkStyle, bool expectsQuad)
        {
            var doc = await new PdfGenerator().GeneratePdf(
                "<!DOCTYPE html><html><head><style>" +
                "@page { size: 400pt 300pt; margin: 60pt; @top-left { content: element(head); } }" +
                $".head {{ position: running(head); }} .head a {{ display:block; width:80pt; height:20pt; {linkStyle} }}" +
                "</style></head><body><div class='head'><a href='http://example.test/'>link</a></div><p>body</p></body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4 });

            if (!expectsQuad)
            {
                Assert.Empty(doc.Pages[0].Annotations);
                return;
            }

            Assert.Equal(8, QuadPointsOf(OnlyLink(doc))?.Length);
        }

        [Fact]
        public async Task Link_InAThreeDimensionalContextDrawnOffThePage_HasNoAnnotation()
        {
            // The whole context is outside the clip, so nothing of any plane is drawn: its links go with it.
            var doc = await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<div style='transform-style:preserve-3d;width:100pt;height:40pt;transform:translateX(5000pt) rotateY(20deg)'>" +
                "<div style='width:100pt;height:40pt;transform:rotateY(10deg)'><a href='http://example.test/' style='display:block;height:40pt'>link</a></div></div></div>");

            Assert.Empty(doc.Pages[0].Annotations);
        }

        // --- Form widgets ---

        private static PdfAcroField OnlyField(PeachPdfDocument doc)
        {
            var fields = new List<PdfAcroField>();
            foreach (var item in doc.PdfDocument.Catalog.AcroForm.Fields)
            {
                if ((item is PdfReference reference ? reference.Value : item) is PdfAcroField field)
                    fields.Add(field);
            }

            return Assert.Single(fields);
        }

        private static PdfFormXObject NormalAppearance(PdfAcroField field)
        {
            var ap = field.Elements.GetDictionary(PdfAnnotation.Keys.AP);
            return Assert.IsType<PdfFormXObject>(Assert.IsType<PdfReference>(ap.Elements["/N"]).Value);
        }

        /// <summary>The appearance's <c>/Matrix</c>, read back from the literal it is written as (<c>[a b c d e f]</c>).</summary>
        private static XMatrix MatrixOf(PdfFormXObject appearance)
        {
            var numbers = appearance.Elements[PdfFormXObject.Keys.Matrix]!.ToString()!
                .Trim('[', ']', ' ')
                .Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => double.Parse(n, System.Globalization.CultureInfo.InvariantCulture))
                .ToArray();

            return new XMatrix(numbers[0], numbers[1], numbers[2], numbers[3], numbers[4], numbers[5]);
        }

        private static string Input(string style) =>
            $"<input type='text' name='a' style='position:absolute;left:100pt;top:100pt;width:100pt;height:40pt;box-sizing:border-box;{style}'>";

        [Fact]
        public async Task Widget_UnderTranslate_MovesWithTheControl_AndKeepsItsAppearance()
        {
            var plain = OnlyField(await Generate(Input(""), forms: true));
            var moved = OnlyField(await Generate(Input("transform:translate(30pt,20pt)"), forms: true));

            var (x1, y1, x2, y2) = RectOf(plain);
            AssertRect((x1 + 30, y1 - 20, x2 + 30, y2 - 20), moved);

            // A translation needs no matrix: the appearance is the same stream it was.
            Assert.False(NormalAppearance(moved).Elements.ContainsKey(PdfFormXObject.Keys.Matrix));
        }

        [Fact]
        public async Task Widget_UnderScale_IsScaledByTheReadersFit()
        {
            var plain = OnlyField(await Generate(Input(""), forms: true));
            var scaled = OnlyField(await Generate(Input("transform:scale(2,1.5)"), forms: true));

            // Where the control lands depends on its pivot, which is the border box's centre and not the widget rectangle's; how big it is does not.
            var (x1, y1, x2, y2) = RectOf(plain);
            var (w, h) = (x2 - x1, y2 - y1);
            var (sx1, sy1, sx2, sy2) = RectOf(scaled);
            Assert.Equal(w * 2, sx2 - sx1, Tolerance);
            Assert.Equal(h * 1.5, sy2 - sy1, Tolerance);

            // The appearance is still drawn at the control's own size, and the reader's BBox-to-Rect fit does the scaling: no /Matrix needed.
            var appearance = NormalAppearance(scaled);
            Assert.False(appearance.Elements.ContainsKey(PdfFormXObject.Keys.Matrix));
            Assert.Equal(w, appearance.Elements.GetRectangle(PdfFormXObject.Keys.BBox).Width, 3);
        }

        [Fact]
        public async Task Widget_Mirrored_NeedsTheMatrixToFlipItsAppearance()
        {
            // A fit cannot flip: only the matrix can.
            var mirrored = OnlyField(await Generate(Input("transform:scaleX(-1)"), forms: true));

            Assert.Equal(-1, MatrixOf(NormalAppearance(mirrored)).M11, 3);
        }

        [Fact]
        public async Task Link_InATrackTheClipHides_LeavesNoGhostAtItsLayoutPosition()
        {
            // A carousel: the track is translated 500pt left inside an overflow:hidden box. The first slide's link is clipped away, so it is
            // not drawn - and must not stay clickable at its layout position, over whatever is showing there.
            var doc = await Generate(
                "<div style='position:absolute;left:100pt;top:100pt;width:100pt;height:40pt;overflow:hidden'>" +
                "<div style='position:relative;width:1000pt;height:40pt;transform:translateX(-500pt)'>" +
                "<a href='http://example.test/first' style='position:absolute;left:0;top:0;width:100pt;height:40pt;display:block'>first</a>" +
                "<a href='http://example.test/second' style='position:absolute;left:500pt;top:0;width:100pt;height:40pt;display:block'>second</a>" +
                "</div></div>");

            Assert.DoesNotContain(doc.Pages[0].Annotations.Cast<PdfAnnotation>(), a => a.Elements["/A"]?.ToString()?.Contains("first") == true);
        }

        [Fact]
        public async Task Widget_UnderARotation_IsTheBoundingBox_AndTurnsItsAppearance()
        {
            var plain = OnlyField(await Generate(Input(""), forms: true));
            var turned = OnlyField(await Generate(Input("transform:rotate(90deg)"), forms: true));

            // A quarter turn swaps the box's width and height.
            var (x1, y1, x2, y2) = RectOf(plain);
            var (tx1, ty1, tx2, ty2) = RectOf(turned);
            Assert.Equal(y2 - y1, tx2 - tx1, Tolerance);
            Assert.Equal(x2 - x1, ty2 - ty1, Tolerance);

            // Clockwise on the page, in y-up coordinates: the appearance's x axis points down the page, its y axis along +x.
            var matrix = MatrixOf(NormalAppearance(turned));
            Assert.Equal(0, matrix.M11, 3);
            Assert.Equal(-1, matrix.M12, 3);
            Assert.Equal(1, matrix.M21, 3);
            Assert.Equal(0, matrix.M22, 3);
        }

        [Fact]
        public async Task Widget_MovedOffThePage_KeepsItsField()
        {
            // The control holds a value and belongs to its form wherever it is drawn: only a link, which is just a click target, is dropped.
            var plain = OnlyField(await Generate(Input(""), forms: true));
            var moved = OnlyField(await Generate(Input("transform:translate(0,5000pt)"), forms: true));

            Assert.Equal(RectOf(plain).Y1 - 5000, RectOf(moved).Y1, Tolerance);
        }

        [Fact]
        public async Task Widget_ProjectedOffThePage_KeepsItsFieldAsAnEmptyRectangle()
        {
            // Drawn nowhere, so nothing to see or click - but the field still holds its value and belongs to its form.
            var field = OnlyField(await Generate(
                "<div style='perspective:300pt;position:absolute;left:100pt;top:100pt;width:100pt;height:40pt'>" +
                "<input type='text' name='a' style='display:block;width:100pt;height:40pt;transform:translateX(5000pt) rotateY(20deg)'></div>", forms: true));

            var (x1, y1, x2, y2) = RectOf(field);
            Assert.Equal(0, x2 - x1, 3);
            Assert.Equal(0, y2 - y1, 3);
        }

        [Fact]
        public async Task Checkbox_UnderARotation_TurnsBothOfItsStates()
        {
            var doc = await Generate(
                "<input type='checkbox' name='c' checked style='position:absolute;left:100pt;top:100pt;width:20pt;height:20pt;transform:rotate(90deg)'>", forms: true);

            var field = OnlyField(doc);
            var states = field.Elements.GetDictionary(PdfAnnotation.Keys.AP).Elements.GetDictionary("/N");

            // Two appearance streams, on and off, and each is carried onto the box.
            Assert.Equal(2, states.Elements.Count);
            foreach (var key in states.Elements.Keys)
            {
                var appearance = (PdfFormXObject)states.Elements.GetReference(key).Value;
                Assert.True(appearance.Elements.ContainsKey(PdfFormXObject.Keys.Matrix));
            }
        }

        // --- The map the annotation pass applies ---

        [Fact]
        public void AnnotationPlacement_BoxEntirelyBehindTheViewer_IsNotPlaced()
        {
            // w is negative everywhere: nothing of the rectangle is in front of the eye.
            var behind = new PeachDrawing.Homography(1, 0, 0, 0, 1, 0, 0, 0, -1);
            var frame = new PageFrame(0, 0, 0, 0, 1, 800);

            Assert.False(AnnotationPlacement.TryMap(behind, new PeachDrawing.Core.Rect(0, 0, 10, 10), frame, out _));
        }

        [Fact]
        public void AnnotationPlacement_Identity_IsTheLayoutRectangle_AndNeedsNoQuad()
        {
            var frame = new PageFrame(10, 20, 0, 0, 1, 800);

            Assert.True(AnnotationPlacement.TryMap(PeachDrawing.Homography.Identity, new PeachDrawing.Core.Rect(5, 6, 30, 40), frame, out var placed));

            Assert.Equal(15, placed.Bounds.X, 6);
            Assert.Equal(800 - 20 - 6 - 40, placed.Bounds.Y, 6);
            Assert.Equal(30, placed.Bounds.Width, 6);
            Assert.Equal(40, placed.Bounds.Height, 6);
            Assert.Null(placed.Quad);
            Assert.True(placed.AppearanceMatrix is { } matrix && matrix.M11 == 1 && matrix.M22 == 1);
        }

        [Fact]
        public void AnnotationPlacement_Perspective_HasNoAppearanceMatrix()
        {
            // A map with a divisor is not affine, so no matrix can carry an appearance onto it.
            var persp = new PeachDrawing.Homography(1, 0, 0, 0, 1, 0, 0.002, 0, 1);

            Assert.True(AnnotationPlacement.TryMap(persp, new PeachDrawing.Core.Rect(0, 0, 100, 40), new PageFrame(0, 0, 0, 0, 1, 800), out var placed));
            Assert.Null(placed.AppearanceMatrix);
            Assert.NotNull(placed.Quad);
        }
    }
}
