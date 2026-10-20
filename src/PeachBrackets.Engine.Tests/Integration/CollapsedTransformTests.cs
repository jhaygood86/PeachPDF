using PeachPDF;
using PeachPDF.Rendering;
using System.Numerics;
using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Utils;
using PeachPDF.Tests.TestSupport;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// css-transforms-1 §"Transform Rendering": an element whose transformation matrix is not invertible has no visible area and is not
    /// rendered. The painter already skipped an element whose 2D matrix collapses; these cover what else "not rendered" has to take with it
    /// - the link annotations and form widgets over it, the 3D rendering context rooted at it, and the elements whose 4x4 is singular
    /// while their 2D projection is not - and the edge-on plane that perspective still turned into a hairline.
    /// </summary>
    public class CollapsedTransformTests
    {
        // --- Links and widgets over something that is not drawn ---

        private static Task<PeachPdfDocument> Generate(string body, bool forms = false) =>
            new PdfGenerator().GeneratePdf(
                $"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = forms });

        [Theory]
        [InlineData("scale(0)")]
        [InlineData("matrix(0,0,0,0,0,0)")]
        [InlineData("scaleX(0)")]
        [InlineData("rotateX(90deg)")]
        [InlineData("scaleZ(0)")]
        public async Task Link_InsideAnElementWithNoVisibleArea_HasNoAnnotation(string transform)
        {
            var doc = await Generate($"<div style='transform:{transform};width:100pt;height:40pt'><a href='http://example.test/'>link</a></div>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_OnTheCollapsedElementItself_HasNoAnnotation()
        {
            var doc = await Generate("<div style='transform:scale(0);display:block'><a href='http://example.test/' style='display:block'>link</a></div><a href='http://example.test/' style='display:block;transform:scale(0)'>itself</a>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_InsideAnElementThatOnlyShrinks_KeepsItsAnnotation()
        {
            // The control: the walk must not drop a link under a transform that leaves a visible area.
            var doc = await Generate("<div style='transform:scale(0.5);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div>");

            Assert.Single(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_BesideACollapsedElement_KeepsItsAnnotation()
        {
            var doc = await Generate("<div style='transform:scale(0)'><a href='http://example.test/a'>gone</a></div><p><a href='http://example.test/b'>kept</a></p>");

            Assert.Single(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_OnADisplayContentsElementInsideACollapsedOne_HasNoAnnotation()
        {
            var doc = await Generate("<div style='transform:scale(0)'><a href='http://example.test/' style='display:contents'><span style='display:block'>link</span></a></div>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_InsideACollapsedSvg_HasNoAnnotation()
        {
            var doc = await Generate("<div style='transform:scale(0)'><svg width='100' height='100'><a href='http://example.test/'><rect width='50' height='50'/></a></svg></div>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task GetLinks_WithoutBookmarks_SkipsACollapsedElementsLinks()
        {
            // The bookmark-agnostic walk behind the public HtmlContainer.GetLinks().
            var (_, container) = await Layout(
                "<div style='transform:scale(0)'><a href='http://example.test/a'>gone</a></div><p><a href='http://example.test/b'>kept</a></p>");

            var link = Assert.Single(container.GetLinks(null));

            Assert.Equal("http://example.test/b", link.Href);
        }

        [Theory]
        // Off the eye's axis, an edge-on plane under a parent's perspective is still seen: a trapezoid, not a line.
        [InlineData("<div style='perspective:300pt;perspective-origin:0 0;width:200pt'><div style='transform:rotateY(90deg);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div></div>")]
        [InlineData("<div style='perspective:300pt;perspective-origin:0 0;width:200pt'><div style='transform:rotateX(90deg);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div></div>")]
        // The same through the element's own perspective().
        [InlineData("<div style='transform:perspective(300pt) translateX(80pt) rotateY(90deg);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div>")]
        // A plane in a tilted 3D context: the child is edge-on as a flat transform, standing up in the context.
        [InlineData("<div style='transform-style:preserve-3d;transform:rotateX(-60deg);width:100pt;height:40pt'><div style='transform:rotateX(90deg);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div></div>")]
        // A box that is thin but overflows with thick content: the painter measures what the subtree paints, not the box.
        [InlineData("<div style='transform:scaleY(0.0005);height:1pt'><div style='height:100pt'><a href='http://example.test/'>link</a></div></div>")]
        public async Task Link_InAPlaneThePainterDraws_KeepsItsAnnotation(string body)
        {
            var doc = await Generate(body);

            Assert.Single(doc.PageAnnotations(0));
        }

        [Theory]
        // Level with the eye, an edge-on plane under perspective projects to a line: nothing is drawn, so nothing is clickable.
        [InlineData("<div style='perspective:200pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:rotateY(90deg);width:100pt;height:100pt'><a href='http://example.test/'>link</a></div></div>")]
        [InlineData("<div style='perspective:200pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:rotateX(90deg);width:100pt;height:100pt'><a href='http://example.test/'>link</a></div></div>")]
        // The same for a plane of a 3D rendering context.
        [InlineData("<div style='perspective:300pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'><div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;transform:rotateY(90deg)'><a href='http://example.test/'>link</a></div></div></div>")]
        // A zero-height wrapper whose overflowing content is squashed to nothing: the box says nothing, the subtree does.
        [InlineData("<div style='transform:scaleY(0.000001);height:0'><div style='height:40pt'><a href='http://example.test/'>link</a></div></div>")]
        public async Task Link_InAPlaneThePainterCulled_HasNoAnnotation(string body)
        {
            var doc = await Generate(body);

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Theory]
        // Turned away with backface-visibility: hidden.
        [InlineData("<div style='margin:20pt 0 0 50pt;width:100pt;height:100pt;transform:rotateY(180deg);backface-visibility:hidden'><a href='http://example.test/'>link</a></div>")]
        // Brought past the eye: behind the viewer, so not there at all.
        [InlineData("<div style='perspective:100pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:translateZ(200pt);width:100pt;height:100pt;background:#c33'><a href='http://example.test/'>link</a></div></div>")]
        // Wholly behind the eye through the element's own perspective(), and not a plane parallel to the view (so no uniform divisor to see it by).
        [InlineData("<div style='margin:20pt 0 0 50pt;width:100pt;height:100pt;transform:perspective(100pt) translateZ(150pt) rotateY(30deg)'><a href='http://example.test/'>link</a></div>")]
        // The same two as planes of a 3D rendering context.
        [InlineData("<div style='perspective:300pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'><div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#c33;transform:rotateY(180deg);backface-visibility:hidden'><a href='http://example.test/'>link</a></div></div></div>")]
        [InlineData("<div style='perspective:100pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'><div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#c33;transform:translateZ(200pt)'><a href='http://example.test/'>link</a></div></div></div>")]
        public async Task Link_InAPlaneThatIsNotThereToBeSeen_HasNoAnnotation(string body)
        {
            var doc = await Generate(body);

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_InAPlaneWhoseChildPlanesAreAllCulledToo_HasNoAnnotation()
        {
            // The edge-on plane has a child plane of its own, but that one is edge-on as well: nothing in the subtree is drawn, so the link in the
            // parent's own content goes too (the child plane being merely present must not keep it).
            var doc = await Generate(
                "<div style='perspective:300pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'>" +
                "<div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#c33;transform-style:preserve-3d;transform:rotateY(90deg)'>" +
                "<a href='http://example.test/a'>parent link</a>" +
                "<div style='width:100pt;height:100pt;background:#33c;backface-visibility:hidden'><a href='http://example.test/b'>child link</a></div></div></div></div>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_InAPlaneThatFacesTheViewerAgain_KeepsItsAnnotation()
        {
            // The edge-on plane is not drawn, but the plane hanging off it turns back to face the viewer: composed on its own, so its link is live.
            var doc = await Generate(
                "<div style='perspective:300pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'>" +
                "<div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#c33;transform-style:preserve-3d;transform:rotateY(90deg)'>" +
                "<div style='width:100pt;height:100pt;background:#33c;transform:rotateY(-90deg)'><a href='http://example.test/'>link</a></div></div></div></div>");

            Assert.Single(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Link_InAPlaneWhoseFourByFourIsSingularUnderPerspective_HasNoAnnotation()
        {
            var doc = await Generate("<div style='perspective:300pt'><div style='transform:scaleZ(0);width:100pt;height:40pt'><a href='http://example.test/'>link</a></div></div>");

            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task Bookmark_OfACollapsedHeading_IsKept()
        {
            // A bookmark names a place in the document, not something painted: the heading is still there in the layout.
            var doc = await Generate("<h1 style='transform:scale(0)'><a href='http://example.test/'>Chapter</a></h1><p>text</p>");

            Assert.Single(doc.Inspect().Outlines);
            Assert.Empty(doc.PageAnnotations(0));
        }

        [Fact]
        public async Task FormWidget_InsideACollapsedElement_IsNotEmitted()
        {
            var collapsed = await Generate("<div style='transform:scale(0)'><input type='text' name='a'></div><input type='text' name='b'>", forms: true);
            var shrunk = await Generate("<div style='transform:scale(0.5)'><input type='text' name='a'></div><input type='text' name='b'>", forms: true);

            Assert.Equal(1, FieldCount(collapsed));
            Assert.Equal(2, FieldCount(shrunk));
        }

        private static int FieldCount(PeachPdfDocument doc) => doc.Inspect().Fields.Count;

        // --- The full 4x4 decides ---

        public static TheoryData<string, bool> FourByFourTransforms => new()
        {
            { "scaleZ(0)", true },
            { "scale3d(1,1,0)", true },
            { "matrix3d(1,0,0,0, 0,1,0,0, 0,0,0,0, 0,0,0,1)", true },       // a zero z-row
            { "scale(0)", true },
            { "rotateX(90deg)", false },                                     // invertible in 4x4: edge-on is for the painter to find
            { "scaleZ(0.5)", false },
            { "translateZ(50pt)", false },
            { "rotateX(30deg) translateZ(10pt)", false },
            { "rotate3d(1,1,0,40deg)", false },
            { "scale(0.5)", false },
        };

        [Theory]
        [MemberData(nameof(FourByFourTransforms))]
        public async Task LeavesNoVisibleArea_BeforePainting_AnswersForTheWholeTransformationMatrix(string transform, bool expected)
        {
            var (root, _) = await Layout($"<div id='t' style='transform:{transform};width:100pt;height:40pt'>x</div>");

            Assert.Equal(expected, DomUtils.LeavesNoVisibleArea(LayoutHarness.FindById(root, "t")!));
        }

        [Fact]
        public async Task LeavesNoVisibleArea_NoTransform_IsFalse()
        {
            var (root, _) = await Layout("<div id='t' style='width:100pt;height:40pt'>x</div>");

            Assert.False(DomUtils.LeavesNoVisibleArea(LayoutHarness.FindById(root, "t")!));
        }

        public static TheoryData<Matrix4x4, bool> Matrices4 => new()
        {
            { Matrix4x4.Identity, true },
            { Matrix4x4.CreateRotationX(0.5f), true },
            { Matrix4x4.CreateRotationX(MathF.PI / 2), true },               // a rotation: edge-on, but invertible
            { Matrix4x4.CreateScale(1, 1, 0), false },
            { Matrix4x4.CreateScale(0.001f), true },
            { Matrix4x4.CreateScale(1e-5f), true },                          // determinant 1e-15: tiny, not singular (the 2D checks judge its area)
            { Matrix4x4.CreateScale(1e-7f), false },                         // determinant 1e-21
            { Matrix4x4.CreateTranslation(1e6f, -1e6f, 5), true },
            { default, false },                                              // all zero
            { new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 0), false },
            { new Matrix4x4(1, 2, 0, 0, 2, 4, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1), false },          // rank deficient in x/y
            { new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, float.NaN, 0, 0, 1), false },
            { new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, float.PositiveInfinity, 0, 1), false },
            { new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, 0, 0, 0, float.NaN, 1), false },
            { new Matrix4x4(1, 0, 0, 0, 0, 1, 0, 0, 0, 0, 1, -0.01f, 0, 0, 0, 1), true },       // a perspective
        };

        [Theory]
        [MemberData(nameof(Matrices4))]
        public void IsInvertible4_DecidesOnTheDeterminantAndFiniteness(Matrix4x4 matrix, bool expected) =>
            Assert.Equal(expected, FragmentPainter.IsInvertible4(matrix));

        [Fact]
        public void IsInvertible4_AgreesWithTheFrameworkDeterminantForOrdinaryMatrices()
        {
            // Guards the hand-written cofactor expansion against a transposed term: a skewed, scaled, rotated, perspective matrix.
            var m = Matrix4x4.CreateScale(2, 3, 4) * Matrix4x4.CreateRotationX(0.7f) * Matrix4x4.CreateRotationY(-0.3f) *
                    Matrix4x4.CreateTranslation(5, -6, 7);
            m.M14 = 0.002f;
            m.M24 = -0.001f;
            m.M34 = 0.003f;
            Assert.True(FragmentPainter.IsInvertible4(m));

            // Make one row a combination of two others: singular whatever the entries.
            var singular = new Matrix4x4(
                1, 2, 3, 4,
                5, 6, 7, 8,
                6, 8, 10, 12,
                0.5f, -1, 2, 3);
            Assert.Equal(0f, singular.GetDeterminant(), 1e-3);
            Assert.False(FragmentPainter.IsInvertible4(singular));
        }

        // --- Painting ---

        private static Task<(CssBox Root, HtmlContainerInt Container)> Layout(string body) =>
            LayoutHarness.LayoutAsync(LayoutHarness.Wrap(body), margin: 0);

        [Fact]
        public async Task Paint_SingularFourByFour_PaintsNothingForTheBoxOrItsSubtree()
        {
            var (root, container) = await Layout("<div id='t' style='transform:scaleZ(0);width:50pt;height:50pt;background:#c33'><p style='background:#33c'>x</p></div>");

            var canvas = new RasterCanvas(new HtmlRenderContext(), new RasterSurface(120, 120, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "t")!, canvas);

            Assert.Equal(0, Covered(canvas));
        }

        [Fact]
        public async Task Paint_AFourByFourThatOnlyMovesInZ_StillPaints()
        {
            var (root, container) = await Layout("<div id='t' style='transform:translateZ(5pt);width:50pt;height:50pt;background:#c33'></div>");

            var canvas = new RasterCanvas(new HtmlRenderContext(), new RasterSurface(120, 120, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintBox(container, LayoutHarness.FindById(root, "t")!, canvas);

            Assert.True(Covered(canvas) > 0);
        }

        private const string CollapsedContext =
            "<div style='perspective:300px'><div style='transform-style:preserve-3d;transform:{0};width:100px;height:100px'>" +
            "<div style='position:absolute;width:50px;height:50px;background:#0a0;transform:translateZ(5px)'></div></div></div>";

        [Theory]
        [InlineData("scale(0)")]
        [InlineData("scaleZ(0)")]
        public async Task Paint_CollapsedPreserve3dRoot_NeverReachesTheCompositor(string transform)
        {
            var (_, container) = await Layout(string.Format(CollapsedContext, transform));

            var canvas = new RasterCanvas(new HtmlRenderContext(), new RasterSurface(220, 180, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, canvas);

            Assert.Equal(0, Covered(canvas));
        }

        [Fact]
        public async Task GeneratePdf_CollapsedPreserve3dRoot_NeedsNoBitmapUnderPdfA1b()
        {
            // A visible context that needs a depth test (a plane tilted in perspective) is composed as a bitmap, which PDF/A-1b rejects; a collapsed one has no
            // picture, so nothing is rejected.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, PdfAConformance = PeachPDF.PdfAConformance.PdfA1B, Metadata = new PdfDocumentMetadata { CreationDate = DateTimeOffset.UtcNow } };

            var doc = await new PdfGenerator().GeneratePdf($"<!DOCTYPE html><html><body>{string.Format(CollapsedContext, "scale(0)")}<p>after</p></body></html>", config);
            Assert.Equal(1, doc.PageCount);

            var visible = CollapsedContext.Replace("transform:translateZ(5px)", "transform:rotateY(30deg)");
            await Assert.ThrowsAnyAsync<InvalidOperationException>(async () =>
                await new PdfGenerator().GeneratePdf($"<!DOCTYPE html><html><body>{string.Format(visible, "scale(0.5)")}<p>after</p></body></html>", config));
        }

        [Fact]
        public async Task Paint_CollapsedMemberOfA3dContext_IsNotDrawnAndLeavesTheRestAlone()
        {
            const string html =
                "<div style='position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'>" +
                "<div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#ff0000;transform:translateZ(30pt)'></div>" +
                "<div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#0000ff;transform:translateZ(60pt) scaleZ(0)'></div>" +
                "</div></div>";
            var (_, container) = await Layout(html);

            var canvas = new RasterCanvas(new HtmlRenderContext(), new RasterSurface(220, 180, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, canvas);

            // The blue plane is the nearer one (z = 60pt against 30pt) but its matrix is singular: only the red plane is there.
            Assert.Equal(new byte[] { 255, 0, 0, 255 }, canvas.Surface.Row(80).Slice(100 * 4, 4).ToArray());
        }

        // --- An edge-on plane under perspective ---

        [Fact]
        public async Task Paint_PlaneTiltedUnderParentPerspective_IsStillDrawn()
        {
            // The control for the sliver cull: a plane turned 60 degrees keeps a visible image.
            var (_, container) = await Layout(
                "<div style='perspective:200pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:rotateY(60deg);width:100pt;height:100pt;background:#c33'></div></div>");

            var canvas = new RasterCanvas(new HtmlRenderContext(), new RasterSurface(220, 180, 0, 0, 1, 1), 1);
            FragmentPaintHarness.PaintPage(container, canvas);

            Assert.True(Covered(canvas) > 1000);
        }

        private static readonly System.Text.RegularExpressions.Regex ImageXObject = new(@"/Subtype\s*/Image");

        private static readonly System.Text.RegularExpressions.Regex TextRenderModeInvisible = new(@"\b3 Tr");

        private static async Task<string> ContentOf(string body)
        {
            var doc = await new PdfGenerator().GeneratePdf($"<!DOCTYPE html><html><body style='margin:0'>{body}</body></html>",
                new PdfGenerateConfig { PageSize = PageSize.A4, CompressContentStreams = false });
            using var stream = new MemoryStream();
            doc.Save(stream);
            return System.Text.Encoding.Latin1.GetString(stream.ToArray());
        }

        // What the hairline was made of in the PDF: a bitmap of the plane a fraction of a point wide, and the plane's text supplied again
        // (3 Tr, invisible) under the near-singular linearisation of its transform.

        [Theory]
        [InlineData("rotateY(90deg)")]
        [InlineData("rotateX(90deg)")]
        public async Task GeneratePdf_EdgeOnPlaneUnderParentPerspective_EmitsNeitherABitmapNorATextLayer(string transform)
        {
            var edgeOn = await ContentOf($"<div style='perspective:200pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:{transform};width:100pt;height:100pt;background:#c33'>hidden words</div></div>");
            var tilted = await ContentOf("<div style='perspective:200pt;margin:20pt 0 0 50pt;width:100pt;height:100pt'><div style='transform:rotateY(60deg);width:100pt;height:100pt;background:#c33'>hidden words</div></div>");

            Assert.DoesNotMatch(ImageXObject, edgeOn);
            Assert.DoesNotMatch(TextRenderModeInvisible, edgeOn);

            // The control: a plane that keeps an image has both.
            Assert.Matches(ImageXObject, tilted);
            Assert.Matches(TextRenderModeInvisible, tilted);
        }

        [Fact]
        public async Task GeneratePdf_EdgeOnPlaneInA3dContext_EmitsNothingForIt()
        {
            string Context(string plane) =>
                "<div style='perspective:300pt;position:relative;width:200pt;height:160pt'><div style='position:absolute;left:0;top:0;width:200pt;height:160pt;transform-style:preserve-3d'>" +
                $"<div style='position:absolute;left:50pt;top:30pt;width:100pt;height:100pt;background:#ff0000;transform:{plane}'>words</div></div></div>";

            var edgeOn = await ContentOf(Context("rotateY(90deg)"));
            var tilted = await ContentOf(Context("rotateY(30deg)"));

            // The plane under test is drawn as a bitmap only when it keeps an image: an edge-on one is dropped (the root plane alone has no depth to resolve, so it stays vector).
            Assert.Empty(ImageXObject.Matches(edgeOn));
            Assert.NotEmpty(ImageXObject.Matches(tilted));
            Assert.DoesNotMatch(TextRenderModeInvisible, edgeOn);
            Assert.Matches(TextRenderModeInvisible, tilted);
        }

        [Theory]
        [InlineData(1, 1, false)]            // the box as it is
        [InlineData(0, 0, true)]             // every corner on one point: a speck
        [InlineData(1, 0.0002, false)]       // 100 x 0.02: thin, but over the 0.0128 cut
        [InlineData(1, 0.0001, true)]        // 100 x 0.01: a hairline
        [InlineData(0.0001, 1, true)]        // the same, the other way round
        public void ProjectsToSliver_MeasuresTheProjectedImage(double scaleX, double scaleY, bool expected)
        {
            var map = new Homography(scaleX, 0, 0, 0, scaleY, 0, 0, 0, 1);

            Assert.Equal(expected, FragmentPainter.ProjectsToSliver(map, new Rect(0, 0, 100, 100)));
        }

        [Fact]
        public void ProjectsToSliver_ALineOrAnImageBehindTheEye_IsHandledWithoutThrowing()
        {
            // Collapsed onto the x axis: area zero, length 100.
            Assert.True(FragmentPainter.ProjectsToSliver(new Homography(1, 0, 0, 0, 0, 0, 0, 0, 1), new Rect(0, 0, 100, 100)));

            // Wholly behind the eye (w < 0 everywhere): no polygon, so not judged a sliver - the caller drops it on its own.
            Assert.False(FragmentPainter.ProjectsToSliver(new Homography(1, 0, 0, 0, 1, 0, 0, 0, -1), new Rect(0, 0, 100, 100)));
        }

        /// <summary>The pixels with any ink.</summary>
        private static int Covered(RasterCanvas canvas)
        {
            var n = 0;
            for (var y = 0; y < canvas.Surface.Height; y++)
            {
                var row = canvas.Surface.Row(y);
                for (var x = 0; x < canvas.Surface.Width; x++)
                    if (row[x * 4 + 3] != 0)
                        n++;
            }

            return n;
        }
    }
}
