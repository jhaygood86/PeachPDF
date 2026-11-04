using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF;

namespace PeachDrawing.PDF.Tests
{
    /// <summary>
    /// The annotation API of the package written the way a consumer uses it: a composer, a builder, the standard appearances. Nothing here
    /// references PeachPDF.
    /// </summary>
    public class PdfAnnotationApiTests
    {
        private static readonly PaintColor Red = PaintColor.FromArgb(255, 200, 0, 0);

        private static PdfComposer NewComposer(bool withFile = true)
        {
            var options = new PdfComposerOptions { CompressContentStreams = false };
            if (withFile)
            {
                options.EmbeddedFiles.Add(new PdfEmbeddedFile
                {
                    FileName = "data.csv",
                    Data = Encoding.UTF8.GetBytes("a,b\n1,2\n"),
                    MimeType = "text/csv",
                });
            }

            return new PdfComposer(new PdfRenderContext(), options);
        }

        private static string Save(PdfComposer composer)
        {
            using var stream = new MemoryStream();
            composer.Save(stream);
            return Encoding.Latin1.GetString(stream.ToArray());
        }

        private static PdfAnnotationBuilder BuilderFor(PdfComposer composer) => composer.CreateAnnotationBuilder(new PdfRenderContext());

        [Fact]
        public void FileAttachment_PointsAtTheEmbeddedFile_WithoutStoringItAgain()
        {
            using var composer = NewComposer();
            var page = composer.AddPage(300, 300);
            Assert.Equal(["data.csv"], composer.EmbeddedFileNames);

            BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "FileAttachment",
                Bounds = new Rect(20, 30, 16, 16),
                ContentSize = new Size(16, 16),
                PaintAppearance = PdfAnnotationAppearance.Icon(PdfAnnotationIcon.PushPin, new Size(16, 16)),
                IconName = PdfAnnotationAppearance.IconName(PdfAnnotationIcon.PushPin),
                EmbeddedFileName = "data.csv",
                Contents = "The data",
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Contains("/Subtype /FileAttachment", pdf);
            Assert.Contains("/Name /PushPin", pdf);
            Assert.Matches(@"/FS \d+ 0 R", pdf);
            Assert.Contains("/AP", pdf);
            // The file spec the annotation names is the one object that holds the embedded stream: one stream, however it is reached.
            Assert.Single(Regex.Matches(pdf, "/Type /EmbeddedFile"));
            var spec = Regex.Match(pdf, @"/FS (\d+) 0 R").Groups[1].Value;
            Assert.Matches($@"(?m)^{spec} 0 obj\s*<<[^>]*?/Type /Filespec", pdf);
        }

        [Fact]
        public void FileAttachment_NamingAFileThatIsNotEmbedded_IsRejected()
        {
            using var composer = NewComposer();
            var page = composer.AddPage(300, 300);

            var error = Assert.Throws<ArgumentException>(() => BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "FileAttachment",
                Bounds = new Rect(0, 0, 16, 16),
                ContentSize = new Size(16, 16),
                EmbeddedFileName = "missing.csv",
            }));

            Assert.Contains("missing.csv", error.Message);
        }

        [Fact]
        public void FileAttachment_WithNoFileName_IsRejected()
        {
            using var composer = NewComposer();
            var page = composer.AddPage(300, 300);

            Assert.Throws<ArgumentException>(() => BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "FileAttachment",
                Bounds = new Rect(0, 0, 16, 16),
                ContentSize = new Size(16, 16),
            }));
        }

        [Fact]
        public void EmbeddedFileNames_IsEmptyForADocumentThatEmbedsNothing()
        {
            using var composer = NewComposer(withFile: false);
            composer.AddPage(100, 100);

            Assert.Empty(composer.EmbeddedFileNames);
        }

        [Theory]
        [InlineData(PdfAnnotationIcon.Note, "Note", false)]
        [InlineData(PdfAnnotationIcon.Comment, "Comment", false)]
        [InlineData(PdfAnnotationIcon.Key, "Key", false)]
        [InlineData(PdfAnnotationIcon.Help, "Help", false)]
        [InlineData(PdfAnnotationIcon.NewParagraph, "NewParagraph", false)]
        [InlineData(PdfAnnotationIcon.Paragraph, "Paragraph", false)]
        [InlineData(PdfAnnotationIcon.Insert, "Insert", false)]
        [InlineData(PdfAnnotationIcon.Paperclip, "Paperclip", true)]
        [InlineData(PdfAnnotationIcon.PushPin, "PushPin", true)]
        [InlineData(PdfAnnotationIcon.Graph, "Graph", true)]
        [InlineData(PdfAnnotationIcon.Tag, "Tag", true)]
        public void Icons_HaveTheirPdfNameAndDrawSomething(PdfAnnotationIcon icon, string name, bool fileAttachment)
        {
            Assert.Equal(name, PdfAnnotationAppearance.IconName(icon));
            Assert.Equal(fileAttachment, PdfAnnotationAppearance.IsFileAttachmentIcon(icon));

            using var composer = NewComposer(withFile: false);
            var page = composer.AddPage(100, 100);
            BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Text",
                Bounds = new Rect(10, 10, 20, 20),
                ContentSize = new Size(20, 20),
                PaintAppearance = PdfAnnotationAppearance.Icon(icon, new Size(20, 20), Red),
                IconName = name,
            });
            page.Finish();

            var pdf = Save(composer);

            // The appearance form holds real path operators in the colour the icon was asked for, so it is not an empty stream.
            Assert.Contains($"/Name /{name}", pdf);
            Assert.Matches(@"(?s)/Subtype /Form.*?stream.*?\sf\*?\s", pdf);
            if (icon is not (PdfAnnotationIcon.NewParagraph or PdfAnnotationIcon.Paperclip))
                Assert.Contains("0.784 0 0 rg", pdf);
        }

        [Fact]
        public void Icon_OutOfRange_Throws()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => PdfAnnotationAppearance.IconName((PdfAnnotationIcon)99));
        }

        [Theory]
        [InlineData("Highlight")]
        [InlineData("Underline")]
        [InlineData("Squiggly")]
        [InlineData("StrikeOut")]
        public void TextMarkup_WritesQuadPointsFlippedToPdfSpace_AndAnAppearance(string subtype)
        {
            using var composer = NewComposer(withFile: false);
            var page = composer.AddPage(200, 200);
            var quads = new[] { new PaintPoint(10, 20), new PaintPoint(60, 20), new PaintPoint(10, 32), new PaintPoint(60, 32) };
            var bounds = new Rect(10, 20, 50, 12);

            BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = subtype,
                Bounds = bounds,
                ContentSize = new Size(bounds.Width, bounds.Height),
                QuadPoints = quads,
                Color = Red,
                PaintAppearance = PdfAnnotationAppearance.Markup(subtype, quads, bounds, Red),
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Contains($"/Subtype /{subtype}", pdf);
            // y is flipped against the 200pt page height: 20 -> 180 and 32 -> 168.
            Assert.Matches(@"/QuadPoints \[\s*10 180 60 180 10 168 60 168\s*\]", pdf);
        }

        [Fact]
        public void Markup_WithANonMarkupSubtype_IsRejected()
        {
            Assert.Throws<ArgumentException>(() => PdfAnnotationAppearance.Markup("Square", [], new Rect(0, 0, 1, 1), Red));
        }

        [Fact]
        public void Popup_IsWrittenAsALinkedAnnotation_OnlyWhenThereIsTextToShow()
        {
            using var composer = NewComposer(withFile: false);
            var page = composer.AddPage(300, 300);
            var builder = BuilderFor(composer);

            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Square",
                Bounds = new Rect(10, 10, 50, 50),
                ContentSize = new Size(50, 50),
                PaintAppearance = PdfAnnotationAppearance.Outline(false, new Size(50, 50), Red),
                Contents = "Look here",
                PopupBounds = new Rect(100, 10, 150, 80),
                PopupOpen = true,
            });
            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Circle",
                Bounds = new Rect(10, 100, 50, 50),
                ContentSize = new Size(50, 50),
                PaintAppearance = PdfAnnotationAppearance.Outline(true, new Size(50, 50), Red),
                PopupBounds = new Rect(100, 100, 150, 80),
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Single(Regex.Matches(pdf, "/Subtype /Popup"));
            Assert.Matches(@"/Popup \d+ 0 R", pdf);
            Assert.Matches(@"/Parent \d+ 0 R", pdf);
            Assert.Contains("/Open true", pdf);
        }

        [Fact]
        public void Dates_AndFreeText_WriteTheirEntries()
        {
            using var composer = NewComposer(withFile: false);
            var page = composer.AddPage(300, 300);

            BuilderFor(composer).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "FreeText",
                Bounds = new Rect(10, 10, 100, 30),
                ContentSize = new Size(100, 30),
                Contents = "Hello",
                CreatedAt = new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero),
                ModifiedAt = new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero),
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Matches(@"/CreationDate \(D:2026030[0-9]", pdf);
            Assert.Matches(@"/M \(D:2026030[0-9]", pdf);
            Assert.Contains("/DA (/Helv 9 Tf 0 g)", pdf);
        }

        [Fact]
        public void ShapesInkAndLine_DrawTheirStrokes()
        {
            using var composer = NewComposer(withFile: false);
            var page = composer.AddPage(300, 300);
            var builder = BuilderFor(composer);
            var strokes = new IReadOnlyList<PaintPoint>[] { [new(0, 0), new(10, 10), new(20, 0)] };

            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Ink",
                Bounds = new Rect(10, 10, 20, 10),
                ContentSize = new Size(20, 10),
                InkStrokes = [[new(10, 10), new(20, 20), new(30, 10)]],
                PaintAppearance = PdfAnnotationAppearance.Ink(strokes, Red, 2),
            });
            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Line",
                Bounds = new Rect(50, 50, 40, 40),
                ContentSize = new Size(40, 40),
                LineEnds = [new(50, 50), new(90, 90)],
                PaintAppearance = PdfAnnotationAppearance.Line(new PaintPoint(0, 0), new PaintPoint(40, 40), Red),
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Contains("/Subtype /Ink", pdf);
            Assert.Contains("/InkList", pdf);
            Assert.Contains("/Subtype /Line", pdf);
            Assert.Matches(@"/L \[\s*50 250 90 210\s*\]", pdf);
        }

        [Fact]
        public async Task Stamp_AndFreeText_DrawTheirTextWithTheGivenFont()
        {
            var context = new PdfRenderContext();
            var name = "Annot-" + Guid.NewGuid().ToString("N");
            await using (var stream = File.OpenRead(PeachPDF.Tests.TestSupport.BundledFonts.Ttf))
                await context.AddFont(stream, name);
            var font = context.GetFont(name, 12, PaintFontStyle.Regular)!;

            using var composer = new PdfComposer(context, new PdfComposerOptions { CompressContentStreams = false });
            var page = composer.AddPage(300, 300);
            var builder = composer.CreateAnnotationBuilder(context);

            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Stamp",
                Bounds = new Rect(10, 10, 120, 40),
                ContentSize = new Size(120, 40),
                IconName = "Draft",
                PaintAppearance = PdfAnnotationAppearance.Stamp("draft", font, Red, new Size(120, 40)),
            });
            builder.AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "FreeText",
                Bounds = new Rect(10, 100, 120, 40),
                ContentSize = new Size(120, 40),
                Contents = "Note",
                PaintAppearance = PdfAnnotationAppearance.FreeText("Note", font, Red, new Size(120, 40)),
            });
            page.Finish();

            var pdf = Save(composer);

            Assert.Matches(@"\sT[jJ]\s", pdf);
            Assert.Contains("/FontFile2", pdf);
        }
    }
}
