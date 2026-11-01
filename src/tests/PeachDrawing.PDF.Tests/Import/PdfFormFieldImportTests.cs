using PeachDrawing.Core;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Form fields and annotation appearances read from a file, and carried onto a page of a new document.</summary>
    public class PdfFormFieldImportTests
    {
        private const string RedBox = "/Type /XObject /Subtype /Form /BBox [0 0 80 20]";
        private const string Red = "1 0 0 rg 0 0 80 20 re f";

        private static byte[] WithAcroForm(byte[] pdf) =>
            System.Text.Encoding.Latin1.GetBytes(System.Text.Encoding.Latin1.GetString(pdf)
                .Replace("/Type /Catalog /Pages 2 0 R", "/Type /Catalog /Pages 2 0 R /AcroForm << /Fields [] /DA (/Helv 0 Tf 0 g) >>"));

        private static byte[] Form(string annots, int width = 100, int height = 100, string pageExtras = "", params string[] objects) =>
            WithAcroForm(TestPdf.Build("0 g", pageExtras: "/Annots [" + annots + "] " + pageExtras, width: width, height: height, extraObjects: objects));

        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int x, int yFromTop, PdfAnnotationPaint paint, out IReadOnlyCollection<string> unsupported)
        {
            var source = PdfPageSource.Open(pdf);
            var (w, h) = source.GetPageSize(0);
            using var canvas = new RasterRenderContext().CreateCanvas((int)Math.Ceiling(w), (int)Math.Ceiling(h));
            unsupported = source.Paint(0, canvas, paint);
            var buffer = canvas.ToPixelBuffer();
            var i = (yFromTop * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static void IsColour((byte R, byte G, byte B, byte A) p, int r, int g, int b) =>
            Assert.True(Math.Abs(p.R - r) < 40 && Math.Abs(p.G - g) < 40 && Math.Abs(p.B - b) < 40 && p.A > 200, $"expected ({r},{g},{b}), was {p}");

        private static void IsEmpty((byte R, byte G, byte B, byte A) p) => Assert.True(p.A < 30, $"expected nothing, was {p}");

        private static byte[] TextFieldFile(string extra = "") => Form("5 0 R", objects:
        [
            "<< /Type /Annot /Subtype /Widget /FT /Tx /T (name) /V (Hello) /Rect [10 10 90 30] /AP << /N 6 0 R >> /DA (/Helv 12 Tf 0 g) /MaxLen 20 /TU (Your name) " + extra + " >>",
            TestPdf.Stream(RedBox, Red),
        ]);

        [Fact]
        public void TextField_IsReadWithItsValueFlagsAndPosition()
        {
            var field = Assert.Single(PdfPageSource.Open(TextFieldFile("/Ff 3")).GetFormFields(0));

            Assert.Equal(PdfFormFieldKind.Text, field.Kind);
            Assert.Equal("name", field.Name);
            Assert.Equal("Hello", field.Value);
            Assert.Equal(20, field.MaxLength);
            Assert.Equal("Your name", field.Placeholder);
            Assert.True(field.ReadOnly);
            Assert.True(field.Required);
            Assert.False(field.AutoFontSize);
            Assert.Equal(10, field.Bounds.X, 0.01);
            Assert.Equal(70, field.Bounds.Y, 0.01);                         // 100 - 30: from the top of the page
            Assert.Equal(80, field.Bounds.Width, 0.01);
            Assert.Equal(20, field.Bounds.Height, 0.01);
            Assert.Equal(20, field.ContentSize.Height, 0.01);
            Assert.Null(field.AppearanceTransform);
        }

        [Fact]
        public void TextField_KeepsItsAppearanceWhenAddedToANewDocument()
        {
            var source = PdfPageSource.Open(TextFieldFile());
            var saved = AddToNewDocument(source);

            // The field is there again, and drawn: the red of the original appearance sits where the field does.
            var field = Assert.Single(PdfPageSource.Open(saved).GetFormFields(0));
            Assert.Equal("name", field.Name);
            Assert.Equal("Hello", field.Value);
            Assert.Equal(20, field.MaxLength);
            IsColour(At(saved, 50, 80, PdfAnnotationPaint.All, out _), 255, 0, 0);
            IsEmpty(At(saved, 50, 50, PdfAnnotationPaint.All, out _));
        }

        private static byte[] AddToNewDocument(PdfPageSource source, Func<string, string>? rename = null, int pages = 1)
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions { CompressContentStreams = false });
            var builder = composer.CreateFormBuilder(new PdfRenderContext());
            var annotations = composer.CreateAnnotationBuilder(new PdfRenderContext());
            for (var i = 0; i < pages; i++)
            {
                var page = composer.AddPage(100, 100);
                foreach (var field in source.GetFormFields(0, 1, rename))
                    builder.AddField(page, field);
                foreach (var annotation in source.GetAnnotations(0))
                    annotations.AddAnnotation(page, annotation);
            }

            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        [Fact]
        public void TextFieldWithoutAnAppearance_IsStillAField()
        {
            var pdf = Form("5 0 R", objects: ["<< /Type /Annot /Subtype /Widget /FT /Tx /T (plain) /V (x) /Rect [0 0 50 20] >>"]);

            var field = Assert.Single(PdfPageSource.Open(pdf).GetFormFields(0));

            Assert.Equal("plain", field.Name);
            Assert.True(field.AutoFontSize);                                  // the form's default appearance has size 0
            IsEmpty(At(AddToNewDocument(PdfPageSource.Open(pdf)), 10, 90, PdfAnnotationPaint.All, out _));
        }

        [Fact]
        public void Checkbox_ReadsItsStateAndDrawsBothAppearances()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /On /AS /On /Rect [10 10 90 30] /AP << /N << /On 6 0 R /Off 7 0 R >> >> >>",
                TestPdf.Stream(RedBox, Red),
                TestPdf.Stream(RedBox, "0 0 1 rg 0 0 80 20 re f"),
            ]);
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.Checkbox, field.Kind);
            Assert.Equal("On", field.Value);
            Assert.True(field.IsChecked);

            var saved = AddToNewDocument(source);
            var again = Assert.Single(PdfPageSource.Open(saved).GetFormFields(0));
            Assert.True(again.IsChecked);
            Assert.Equal("On", again.Value);
            IsColour(At(saved, 50, 80, PdfAnnotationPaint.All, out _), 255, 0, 0);          // the checked appearance
        }

        [Fact]
        public void UncheckedCheckbox_ShowsItsOffAppearance()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Btn /T (agree) /V /Off /AS /Off /Rect [10 10 90 30] /AP << /N << /On 6 0 R /Off 7 0 R >> >> >>",
                TestPdf.Stream(RedBox, Red),
                TestPdf.Stream(RedBox, "0 0 1 rg 0 0 80 20 re f"),
            ]);
            var source = PdfPageSource.Open(pdf);

            Assert.False(Assert.Single(source.GetFormFields(0)).IsChecked);

            IsColour(At(AddToNewDocument(source), 50, 80, PdfAnnotationPaint.All, out _), 0, 0, 255);
        }

        [Fact]
        public void RadioButtons_OfOneGroup_StayOneField()
        {
            var pdf = Form("6 0 R 7 0 R", objects:
            [
                "<< /FT /Btn /Ff 32768 /T (size) /V /Big /Kids [6 0 R 7 0 R] >>",
                "<< /Type /Annot /Subtype /Widget /Parent 5 0 R /AS /Off /Rect [10 10 30 30] /AP << /N << /Small 8 0 R /Off 9 0 R >> >> >>",
                "<< /Type /Annot /Subtype /Widget /Parent 5 0 R /AS /Big /Rect [50 10 70 30] /AP << /N << /Big 8 0 R /Off 9 0 R >> >> >>",
                TestPdf.Stream(RedBox, Red),
                TestPdf.Stream(RedBox, "0 0 1 rg 0 0 80 20 re f"),
            ]);
            var source = PdfPageSource.Open(pdf);

            var fields = source.GetFormFields(0);
            Assert.Equal(2, fields.Count);
            Assert.All(fields, f => Assert.Equal(PdfFormFieldKind.Radio, f.Kind));
            Assert.All(fields, f => Assert.Equal("size", f.Name));
            Assert.Equal(["Small", "Big"], fields.Select(f => f.Value));
            Assert.Equal([false, true], fields.Select(f => f.IsChecked));          // /AS is per button; the second is selected

            var again = PdfPageSource.Open(AddToNewDocument(source)).GetFormFields(0);
            Assert.Equal(2, again.Count);
            Assert.Equal([false, true], again.Select(f => f.IsChecked));
        }

        [Fact]
        public void ChoiceField_ReadsItsOptionsAndSelection()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 131072 /T (pick) /V (b) /Opt [(a) [(b) (Bee)]] /Rect [10 10 90 30] /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);
            var field = Assert.Single(PdfPageSource.Open(pdf).GetFormFields(0));

            Assert.Equal(PdfFormFieldKind.Select, field.Kind);
            Assert.Equal(["a", "b"], field.Options.Select(o => o.Value));
            Assert.Equal(["a", "Bee"], field.Options.Select(o => o.Label));
            Assert.Equal([false, true], field.Options.Select(o => o.Selected));
        }

        [Fact]
        public void CombField_AndPassword_AreRead()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Tx /Ff 16785408 /T (code) /MaxLen 6 /Rect [10 10 90 30] >>",
            ]);
            var field = Assert.Single(PdfPageSource.Open(pdf).GetFormFields(0));

            Assert.Equal(6, field.CombCells);
            Assert.True(field.Password);
        }

        [Fact]
        public void FieldOnARotatedPage_IsPlacedAndTurnedWithThePage()
        {
            // A 200x100 page shown turned 90 degrees clockwise: the rectangle (10,20)-(90,40) lands at x 20..40, y 10..90.
            var pdf = Form("5 0 R", width: 200, height: 100, pageExtras: "/Rotate 90", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Tx /T (r) /Rect [10 20 90 40] /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));

            Assert.Equal(20, field.Bounds.X, 0.01);
            Assert.Equal(10, field.Bounds.Y, 0.01);
            Assert.Equal(20, field.Bounds.Width, 0.01);
            Assert.Equal(80, field.Bounds.Height, 0.01);
            Assert.Equal(80, field.ContentSize.Width, 0.01);                  // the upright appearance is still 80 wide
            Assert.NotNull(field.AppearanceTransform);
        }

        [Fact]
        public void HiddenWidgets_AreLeftOut()
        {
            var pdf = Form("5 0 R", objects: ["<< /Type /Annot /Subtype /Widget /FT /Tx /T (h) /F 2 /Rect [10 10 90 30] >>"]);

            Assert.Empty(PdfPageSource.Open(pdf).GetFormFields(0));
        }

        [Fact]
        public void Annotations_AreDrawnOnlyWhenAsked_AndFormFieldsOnlyForAll()
        {
            var pdf = Form("5 0 R 7 0 R 9 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [0 0 40 20] /AP << /N 6 0 R >> >>",
                TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 40 20]", "0 1 0 rg 0 0 40 20 re f"),
                "<< /Type /Annot /Subtype /Widget /FT /Tx /T (t) /Rect [0 40 40 60] /AP << /N 8 0 R >> >>",
                TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 40 20]", "1 0 0 rg 0 0 40 20 re f"),
                "<< /Type /Annot /Subtype /Widget /FT /Btn /Ff 65536 /T (go) /Rect [50 0 90 20] /AP << /N 10 0 R >> >>",
                TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 40 20]", "0 0 1 rg 0 0 40 20 re f"),
            ]);

            IsEmpty(At(pdf, 20, 90, PdfAnnotationPaint.None, out _));                                // nothing without asking
            IsColour(At(pdf, 20, 90, PdfAnnotationPaint.ExceptFormFields, out var unsupported), 0, 255, 0);   // the square annotation
            Assert.Empty(unsupported);
            IsEmpty(At(pdf, 20, 50, PdfAnnotationPaint.ExceptFormFields, out _));                    // the text field is carried, not drawn
            IsEmpty(At(pdf, 70, 90, PdfAnnotationPaint.ExceptFormFields, out _));                    // and so is the push button
            IsColour(At(pdf, 20, 50, PdfAnnotationPaint.All, out _), 255, 0, 0);                     // All draws the text field too
            IsColour(At(pdf, 70, 90, PdfAnnotationPaint.All, out _), 0, 0, 255);
            Assert.Equal([PdfFormFieldKind.Text, PdfFormFieldKind.PushButton], PdfPageSource.Open(pdf).GetFormFields(0).Select(f => f.Kind));
        }

        [Fact]
        public void AnnotationAppearance_IsFittedToItsRectangle()
        {
            // The appearance box is 10x10 but the annotation rectangle is 40x20: the green square is stretched to fill it.
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Stamp /Rect [10 10 50 30] /AP << /N 6 0 R >> >>",
                TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10]", "0 1 0 rg 0 0 10 10 re f"),
            ]);

            IsColour(At(pdf, 45, 75, PdfAnnotationPaint.ExceptFormFields, out _), 0, 255, 0);
            IsEmpty(At(pdf, 55, 75, PdfAnnotationPaint.ExceptFormFields, out _));
        }

        [Fact]
        public void HiddenAnnotation_IsNotDrawn()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Stamp /F 2 /Rect [10 10 50 30] /AP << /N 6 0 R >> >>",
                TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 10 10]", "0 1 0 rg 0 0 10 10 re f"),
            ]);

            IsEmpty(At(pdf, 20, 75, PdfAnnotationPaint.All, out _));
        }

        [Fact]
        public void TheSameFieldAddedTwice_CanBeRenamedPerPage()
        {
            var source = PdfPageSource.Open(TextFieldFile());
            var saved = AddToNewDocument(source, pages: 2, rename: null);

            // Without renaming, both pages hold a field of the same name (the caller's choice); the renaming hook lets a caller avoid it.
            Assert.Single(PdfPageSource.Open(saved).GetFormFields(0));

            var names = new List<string>();
            var renamed = source.GetFormFields(0, 1, n => { names.Add(n); return n + "_2"; });
            Assert.Equal(["name"], names);
            Assert.Equal("name_2", Assert.Single(renamed).Name);
        }

        [Fact]
        public void ListBox_IsKeptAsAListBoxWithItsSelection()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Ch /Ff 2097152 /T (many) /V [(a) (c)] /Opt [(a) [(b) (Bee)] (c)] /Rect [10 10 90 60] /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.List, field.Kind);
            Assert.True(field.MultiSelect);
            Assert.Equal([true, false, true], field.Options.Select(o => o.Selected));

            var again = Assert.Single(PdfPageSource.Open(AddToNewDocument(source)).GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.List, again.Kind);                                       // not a drop-down
            Assert.True(again.MultiSelect);
            Assert.Equal(["a", "b", "c"], again.Options.Select(o => o.Value));
            Assert.Equal([true, false, true], again.Options.Select(o => o.Selected));
        }

        [Fact]
        public void MultilineTextField_StaysMultiline()
        {
            var pdf = Form("5 0 R", objects: ["<< /Type /Annot /Subtype /Widget /FT /Tx /Ff 4096 /T (notes) /V (one) /Rect [10 10 90 60] >>"]);
            var source = PdfPageSource.Open(pdf);

            Assert.True(Assert.Single(source.GetFormFields(0)).Multiline);
            Assert.True(Assert.Single(PdfPageSource.Open(AddToNewDocument(source)).GetFormFields(0)).Multiline);
        }

        [Fact]
        public void PushButton_KeepsItsCaptionAndAppearance()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Btn /Ff 65536 /T (go) /MK << /CA (Send) >> /Rect [10 10 90 30] /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.PushButton, field.Kind);
            Assert.Equal("Send", field.Value);

            var saved = AddToNewDocument(source);
            var again = Assert.Single(PdfPageSource.Open(saved).GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.PushButton, again.Kind);
            Assert.Equal("Send", again.Value);
            IsColour(At(saved, 50, 80, PdfAnnotationPaint.All, out _), 255, 0, 0);
        }

        [Fact]
        public void SignatureField_IsKeptUnsigned_WithItsAppearance()
        {
            var pdf = Form("5 0 R", objects:
            [
                "<< /Type /Annot /Subtype /Widget /FT /Sig /T (sign) /V << /Type /Sig /Filter /Adobe.PPKLite /Contents <00> >> /Rect [10 10 90 30] /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.Signature, field.Kind);
            Assert.Null(field.Value);

            var saved = AddToNewDocument(source);
            Assert.Equal(PdfFormFieldKind.Signature, Assert.Single(PdfPageSource.Open(saved).GetFormFields(0)).Kind);
            IsColour(At(saved, 50, 80, PdfAnnotationPaint.All, out _), 255, 0, 0);
            Assert.DoesNotContain("PPKLite", System.Text.Encoding.Latin1.GetString(saved));          // the signature itself is not carried
        }

        private static byte[] AnnotationFile() => Form("5 0 R 7 0 R 9 0 R 11 0 R", objects:
        [
            "<< /Type /Annot /Subtype /Highlight /Rect [10 70 60 90] /QuadPoints [10 90 60 90 10 70 60 70] /C [1 1 0] /CA 0.5 /Contents (Important) /T (Ada) /Subj (Review) /F 4 /AP << /N 6 0 R >> >>",
            TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 50 20]", "1 1 0 rg 0 0 50 20 re f"),
            "<< /Type /Annot /Subtype /Ink /Rect [10 10 40 40] /InkList [[10 10 25 40 40 10]] /C [1 0 0] /BS << /W 2 >> /F 4 /AP << /N 8 0 R >> >>",
            TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 30 30]", "0 0 1 rg 0 0 30 30 re f"),
            "<< /Type /Annot /Subtype /Stamp /Name /Approved /Rect [60 10 90 30] /F 2 /AP << /N 10 0 R >> >>",
            TestPdf.Stream("/Type /XObject /Subtype /Form /BBox [0 0 30 20]", "0 1 0 rg 0 0 30 20 re f"),
            "<< /Type /Annot /Subtype /Text /Rect [60 40 80 60] /Contents (No appearance) /F 4 >>",
        ]);

        [Fact]
        public void Annotations_AreReadWithTheirMarkup_AndSkippedWithoutAppearanceOrWhenHidden()
        {
            var annotations = PdfPageSource.Open(AnnotationFile()).GetAnnotations(0);

            Assert.Equal(["Highlight", "Ink"], annotations.Select(a => a.Subtype));            // the stamp is hidden, the note has no appearance
            var highlight = annotations[0];
            Assert.Equal("Important", highlight.Contents);
            Assert.Equal("Ada", highlight.Title);
            Assert.Equal("Review", highlight.Subject);
            Assert.Equal(0.5, highlight.Opacity!.Value, 3);
            Assert.Equal(255, highlight.Color!.Value.R);
            Assert.Equal(255, highlight.Color!.Value.G);
            Assert.Equal(0, highlight.Color!.Value.B);
            Assert.Equal(4, highlight.QuadPoints.Count);
            Assert.Equal(10, highlight.QuadPoints[0].X, 0.01);
            Assert.Equal(10, highlight.QuadPoints[0].Y, 0.01);                                 // 100 - 90: from the top of the page
            Assert.Equal(10, highlight.Bounds.X, 0.01);
            Assert.Equal(10, highlight.Bounds.Y, 0.01);
            Assert.Equal(2, annotations[1].BorderWidth);
            var stroke = Assert.Single(annotations[1].InkStrokes);
            Assert.Equal(3, stroke.Count);
            Assert.Equal(90, stroke[0].Y, 0.01);                                               // (10, 10) up from the bottom is 90 from the top
        }

        [Fact]
        public void Annotations_AddedToANewDocument_KeepTheirMarkupAndAppearance()
        {
            var saved = AddToNewDocument(PdfPageSource.Open(AnnotationFile()));
            var again = PdfPageSource.Open(saved).GetAnnotations(0);

            Assert.Equal(["Highlight", "Ink"], again.Select(a => a.Subtype));
            Assert.Equal("Important", again[0].Contents);
            Assert.Equal("Ada", again[0].Title);
            Assert.Equal(4, again[0].QuadPoints.Count);
            Assert.Equal(10, again[0].QuadPoints[0].Y, 0.01);
            Assert.Single(again[1].InkStrokes);
            // They are real annotations now, and drawn: the highlight's yellow and the ink's blue box show where they sit.
            IsColour(At(saved, 30, 20, PdfAnnotationPaint.All, out _), 255, 255, 0);
            IsColour(At(saved, 20, 80, PdfAnnotationPaint.All, out _), 0, 0, 255);
        }

        [Fact]
        public void AnnotationOnARotatedPage_IsPlacedAndTurnedWithThePage()
        {
            var pdf = Form("5 0 R", width: 200, height: 100, pageExtras: "/Rotate 90", objects:
            [
                "<< /Type /Annot /Subtype /Square /Rect [10 20 90 40] /F 4 /AP << /N 6 0 R >> >>",
                TestPdf.Stream(RedBox, Red),
            ]);

            var annotation = Assert.Single(PdfPageSource.Open(pdf).GetAnnotations(0));

            Assert.Equal(20, annotation.Bounds.X, 0.01);
            Assert.Equal(10, annotation.Bounds.Y, 0.01);
            Assert.Equal(80, annotation.ContentSize.Width, 0.01);
            Assert.NotNull(annotation.AppearanceTransform);
        }
    }
}
