using PeachPDF.Tests.TestSupport;
using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// End-to-end tests for the <c>-peachpdf-pdf-annotation-*</c> properties: what the saved document holds for each kind of annotation,
    /// read back structurally rather than by the presence of a token.
    /// </summary>
    public class PdfAnnotationPropertyIntegrationTests
    {
        static readonly byte[] CsvBytes = Encoding.UTF8.GetBytes("a,b\n1,2\n");

        static PdfGenerateConfig Config(PdfAConformance conformance = PdfAConformance.None, bool attach = true)
        {
            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = conformance,
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2026, 1, 15, 10, 30, 0, TimeSpan.Zero) },
            };

            if (attach)
            {
                config.Attachments.Add(new PdfAttachment
                {
                    FileName = "q3-sales.csv",
                    Data = CsvBytes,
                    MimeType = "text/csv",
                    Relationship = PdfAttachmentRelationship.Data,
                    Description = "The numbers behind the report's table",
                });
            }

            return config;
        }

        static async Task<PeachPdfDocument> Make(string body, PdfGenerateConfig config) =>
            await new PdfGenerator().GeneratePdf($"<html><body style=\"margin:0\">{body}</body></html>", config);

        [Fact]
        public async Task FileAttachment_OnEmptySpan_PointsAtTheEmbeddedFileWithoutStoringItTwice()
        {
            var document = await Make(
                "<p>Q3 sales by region <span style=\"-peachpdf-pdf-annotation-type: file-attachment; -peachpdf-pdf-annotation-attachment: 'q3-sales.csv'\"></span></p>",
                Config(PdfAConformance.PdfA3B));

            var file = document.Inspect();
            var annotation = Assert.Single(file.Annotations(file.Pages[0]));

            Assert.Equal("/FileAttachment", annotation.Name("/Subtype"));
            Assert.Equal("/Paperclip", annotation.Name("/Name"));

            // The annotation's file spec is the very object the catalog's /AF array names: one embedded stream, two ways to reach it.
            var fileSpec = annotation.RefNumber("/FS");
            Assert.NotNull(fileSpec);
            var af = file.Catalog.Array("/AF")!.Resolved.OfType<PdfDict>().Single();
            Assert.Equal(af.ObjectNumber, fileSpec);

            // PDF/A: an annotation needs an appearance stream and the Print flag.
            Assert.NotNull(annotation.Dict("/AP")?.Dict("/N") ?? annotation.Dict("/AP")?["/N"] as PdfDict);
            Assert.True(((int)annotation.Number("/F")!.Value & 4) != 0);

            var rect = annotation.RectOf();
            Assert.InRange(rect.Right - rect.Left, 11.9, 12.1);
        }

        [Fact]
        public async Task FileAttachment_NamingAFileTheDocumentDoesNotEmbed_IsSkipped()
        {
            var document = await Make(
                "<p>x <span style=\"-peachpdf-pdf-annotation-type: file-attachment; -peachpdf-pdf-annotation-attachment: 'missing.csv'\"></span></p>",
                Config());

            Assert.Empty(document.PageAnnotations());
        }

        [Fact]
        public async Task FileAttachment_WithoutAFileName_IsSkipped()
        {
            var document = await Make("<p>x <span style=\"-peachpdf-pdf-annotation-type: file-attachment\"></span></p>", Config());

            Assert.Empty(document.PageAnnotations());
        }

        [Theory]
        [InlineData("push-pin", "/PushPin")]
        [InlineData("graph", "/Graph")]
        [InlineData("tag", "/Tag")]
        [InlineData("paperclip", "/Paperclip")]
        [InlineData("note", "/Paperclip")]
        public async Task FileAttachment_Icon_IsWrittenAsTheStandardName(string keyword, string expected)
        {
            var document = await Make(
                $"<p><span style=\"-peachpdf-pdf-annotation-type: file-attachment; -peachpdf-pdf-annotation-attachment: 'q3-sales.csv'; -peachpdf-pdf-annotation-icon: {keyword}\">.</span></p>",
                Config());

            Assert.Equal(expected, Assert.Single(document.PageAnnotations()).Name("/Name"));
        }

        [Fact]
        public async Task Note_UsesTextSubtypeWithContentsAuthorAndSubject()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'Check this'; -peachpdf-pdf-annotation-author: 'Ann'; -peachpdf-pdf-annotation-title: 'Review'; -peachpdf-pdf-annotation-icon: comment\">word</span></p>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal("/Text", annotation.Name("/Subtype"));
            Assert.Equal("/Comment", annotation.Name("/Name"));
            Assert.Equal("Check this", annotation.Text("/Contents"));
            Assert.Equal("Ann", annotation.Text("/T"));
            Assert.Equal("Review", annotation.Text("/Subj"));
        }

        [Theory]
        [InlineData("highlight", "/Highlight")]
        [InlineData("underline", "/Underline")]
        [InlineData("wavy", "/Squiggly")]
        [InlineData("line-through", "/StrikeOut")]
        public async Task TextMarkup_CoversEachLineOfTheElementWithQuadPointsAndAnAppearance(string keyword, string subtype)
        {
            var document = await Make(
                $"<p style=\"width:100pt\"><span style=\"-peachpdf-pdf-annotation-type: {keyword}\">several words that wrap over more than one line of text</span></p>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal(subtype, annotation.Name("/Subtype"));

            var quads = annotation.Array("/QuadPoints")!.Resolved.ToList();
            Assert.True(quads.Count >= 16 && quads.Count % 8 == 0, $"{quads.Count} quad numbers");
            Assert.NotNull(annotation.Dict("/AP"));
        }

        [Theory]
        [InlineData("square", "/Square")]
        [InlineData("circle", "/Circle")]
        [InlineData("line", "/Line")]
        [InlineData("stamp", "/Stamp")]
        [InlineData("free-text", "/FreeText")]
        public async Task ShapeAndTextBoxKinds_AreWrittenWithAnAppearance(string keyword, string subtype)
        {
            var document = await Make(
                $"<div style=\"width:120pt;height:40pt;-peachpdf-pdf-annotation-type: {keyword}; -peachpdf-pdf-annotation-contents: 'Draft'\"></div>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal(subtype, annotation.Name("/Subtype"));
            Assert.NotNull(annotation.Dict("/AP"));
        }

        [Fact]
        public async Task Ink_StrokesComeFromThePathProperty()
        {
            var document = await Make(
                "<div style=\"width:100pt;height:40pt;-peachpdf-pdf-annotation-type: ink; -peachpdf-pdf-annotation-path: path('M 0 0 L 40 20 C 50 30 60 30 70 10')\"></div>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal("/Ink", annotation.Name("/Subtype"));
            Assert.Single(annotation.Array("/InkList")!.Resolved);
        }

        [Fact]
        public async Task Ink_WithoutAPath_IsSkipped()
        {
            var document = await Make("<div style=\"height:20pt;-peachpdf-pdf-annotation-type: ink\"></div>", Config(attach: false));

            Assert.Empty(document.PageAnnotations());
        }

        [Fact]
        public async Task PrinceSpelling_IsAnAliasOfThePeachPdfOne()
        {
            var document = await Make(
                "<p><span style=\"-prince-pdf-annotation-type: text; -prince-pdf-annotation-contents: 'From Prince'; -prince-pdf-annotation-icon: key\">word</span></p>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal("/Text", annotation.Name("/Subtype"));
            Assert.Equal("/Key", annotation.Name("/Name"));
            Assert.Equal("From Prince", annotation.Text("/Contents"));
        }

        [Fact]
        public async Task Dates_AreWrittenAsPdfDates()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-createdate: '2026-03-04T05:06:07Z'; -peachpdf-pdf-annotation-modifydate: '2026-03-05T00:00:00Z'\">w</span></p>",
                Config(attach: false));

            var annotation = Assert.Single(document.PageAnnotations());
            Assert.Equal(new DateTimeOffset(2026, 3, 4, 5, 6, 7, TimeSpan.Zero).UtcDateTime, annotation.Date("/CreationDate")!.Value.UtcDateTime);
            Assert.Equal(new DateTimeOffset(2026, 3, 5, 0, 0, 0, TimeSpan.Zero).UtcDateTime, annotation.Date("/M")!.Value.UtcDateTime);
        }

        [Fact]
        public async Task Position_PlacesTheIconRelativeToTheBoxOrThePage()
        {
            var document = await Make(
                "<div style=\"margin:100pt 0 0 50pt;width:200pt;height:100pt;-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-position: right\"></div>",
                Config(attach: false));

            var rect = Assert.Single(document.PageAnnotations()).RectOf();
            // right: the icon's right edge is the box's (10pt page margin + 50pt + 200pt), its width the standard 16pt icon.
            Assert.InRange(rect.Right, 259.5, 260.5);
            Assert.InRange(rect.Right - rect.Left, 15.9, 16.1);

            var page = await Make(
                "<div style=\"margin:100pt 0 0 50pt;width:200pt;height:100pt;-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-position: page-right page-top\"></div>",
                Config(attach: false));
            var pageRect = Assert.Single(page.PageAnnotations()).RectOf();
            Assert.InRange(pageRect.Right, 594, 596);
            Assert.InRange(pageRect.Top, 825, 843);
        }

        [Fact]
        public async Task Position_OnMarkup_PlacesThePopupWindow()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: highlight; -peachpdf-pdf-annotation-contents: 'Why?'; -peachpdf-pdf-annotation-position: page-right\">word</span></p>",
                Config(attach: false));

            var annotations = document.PageAnnotations();
            Assert.Equal(2, annotations.Count);
            var highlight = annotations.Single(a => a.Name("/Subtype") == "/Highlight");
            var popup = annotations.Single(a => a.Name("/Subtype") == "/Popup");
            Assert.Equal(popup.ObjectNumber, highlight.RefNumber("/Popup"));
            Assert.Equal(highlight.ObjectNumber, popup.RefNumber("/Parent"));
            Assert.InRange(popup.RectOf().Right, 594, 596);
        }

        [Fact]
        public async Task Merge_Duplicates_DropsARepeatOfTheSameAnnotation()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'same'; -peachpdf-pdf-annotation-merge: duplicates\">a</span></p>" +
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'same'; -peachpdf-pdf-annotation-merge: duplicates\">b</span></p>" +
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'other'; -peachpdf-pdf-annotation-merge: duplicates\">c</span></p>",
                Config(attach: false));

            Assert.Equal(2, document.PageAnnotations().Count);
        }

        [Fact]
        public async Task Merge_Key_TreatsElementsWithTheSameKeyAsOne()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'x'; -peachpdf-pdf-annotation-merge: key('k')\">a</span></p>" +
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-contents: 'y'; -peachpdf-pdf-annotation-merge: key('k')\">b</span></p>",
                Config(attach: false));

            Assert.Single(document.PageAnnotations());
        }

        [Fact]
        public async Task Color_IsWrittenAsTheAnnotationColor_AndAutoLeavesTheKindDefault()
        {
            var document = await Make(
                "<p><span style=\"-peachpdf-pdf-annotation-type: text; -peachpdf-pdf-annotation-color: #ff0000\">a</span>" +
                "<span style=\"-peachpdf-pdf-annotation-type: square; -peachpdf-pdf-annotation-color: auto; display:inline-block; width:20pt; height:20pt\"></span></p>",
                Config(attach: false));

            var annotations = document.PageAnnotations();
            var red = annotations.Single(a => a.Name("/Subtype") == "/Text").Array("/C")!.Resolved.OfType<PdfNumber>().Select(n => n.Value).ToArray();
            Assert.Equal([1d, 0d, 0d], red);

            // auto: the square takes its kind's own colour, still a three-component /C.
            Assert.Equal(3, annotations.Single(a => a.Name("/Subtype") == "/Square").Array("/C")!.Resolved.ToList().Count);
        }

        [Fact]
        public async Task Annotation_OnAnElementThatBreaksAcrossPages_GetsOneMarkPerPage()
        {
            var lines = string.Concat(Enumerable.Repeat("<p>filler line</p>", 70));
            var document = await Make(
                lines + "<p><span style=\"-peachpdf-pdf-annotation-type: highlight\">" + string.Concat(Enumerable.Repeat("marked words ", 400)) + "</span></p>",
                Config(attach: false));

            var pagesWithMarks = Enumerable.Range(0, document.Inspect().Pages.Count).Count(page => document.PageAnnotations(page).Any(a => a.Name("/Subtype") == "/Highlight"));
            Assert.True(pagesWithMarks >= 2, $"{pagesWithMarks} pages carry the highlight");
        }

        [Fact]
        public async Task NoType_LeavesTheDocumentWithoutAnnotations()
        {
            var document = await Make("<p><span style=\"-peachpdf-pdf-annotation-contents: 'ignored'\">word</span></p>", Config(attach: false));

            Assert.Empty(document.PageAnnotations());
        }
    }
}
