using System.Text;
using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>
    /// The interpreter's less common paths: curves and the closing/stroking paint operators, ExtGState entries, dashes, malformed operators, inline
    /// images, form recursion, radial shadings, and the ways an image can be refused. Each paints a small page and reads pixels back.
    /// </summary>
    public class PdfInterpreterBehaviorTests
    {
        private sealed record Page(byte[] Pdf)
        {
            public (byte R, byte G, byte B, byte A) At(int x, int y, out IReadOnlyCollection<string> unsupported)
            {
                var doc = PdfSourceDocument.Open(Pdf);
                using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
                unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
                var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
                var i = (y * 100 + x) * 4;
                return (span[i], span[i + 1], span[i + 2], span[i + 3]);
            }

            public IReadOnlyCollection<string> Unsupported
            {
                get { At(0, 0, out var u); return u; }
            }
        }

        private static Page Draw(string content, string resources = "", params string[] extra) =>
            new(TestPdf.Build(content, resources, extraObjects: extra));

        private static void Red(Page page, int x, int y)
        {
            var p = page.At(x, y, out _);
            Assert.True(p.R > 200 && p.G < 60 && p.B < 60 && p.A > 200, $"expected red at ({x},{y}), was {p}");
        }

        private static void Empty(Page page, int x, int y) => Assert.True(page.At(x, y, out _).A < 20, $"expected nothing at ({x},{y})");

        [Fact]
        public void Curves_ClosePaths_AndTheFillStrokeVariants_AllPaint()
        {
            // A fan of operators: each paints a red square-ish region we then probe.
            Red(Draw("1 0 0 rg 10 10 m 10 10 40 10 40 40 c 10 40 l f"), 20, 70);               // c: cubic with a doubled first control point
            Red(Draw("1 0 0 rg 10 10 m 40 10 40 40 v 10 40 l h f"), 20, 70);                   // v: first control point is the current point
            Red(Draw("1 0 0 rg 10 10 m 10 40 40 40 y 40 10 l h f"), 30, 70);                   // y: second control point is the end point
            Red(Draw("1 0 0 rg 0 0 1 RG 5 w 10 10 30 30 re B"), 25, 75);                        // fill and stroke
            Red(Draw("1 0 0 rg 0 0 1 RG 10 10 30 30 re b*"), 25, 75);                           // close, fill (even-odd) and stroke
            Red(Draw("1 0 0 RG 10 w 10 50 m 90 50 l s"), 50, 50);                               // close and stroke
            Red(Draw("1 0 0 rg 10 10 80 80 re 30 30 40 40 re B*"), 15, 50);                     // even-odd fill and stroke
            Red(Draw("1 0 0 rg 0 0 100 100 re W* n 0 0 100 100 re f"), 50, 50);                 // even-odd clip
        }

        [Fact]
        public void ClosingAnEmptyPath_AndPaintingWithNoPath_AreHarmless()
        {
            Empty(Draw("h f S n W n"), 50, 50);
            Red(Draw("1 0 0 rg 50 50 l 90 50 l 90 90 l 50 90 l f 30 30 60 60 re f"), 70, 20);   // a segment with no current point starts one
        }

        [Fact]
        public void Dashes_Caps_Joins_AndMiterLimit_ApplyToStrokes()
        {
            // A dashed 10pt line: the dash is on at the start and off in the first gap.
            var dashed = Draw("1 0 0 RG 10 w [20 20] 0 d 0 50 m 100 50 l S");
            Red(dashed, 10, 50);
            Empty(dashed, 30, 50);
            Red(dashed, 50, 50);

            // A butt cap stops at the end point, a square cap runs past it by half the width.
            Empty(Draw("1 0 0 RG 10 w 0 J 20 50 m 60 50 l S"), 64, 50);
            Red(Draw("1 0 0 RG 10 w 2 J 20 50 m 60 50 l S"), 64, 50);
            Red(Draw("1 0 0 RG 10 w 1 J 20 50 m 60 50 l S"), 64, 50);
            Red(Draw("1 0 0 RG 10 w 1 j 2 M 20 20 m 60 20 l 60 60 l S"), 40, 80);
            Red(Draw("1 0 0 RG 10 w 2 j 20 20 m 60 20 l 60 60 l S"), 40, 80);
            // A zero-width line is still drawn, as a hairline.
            Assert.True(Draw("1 0 0 RG 0 w 0 50 m 100 50 l S").At(50, 50, out _).A > 0);
            // A dash array of zeros is solid; an odd-length array is repeated to make pairs.
            Red(Draw("1 0 0 RG 10 w [0 0] 0 d 0 50 m 100 50 l S"), 50, 50);
            Red(Draw("1 0 0 RG 10 w [30] 0 d 0 50 m 100 50 l S"), 10, 50);
        }

        [Fact]
        public void ExtGState_SetsLineStyleDashAndAlpha_AndReportsWhatItCannotApply()
        {
            const string res = "/ExtGState << /G0 << /LW 10 /LC 2 /LJ 1 /ML 3 /D [[20 20] 0] /CA 0.5 /ca 1 >> /G1 << /BM /Multiply /SMask << /S /Alpha >> /TR /Identity /OP true >> /G2 << /BM /Normal /SMask /None >> >>";
            var styled = Draw("/G0 gs 1 0 0 RG 0 50 m 100 50 l S", res);
            var alpha = styled.At(10, 50, out _);

            Assert.InRange(alpha.A, 110, 145);                          // stroke alpha 0.5
            Assert.True(styled.At(30, 50, out _).A < 20);               // the dash from /D
            Assert.Empty(Draw("/G2 gs", res).Unsupported);              // Normal and no soft mask are no-ops
            Assert.Equal(["gs OP", "gs SMask (no group)", "gs TR"], Draw("/G1 gs", res).Unsupported.Order(StringComparer.Ordinal));
            Assert.Empty(Draw("/Missing gs", res).Unsupported);
        }

        [Fact]
        public void MalformedOperators_AreReported_AndTheStreamContinues()
        {
            var page = Draw("1 2 cm 1 0 0 rg 0 0 50 50 re f");

            Assert.Contains("cm (malformed)", page.Unsupported);
            Red(page, 10, 90);
        }

        [Fact]
        public void InlineImages_AreSkipped_AndReported_AndDrawingContinuesAfterThem()
        {
            var page = Draw("BI /W 1 /H 1 /CS /G /BPC 8 ID \u0001 EI 1 0 0 rg 0 0 100 100 re f");

            Assert.Contains("BI", page.Unsupported);
            Red(page, 50, 50);

            Assert.Contains("BI", Draw("BI /W 1 /H 1 ID \u0001 never terminated").Unsupported);
            Assert.Contains("BI", Draw("BI /W 1 /H 1").Unsupported);
        }

        [Fact]
        public void UnbalancedSaveAndRestore_DoNotLeakOrUnderflow()
        {
            Red(Draw("Q Q q q 1 0 0 rg 0 0 100 100 re f"), 50, 50);
            Empty(Draw("q 0 0 10 10 re W n 0 0 100 100 re 1 0 0 rg f"), 50, 50);
        }

        [Fact]
        public void FormXObjects_ThatRecurse_StopAtTheNestingLimit_AndMissingOnesAreReported()
        {
            var selfReferencing = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Resources << /XObject << /F0 5 0 R >> >>", "/F0 Do");
            var page = Draw("/F0 Do", "/XObject << /F0 5 0 R >>", selfReferencing);

            Assert.Contains("Do (form nesting)", page.Unsupported);
            Assert.Contains("Do (missing XObject)", Draw("/Nope Do").Unsupported);
            Assert.Contains("Do /Foo", Draw("/F0 Do", "/XObject << /F0 5 0 R >>", "<< /Type /XObject /Subtype /Foo >>").Unsupported);
        }

        [Fact]
        public void FormXObject_WithoutItsOwnResources_UsesThePagesOwn()
        {
            var form = TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 100 100]", "/CS0 cs 1 0 0 sc 0 0 100 100 re f");
            var page = Draw("/F0 Do", "/XObject << /F0 5 0 R >> /ColorSpace << /CS0 /DeviceRGB >>", form);

            Red(page, 50, 50);
        }

        [Fact]
        public void ClipText_MarkedContent_AndLegacyOperators_AreAccepted()
        {
            var page = Draw("/Span <</MCID 0>> BDC BX /Tag BMC EMC EX EMC 0 0 d0 /Perceptual ri 1 i 1 0 0 rg 0 0 100 100 re f");

            Assert.Empty(page.Unsupported);
            Red(page, 50, 50);
        }

        [Fact]
        public void UnknownOperators_AreReportedOnce()
        {
            var page = Draw("1 2 3 frobnicate 4 frobnicate");

            Assert.Equal(["frobnicate"], page.Unsupported);
        }

        // ------------------------------------------------------------ radial and awkward shadings

        private static string Radial(string coords, string function = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>", string extra = "") =>
            $"<< /ShadingType 3 /ColorSpace /DeviceRGB /Coords {coords} /Function {function} /Extend [true true] {extra} >>";

        private static Page ShadingPage(string shading) => Draw("/S0 sh", $"/Shading << /S0 {shading} >>");

        [Fact]
        public void RadialShading_RunsFromItsInnerCircleOutward()
        {
            var page = ShadingPage(Radial("[50 50 0 50 50 40]"));

            var centre = page.At(50, 50, out _);
            var edge = page.At(50, 12, out _);
            Assert.True(centre.R > 200 && centre.B < 60, $"red at the centre: {centre}");
            Assert.True(edge.B > 150 && edge.R < 100, $"blue near the rim: {edge}");
        }

        [Fact]
        public void RadialShading_WithAnInnerCircleLargerThanTheOuter_RunsInward_AndAnEmptyOneIsReported()
        {
            var inward = ShadingPage(Radial("[50 50 40 50 50 0]")).At(50, 50, out _);
            Assert.True(inward.B > 200, $"the colours reverse when the circles are swapped: {inward}");

            Assert.Contains("empty radial shading", ShadingPage(Radial("[50 50 0 50 50 0]")).Unsupported);
        }

        [Theory]
        [InlineData("<< /ShadingType 1 >>", "shading type 1")]
        [InlineData("<< /ShadingType 2 /ColorSpace /Nope /Coords [0 0 1 1] >>", "shading colour space")]
        [InlineData("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0 1 1] >>", "shading function")]
        [InlineData("<< /ShadingType 2 /ColorSpace /DeviceRGB /Coords [0 0] /Function << /FunctionType 2 /Domain [0 1] /C0 [0 0 0] /C1 [1 1 1] /N 1 >> >>", "shading coordinates")]
        public void ShadingsThatCannotBeRead_AreReportedWithTheReason(string shading, string expected)
        {
            Assert.Contains(expected, ShadingPage(shading).Unsupported);
        }

        [Fact]
        public void ShadingWithADomain_AndFewerFunctionOutputsThanComponents_StillPaints()
        {
            const string function = "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>";   // one output for a three-component space
            var page = ShadingPage($"<< /ShadingType 2 /ColorSpace /DeviceRGB /Domain [0 1] /Coords [0 0 100 0] /Function {function} >>");

            Assert.True(page.At(2, 50, out _).R > 200);
        }

        [Fact]
        public void PatternColourWithoutAKnownPattern_IsReported()
        {
            var page = Draw("/Pattern cs /Nope scn 0 0 10 10 re f");

            Assert.Contains("scn (missing pattern)", page.Unsupported);
        }

        // ------------------------------------------------------------ images that are refused or unusual

        private static string Image(string entries, string hex) =>
            TestPdf.Stream($"/Type /XObject /Subtype /Image {entries} /Filter /ASCIIHexDecode", hex + ">");

        private static Page WithImage(string entries, string hex, string content = "100 0 0 100 0 0 cm /Im0 Do", string extraResources = "") =>
            Draw(content, "/XObject << /Im0 5 0 R >> " + extraResources, Image(entries, hex));

        [Fact]
        public void ImagesThatCannotBeUsed_AreReportedWithTheReason()
        {
            Assert.Contains("image: image size", WithImage("/Width 0 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "00").Unsupported);
            Assert.Contains("image: bits per component", WithImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 3", "00").Unsupported);
            Assert.Contains("image: image colour space", WithImage("/Width 1 /Height 1 /ColorSpace /Nope /BitsPerComponent 8", "00").Unsupported);
            Assert.Contains("image: image data could not be decoded", Draw("100 0 0 100 0 0 cm /Im0 Do", "/XObject << /Im0 5 0 R >>",
                TestPdf.Stream("/Type /XObject /Subtype /Image /Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /DCTDecode /SMask 6 0 R", "x"),
                Image("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8", "FF")).Unsupported);
        }

        [Fact]
        public void SixteenBitAndSubByteSamples_AreUnpacked()
        {
            // 16-bit gray: 0xFFFF is white; 2-bit gray 0b11 is white; 4-bit gray 0xF is white.
            Assert.True(WithImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 16", "FFFF").At(50, 50, out _).R > 240);
            Assert.True(WithImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 2", "C0").At(50, 50, out _).R > 240);
            Assert.True(WithImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 4", "F0").At(50, 50, out _).R > 240);
            // A short data stream reads the missing samples as zero rather than failing.
            Assert.True(WithImage("/Width 4 /Height 4 /ColorSpace /DeviceGray /BitsPerComponent 8", "FF").At(90, 90, out _).A > 0);
        }

        [Fact]
        public void DecodeArray_InvertsTheSamples_AndAStencilDecodeInvertsThePainting()
        {
            var inverted = WithImage("/Width 1 /Height 1 /ColorSpace /DeviceGray /BitsPerComponent 8 /Decode [1 0]", "FF").At(50, 50, out _);
            Assert.True(inverted.R < 20, $"white inverted is black: {inverted}");

            var stencil = WithImage("/Width 2 /Height 1 /ImageMask true /Decode [1 0]", "40", content: "1 0 0 rg 100 0 0 100 0 0 cm /Im0 Do");
            Assert.True(stencil.At(25, 50, out _).A < 20);              // sample 0 is now unpainted
            Assert.True(stencil.At(75, 50, out _).A > 200);
        }

        [Fact]
        public void ExplicitStencilMask_AndConstantAlpha_ShapeTheImage()
        {
            var mask = Image("/Width 2 /Height 1 /ImageMask true", "40");     // bit 0 = painted ... as a /Mask stream, 1 means masked out
            var page = Draw("100 0 0 100 0 0 cm /Im0 Do", "/XObject << /Im0 5 0 R >>",
                Image("/Width 2 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8 /Mask 6 0 R", "FF0000 FF0000"), mask);

            Assert.True(page.At(25, 50, out _).A > 200);
            Assert.True(page.At(75, 50, out _).A < 20);

            var translucent = Draw("/G0 gs 100 0 0 100 0 0 cm /Im0 Do", "/XObject << /Im0 5 0 R >> /ExtGState << /G0 << /ca 0.5 >> >>",
                Image("/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", "FF0000"));
            Assert.InRange(translucent.At(50, 50, out _).A, 110, 145);
        }

        [Fact]
        public void ImageObjectsAreDecodedOncePerPage_AndTheSameImageCanBeDrawnTwice()
        {
            var page = Draw("q 50 0 0 50 0 0 cm /Im0 Do Q q 50 0 0 50 50 50 cm /Im0 Do Q", "/XObject << /Im0 5 0 R >>",
                Image("/Width 1 /Height 1 /ColorSpace /DeviceRGB /BitsPerComponent 8", "FF0000"));

            Red(page, 25, 75);
            Red(page, 75, 25);
            Empty(page, 25, 25);
        }

        [Fact]
        public void StencilMasksAreRedecodedWhenTheFillColourChanges()
        {
            var page = Draw("1 0 0 rg q 50 0 0 100 0 0 cm /Im0 Do Q 0 0 1 rg q 50 0 0 100 50 0 cm /Im0 Do Q", "/XObject << /Im0 5 0 R >>",
                Image("/Width 1 /Height 1 /ImageMask true", "00"));

            Red(page, 25, 50);
            var right = page.At(75, 50, out _);
            Assert.True(right.B > 200 && right.R < 60, $"the second stamp is blue: {right}");
        }

        [Fact]
        public void BlendMode_FromAnExtGState_ChangesHowLaterPaintCombines_AndQRestoresIt()
        {
            const string res = "/ExtGState << /Mul << /BM /Multiply >> /Arr << /BM [/Bogus /Screen] >> /Bad << /BM /Bogus >> /Norm << /BM /Normal >> >>";
            // Blue multiplied over red is black; ordinary blue over red is blue.
            var multiplied = Draw("1 0 0 rg 0 0 100 100 re f /Mul gs 0 0 1 rg 0 0 100 100 re f", res).At(50, 50, out _);
            var normal = Draw("1 0 0 rg 0 0 100 100 re f 0 0 1 rg 0 0 100 100 re f", res).At(50, 50, out _);
            Assert.True(multiplied.R < 30 && multiplied.B < 30, $"multiply gives black: {multiplied}");
            Assert.True(normal.B > 200, $"normal gives blue: {normal}");

            var restored = Draw("1 0 0 rg 0 0 100 100 re f q /Mul gs Q 0 0 1 rg 0 0 100 100 re f", res).At(50, 50, out _);
            Assert.True(restored.B > 200, $"Q undoes the blend mode: {restored}");

            // The first known name of an array is used; an unknown mode is reported; Normal over Normal changes nothing.
            var screened = Draw("1 0 0 rg 0 0 100 100 re f /Arr gs 0 0 1 rg 0 0 100 100 re f", res).At(50, 50, out _);
            Assert.True(screened.R > 200 && screened.B > 200, $"screen of blue over red is magenta: {screened}");
            Assert.Contains("gs BM", Draw("/Bad gs", res).Unsupported);
            Assert.Empty(Draw("/Norm gs /Norm gs", res).Unsupported);
        }

        // ------------------------------------------------------------ soft masks

        private static string MaskForm(string content) =>
            TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 100 100] /Group << /S /Transparency /CS /DeviceGray >>", content);

        private static Page Masked(string content, string maskDict, string maskContent) =>
            Draw(content, $"/ExtGState << /G0 << /SMask {maskDict} >> /G1 << /SMask /None >> >>", MaskForm(maskContent));

        [Fact]
        public void LuminositySoftMask_HidesWhereTheMaskIsBlack()
        {
            // The mask is white on the left half only; the red square shows there and nowhere else.
            var page = Masked("/G0 gs 1 0 0 rg 0 0 100 100 re f", "<< /Type /Mask /S /Luminosity /G 5 0 R >>", "1 g 0 0 50 100 re f");

            Red(page, 25, 50);
            Empty(page, 75, 50);
        }

        [Fact]
        public void LuminositySoftMask_WithAWhiteBackdrop_ShowsWhereTheMaskDrawsNothing()
        {
            var page = Masked("/G0 gs 1 0 0 rg 0 0 100 100 re f", "<< /Type /Mask /S /Luminosity /BC [1] /G 5 0 R >>", "0 g 0 0 50 100 re f");

            Empty(page, 25, 50);        // the mask is black here
            Red(page, 75, 50);          // the backdrop (white) shows through everywhere else
        }

        [Fact]
        public void AlphaSoftMask_KeepsWhereTheMaskDrew()
        {
            var page = Masked("/G0 gs 1 0 0 rg 0 0 100 100 re f", "<< /Type /Mask /S /Alpha /G 5 0 R >>", "0 g 50 0 50 100 re f");

            Empty(page, 25, 50);
            Red(page, 75, 50);
        }

        [Fact]
        public void SoftMask_IsEndedByQ_AndByNone()
        {
            var afterQ = Masked("q /G0 gs 1 0 0 rg 0 0 100 100 re f Q 0 0 1 rg 0 0 10 10 re f", "<< /S /Luminosity /G 5 0 R >>", "1 g 0 0 50 50 re f");
            Red(afterQ, 25, 75);
            Empty(afterQ, 75, 25);
            Assert.True(afterQ.At(5, 95, out _).B > 200, "drawing after Q is unmasked");

            var afterNone = Masked("/G0 gs 1 0 0 rg 0 0 100 100 re f /G1 gs 0 0 1 rg 0 0 10 10 re f", "<< /S /Luminosity /G 5 0 R >>", "1 g 0 0 50 50 re f");
            Red(afterNone, 25, 75);
            Assert.True(afterNone.At(5, 95, out _).B > 200, "/None ends the mask");
        }

        [Fact]
        public void SoftMask_AppliesUnderTheTransformInForceWhenItWasSet()
        {
            // Set under a 0.5 scale, so the mask's white 0..100 square covers only the page's 0..50.
            var page = Masked("0.5 0 0 0.5 0 0 cm /G0 gs 1 0 0 rg 0 0 200 200 re f", "<< /S /Luminosity /G 5 0 R >>", "1 g 0 0 100 100 re f");

            Red(page, 25, 75);
            Empty(page, 75, 25);
        }

        [Fact]
        public void SoftMaskWithoutAGroup_OrNestedTooDeep_IsReported()
        {
            Assert.Contains("gs SMask (no group)", Masked("/G0 gs 0 0 1 1 re f", "<< /S /Luminosity >>", "").Unsupported);
        }

        [Fact]
        public void ReplacingASoftMask_EndsTheFirstGroup()
        {
            var page = Draw("/G0 gs 1 0 0 rg 0 0 100 100 re f /G2 gs 0 0 1 rg 0 0 100 100 re f",
                "/ExtGState << /G0 << /SMask << /S /Luminosity /G 5 0 R >> >> /G2 << /SMask << /S /Luminosity /G 6 0 R >> >> >>",
                MaskForm("1 g 0 0 50 100 re f"), MaskForm("1 g 50 0 50 100 re f"));

            Red(page, 25, 50);                                            // first mask: left
            Assert.True(page.At(75, 50, out _).B > 200, "second mask: right, in blue");
        }

        // ------------------------------------------------------------ Type 3 fonts

        private static string Type3Font(string extra = "", string procName = "sq") =>
            $"<< /Type /Font /Subtype /Type3 /FontBBox [0 0 750 750] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /{procName} 6 0 R >> " +
            "/Encoding << /Type /Encoding /Differences [65 /sq] >> /FirstChar 65 /LastChar 66 /Widths [800 400] " + extra + " >>";

        private static Page Type3(string content, string proc, string fontExtra = "")
        {
            return Draw(content, "/Font << /F0 5 0 R >>", Type3Font(fontExtra), TestPdf.Stream("", proc));
        }

        [Fact]
        public void Type3Glyph_IsDrawnUnderTheFontMatrixSizeAndPosition_InTheFillColour()
        {
            // 'd1' glyph: a 750-unit square, so 37.5pt at size 50. At (10, 20) it covers x 10..47.5, y 20..57.5 (canvas y 42.5..80).
            var page = Type3("BT /F0 50 Tf 10 20 Td 0 0 1 rg (A) Tj ET", "800 0 0 0 750 750 d1 1 0 0 rg 0 0 750 750 re f");

            Assert.True(page.At(25, 60, out var unsupported).B > 200, "painted in the fill colour; the glyph's own red is ignored after d1");
            Assert.Empty(unsupported);
            Assert.True(page.At(25, 60, out _).R < 60);
            Empty(page, 60, 60);
            Empty(page, 25, 90);
        }

        [Fact]
        public void Type3Advance_UsesTheWidthsTimesTheFontMatrix_SoTheNextGlyphLandsAfterIt()
        {
            // Width 800 -> 0.8 em -> 40pt at size 50, so the second glyph starts at x = 50.
            var page = Type3("BT /F0 50 Tf 10 20 Td 0 0 1 rg (AA) Tj ET", "800 0 0 0 750 750 d1 0 0 750 750 re f");

            Assert.True(page.At(25, 60, out _).B > 200);
            Assert.True(page.At(65, 60, out _).B > 200, "the second glyph");
            Empty(page, 49, 60);                                                      // the gap between them (47.5 .. 50)
        }

        [Fact]
        public void Type3GlyphThatStartsWithD0_SetsItsOwnColours()
        {
            var page = Type3("BT /F0 50 Tf 10 20 Td 0 0 1 rg (A) Tj ET", "800 0 d0 1 0 0 rg 0 0 750 750 re f");

            Red(page, 25, 60);
        }

        [Fact]
        public void Type3Text_HonoursSpacingScaleRiseAndTheTextMatrix_AndInvisibleTextDrawsNothing()
        {
            const string proc = "800 0 0 0 750 750 d1 0 0 750 750 re f";
            Assert.True(Type3("BT /F0 50 Tf 0 0 1 rg 20 Tc 10 20 Td (AA) Tj ET", proc).At(85, 60, out _).B > 200, "Tc adds space after each glyph");
            Assert.True(Type3("BT /F0 50 Tf 0 0 1 rg 50 Tz 10 20 Td (A) Tj ET", proc).At(15, 60, out _).B > 200, "Tz 50 halves the width...");
            Empty(Type3("BT /F0 50 Tf 0 0 1 rg 50 Tz 10 20 Td (A) Tj ET", proc), 35, 60);
            Assert.True(Type3("BT /F0 50 Tf 0 0 1 rg 30 Ts 10 20 Td (A) Tj ET", proc).At(25, 35, out _).B > 200, "Ts lifts it");
            Assert.True(Type3("BT /F0 50 Tf 0 0 1 rg 1 0 0 1 40 20 Tm (A) Tj ET", proc).At(55, 60, out _).B > 200, "Tm places it");
            Empty(Type3("BT /F0 50 Tf 0 0 1 rg 3 Tr 10 20 Td (A) Tj ET", proc), 25, 60);
            Assert.True(Type3("BT /F0 50 Tf 0 0 1 rg 10 20 Td [(A) -500 (A)] TJ ET", proc).At(85, 60, out _).B > 200, "TJ adjusts: the second glyph starts 25pt later than it would");
        }

        [Fact]
        public void Type3_WordSpacing_ComesFromCodeThirtyTwoOnly()
        {
            const string spaceFont = "<< /Type /Font /Subtype /Type3 /FontBBox [0 0 750 750] /FontMatrix [0.001 0 0 0.001 0 0] /CharProcs << /sq 6 0 R /sp 7 0 R >> /Encoding << /Differences [32 /sp 65 /sq] >> /FirstChar 32 /LastChar 65 /Widths [500 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 0 800] >>";
            var page = Draw("BT /F0 50 Tf 0 0 1 rg 20 Tw 10 20 Td (A A) Tj ET", "/Font << /F0 5 0 R >>", spaceFont,
                TestPdf.Stream("", "800 0 0 0 750 750 d1 0 0 750 750 re f"), TestPdf.Stream("", "500 0 d0"));

            // 'A' at 10..47.5, then the space (25pt + 20pt word spacing), so the second 'A' starts at x = 10 + 40 + 25 + 20 = 95.
            Assert.True(page.At(25, 60, out _).B > 200);
            Assert.True(page.At(97, 60, out _).B > 200, "word spacing applies to the space");
        }

        [Fact]
        public void Type3Font_WithoutGlyphProcedures_OrACodeWithoutAProcedure_IsHandled()
        {
            Assert.Contains("font: Type3 font without CharProcs", Draw("BT /F0 20 Tf (A) Tj ET", "/Font << /F0 5 0 R >>", "<< /Type /Font /Subtype /Type3 >>").Unsupported);

            var page = Type3("BT /F0 50 Tf 0 0 1 rg 10 20 Td (BA) Tj ET", "800 0 0 0 750 750 d1 0 0 750 750 re f");   // B has a width but no glyph
            Empty(page, 25, 60);
            Assert.True(page.At(40, 60, out _).B > 200, "A after B's 20pt advance (400 * 0.05)");
        }

        [Fact]
        public void Type3GlyphsThatShowType3Text_DoNotRecurseWithoutEnd()
        {
            var selfShowing = Draw("BT /F0 50 Tf (A) Tj ET", "/Font << /F0 5 0 R >>", Type3Font(" /Resources << /Font << /F0 5 0 R >> >>"),
                TestPdf.Stream("", "800 0 0 0 750 750 d1 BT /F0 50 Tf (A) Tj ET"));

            Assert.NotNull(selfShowing.Unsupported);   // it returns at all: the nesting limit stops the recursion
        }
    }
}
