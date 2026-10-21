using PeachDrawing;
using PeachDrawing.Core;
using PeachDrawing.PDF;
using PeachPDF.Layout;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// <see cref="IDocumentBuilder.OverlayFile"/> and <see cref="IDocumentBuilder.UnderlayFile"/>: the finished document is read back
    /// and rasterized, so each test fails if the layer were not drawn, were drawn on the wrong page, or on the wrong side of the
    /// page's own content.
    /// </summary>
    public class FileLayerIntegrationTests
    {
        private static readonly (byte R, byte G, byte B) Red = (255, 0, 0);
        private static readonly (byte R, byte G, byte B) Blue = (0, 0, 255);
        private static readonly (byte R, byte G, byte B) Green = (0, 255, 0);

        /// <summary>A two-page 200x200pt file: page 1 has a red square at the top-left and one at the bottom-right, page 2 has a green square at the top-left.</summary>
        private static byte[] LayerFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            var first = composer.AddPage(200, 200).Canvas;
            var red = first.GetSolidBrush(PaintColor.FromArgb(255, 255, 0, 0));
            first.DrawRectangle(red, 0, 0, 50, 50);
            first.DrawRectangle(red, 150, 150, 40, 40);
            var second = composer.AddPage(200, 200).Canvas;
            second.DrawRectangle(second.GetSolidBrush(PaintColor.FromArgb(255, 0, 255, 0)), 0, 0, 50, 50);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        /// <summary>The base document: <paramref name="pages"/> pages of 200x200pt, each a 100pt blue square at the top-left.</summary>
        private static async Task<byte[]> BuildAsync(int pages, Action<IDocumentBuilder> layers)
        {
            var generator = new PdfGenerator();
            var document = await generator.CreateDocument(doc =>
            {
                for (var i = 0; i < pages; i++)
                {
                    doc.Page(page =>
                    {
                        page.Size(200, 200).Margin(0);
                        page.Content(container => container.Width(100).Height(100).Background(PdfColor.FromRgb(0, 0, 255)).Text(" "));
                    });
                }

                layers(doc);
            });

            using var ms = new MemoryStream();
            document.Save(ms);
            return ms.ToArray();
        }

        /// <summary>The colour at a point (in points from the page's top-left corner) of one page of <paramref name="pdf"/>, or null when nothing is drawn there.</summary>
        private static (byte R, byte G, byte B)? ColourAt(byte[] pdf, int pageIndex, int x, int y)
        {
            var source = PdfPageSource.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 200);
            source.Paint(pageIndex, canvas);
            return Pixel(canvas, x, y);
        }

        private static (byte R, byte G, byte B)? Pixel(RasterCanvas canvas, int x, int y)
        {
            var buffer = canvas.ToPixelBuffer();
            var i = (y * buffer.Width + x) * 4;
            var span = buffer.PremultipliedRgba.Span;
            return span[i + 3] == 0 ? null : (span[i], span[i + 1], span[i + 2]);
        }

        [Fact]
        public async Task OverlayFile_DrawsTheLayerOverThePageContent()
        {
            var pdf = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(LayerFile()).SourcePages("1")));

            Assert.Equal(Red, ColourAt(pdf, 0, 25, 25));    // over the blue square
            Assert.Equal(Blue, ColourAt(pdf, 0, 75, 75));   // blue square outside the layer's square
            Assert.Equal(Red, ColourAt(pdf, 0, 170, 170));  // where the page has nothing
        }

        [Fact]
        public async Task UnderlayFile_DrawsTheLayerUnderThePageContent()
        {
            var pdf = await BuildAsync(1, doc => doc.UnderlayFile(layer => layer.File(LayerFile()).SourcePages("1")));

            Assert.Equal(Blue, ColourAt(pdf, 0, 25, 25));   // the page's blue square covers the layer
            Assert.Equal(Red, ColourAt(pdf, 0, 170, 170));  // but the layer shows where the page draws nothing
        }

        [Fact]
        public async Task RepeatSourcePages_PutsTheSameFilePageOnEveryTargetPage()
        {
            var pdf = await BuildAsync(3, doc => doc.OverlayFile(layer => layer.File(LayerFile()).SourcePages("1").RepeatSourcePages("1")));

            for (var page = 0; page < 3; page++)
                Assert.Equal(Red, ColourAt(pdf, page, 25, 25));
        }

        [Fact]
        public async Task SourcePages_ArePairedWithTargetPagesInOrder_AndTheLayerStopsWhenTheyRunOut()
        {
            var pdf = await BuildAsync(3, doc => doc.OverlayFile(layer => layer.File(LayerFile())));

            Assert.Equal(Red, ColourAt(pdf, 0, 25, 25));            // file page 1
            Assert.Equal(Green, ColourAt(pdf, 1, 25, 25));    // file page 2
            Assert.Equal(Blue, ColourAt(pdf, 2, 25, 25));           // the file has no third page
        }

        [Fact]
        public async Task TargetPages_RestrictTheLayer_AndZMeansTheLastPage()
        {
            var pdf = await BuildAsync(3, doc => doc.OverlayFile(layer => layer.File(LayerFile()).SourcePages("1").RepeatSourcePages("1").TargetPages("2-z")));

            Assert.Equal(Blue, ColourAt(pdf, 0, 25, 25));
            Assert.Equal(Red, ColourAt(pdf, 1, 25, 25));
            Assert.Equal(Red, ColourAt(pdf, 2, 25, 25));
        }

        [Fact]
        public async Task LayersFromAPathAndAStream_AreRead()
        {
            var path = Path.Combine(Path.GetTempPath(), "layer-" + Guid.NewGuid().ToString("N") + ".pdf");
            await File.WriteAllBytesAsync(path, LayerFile(), TestContext.Current.CancellationToken);
            try
            {
                var fromPath = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(path).SourcePages("1")));
                using var stream = new MemoryStream(LayerFile());
                var fromStream = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(stream).SourcePages("1")));

                Assert.Equal(Red, ColourAt(fromPath, 0, 25, 25));
                Assert.Equal(Red, ColourAt(fromStream, 0, 25, 25));
            }
            finally
            {
                File.Delete(path);
            }
        }

        [Fact]
        public async Task ALayerWithNoFile_Throws()
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => BuildAsync(1, doc => doc.OverlayFile(_ => { })));
        }

        [Fact]
        public async Task ANotAPdfLayerFile_Throws()
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => BuildAsync(1, doc => doc.OverlayFile(layer => layer.File([1, 2, 3]))));
        }

        [Theory]
        [InlineData("1", 5, new[] { 1 })]
        [InlineData("1-3,5", 5, new[] { 1, 2, 3, 5 })]
        [InlineData("2-z", 4, new[] { 2, 3, 4 })]
        [InlineData("z-3", 5, new[] { 5, 4, 3 })]
        [InlineData(" 2 , 9 ", 5, new[] { 2 })]
        [InlineData("", 5, new int[0])]
        public void PageRange_ParsesPagesSpansAndZ(string range, int pageCount, int[] expected)
        {
            Assert.Equal(expected, PageRange.Parse(range, pageCount));
        }

        [Theory]
        [InlineData("a")]
        [InlineData("1-b")]
        [InlineData("-3")]
        public void PageRange_RejectsText_ThatIsNotARange(string range)
        {
            Assert.Throws<FormatException>(() => PageRange.Parse(range, 5));
        }

        /// <summary>A three-page file with a web link and a link to page 3 on page 1, a two-level outline, a named destination, information and an attachment.</summary>
        private static byte[] NavigationFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions
            {
                Title = "Stamp title",
                Author = "Stamp author",
                EmbeddedFiles = { new PdfEmbeddedFile { FileName = "stamp.txt", Data = "hello"u8.ToArray(), MimeType = "text/plain" } },
            });
            for (var i = 0; i < 3; i++)
                composer.AddPage(200, 200);
            composer.Pages[0].AddLink(new Rect(10, 10, 40, 20), "https://example.com/stamp");
            composer.AddNamedDestination("stamp-last", 2, 30);
            composer.Pages[0].AddDestinationLink(new Rect(10, 40, 40, 20), "stamp-last");
            composer.AddBookmarks(
            [
                new PdfBookmark(1, "First", true) { PageIndex = 0 },
                new PdfBookmark(2, "Nested", false) { PageIndex = 2, TopPoints = 30 },
                new PdfBookmark(1, "Second", false) { PageIndex = 1 },
            ]);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        /// <summary>Draws every state of a field as a solid colour, so a rasterized copy shows which appearance came across.</summary>
        private sealed class FlatAppearance(PaintColor colour) : IPdfFormAppearance
        {
            public double PixelsPerPoint => 1;

            public double TextFontSizePoints(Size sizePoints, bool autoFontSize, bool isPlaceholder) => 10;

            public void PaintText(Canvas canvas, Size sizePoints, string text, bool autoFontSize, int? combCells, bool isPlaceholder) =>
                canvas.DrawRectangle(canvas.GetSolidBrush(colour), 0, 0, sizePoints.Width, sizePoints.Height);

            public void PaintCheckbox(Canvas canvas, Size sizePoints, bool isChecked) => PaintText(canvas, sizePoints, "", false, null, false);

            public void PaintRadio(Canvas canvas, Size sizePoints, bool isSelected) => PaintText(canvas, sizePoints, "", false, null, false);
        }

        /// <summary>A one-page file with a text field named "who" (value "Ada", green) at the top-left and a stamp-like annotation drawn by its appearance.</summary>
        private static byte[] FormFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(200, 200);
            composer.CreateFormBuilder(new PdfRenderContext()).AddField(page, new PdfFormFieldDefinition
            {
                Kind = PdfFormFieldKind.Text,
                Name = "who",
                Value = "Ada",
                Bounds = new Rect(10, 10, 80, 20),
                ContentSize = new Size(80, 20),
                Appearance = new FlatAppearance(PaintColor.FromArgb(255, 0, 255, 0)),
            });
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        [Fact]
        public async Task FormFields_OfTheDrawnPage_AreCarriedAcrossWithTheirAppearance()
        {
            var pdf = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(FormFile()).SourcePages("1")));
            var source = PdfPageSource.Open(pdf);

            var field = Assert.Single(source.GetFormFields(0));

            Assert.Equal("who", field.Name);
            Assert.Equal("Ada", field.Value);
            Assert.Equal(10, field.Bounds.X, 0.5);
            Assert.Equal(10, field.Bounds.Y, 0.5);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 200);
            source.Paint(0, canvas, PdfAnnotationPaint.All);
            Assert.Equal(Green, Pixel(canvas, 50, 20));       // the field is drawn with the file's own appearance
        }

        /// <summary>A one-page file with a yellow highlight over (10,10)-(110,30) and a push button captioned "Go" below it.</summary>
        private static byte[] AnnotatedFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext());
            var page = composer.AddPage(200, 200);
            composer.CreateAnnotationBuilder(new PdfRenderContext()).AddAnnotation(page, new PdfAnnotationDefinition
            {
                Subtype = "Highlight",
                Bounds = new Rect(10, 10, 100, 20),
                ContentSize = new Size(100, 20),
                Contents = "Look here",
                Color = PaintColor.FromArgb(255, 255, 255, 0),
                QuadPoints = [new(10, 10), new(110, 10), new(10, 30), new(110, 30)],
                PaintAppearance = canvas => canvas.DrawRectangle(canvas.GetSolidBrush(PaintColor.FromArgb(255, 255, 255, 0)), 0, 0, 100, 20),
            });
            composer.CreateFormBuilder(new PdfRenderContext()).AddField(page, new PdfFormFieldDefinition
            {
                Kind = PdfFormFieldKind.PushButton,
                Name = "go",
                Value = "Go",
                Bounds = new Rect(10, 50, 60, 20),
                ContentSize = new Size(60, 20),
                Appearance = new FlatAppearance(PaintColor.FromArgb(255, 0, 255, 0)),
            });
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        [Fact]
        public async Task Annotations_AndPushButtons_OfTheDrawnPage_StayAnnotations()
        {
            var pdf = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(AnnotatedFile()).SourcePages("1")));
            var source = PdfPageSource.Open(pdf);

            var annotation = Assert.Single(source.GetAnnotations(0));
            Assert.Equal("Highlight", annotation.Subtype);
            Assert.Equal("Look here", annotation.Contents);
            Assert.Equal(4, annotation.QuadPoints.Count);
            var button = Assert.Single(source.GetFormFields(0));
            Assert.Equal(PdfFormFieldKind.PushButton, button.Kind);
            Assert.Equal("Go", button.Value);
            using var canvas = new RasterRenderContext().CreateCanvas(200, 200);
            source.Paint(0, canvas, PdfAnnotationPaint.All);
            Assert.Equal(((byte)255, (byte)255, (byte)0), Pixel(canvas, 60, 20));        // drawn by the annotation's own appearance
            Assert.Equal(Green, Pixel(canvas, 30, 60));                // and the button by its own
        }

        [Fact]
        public async Task FormFields_OfAPageDrawnTwice_GetNamesOfTheirOwn()
        {
            var pdf = await BuildAsync(2, doc => doc.OverlayFile(layer => layer.File(FormFile()).SourcePages("1").RepeatSourcePages("1")));
            var source = PdfPageSource.Open(pdf);

            var names = new[] { source.GetFormFields(0).Single().Name, source.GetFormFields(1).Single().Name };

            Assert.Equal(["who", "who_2"], names);
        }

        /// <summary>A PDF/X-4 file with an RGB output intent named "Stamp condition".</summary>
        private static byte[] PdfXFile()
        {
            using var composer = new PdfComposer(new PdfRenderContext(), new PdfComposerOptions
            {
                XConformance = PeachDrawing.PDF.PdfXConformance.X4,
                CreationDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero),
                Title = "Stamp",
                ColorOptions = new PeachDrawing.PDF.ColorOptions { OutputIntentProfile = TestSupport.IccProfileFixture.BuildRgbProfile(), OutputIntentIdentifier = "Stamp condition" },
            });
            composer.AddPage(200, 200);
            using var ms = new MemoryStream();
            composer.Save(ms);
            return ms.ToArray();
        }

        private static async Task<PdfPageSource> BuildWithAsync(byte[] file, PdfGenerateConfig config)
        {
            var document = await new PdfGenerator().CreateDocument(doc =>
            {
                doc.Page(page => page.Size(200, 200).Margin(0).Content(container => container.Text("Body")));
                doc.OverlayFile(layer => layer.File(file).SourcePages("1"));
            }, config);
            using var ms = new MemoryStream();
            document.Save(ms);
            return PdfPageSource.Open(ms.ToArray());
        }

        [Fact]
        public async Task PdfXDocumentWithoutAnOutputIntent_UsesTheOneOfTheFile()
        {
            var source = await BuildWithAsync(PdfXFile(), new PdfGenerateConfig { PdfXConformance = PdfXConformance.X4, Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero) } });

            Assert.Equal("Stamp condition", source.GetOutputIntent()!.Identifier);
            Assert.Equal(PeachDrawing.PDF.PdfXConformance.X4, source.GetConformance().PdfX);       // the document's own level
        }

        [Fact]
        public async Task FilesPdfXClaim_IsNotAdoptedByADocumentThatMakesNone_ButItsOutputIntentIs()
        {
            var source = await BuildWithAsync(PdfXFile(), new PdfGenerateConfig());

            Assert.Equal(new PdfSourceConformance(PeachDrawing.PDF.PdfAConformance.None, PeachDrawing.PDF.PdfXConformance.None), source.GetConformance());
            Assert.Equal("Stamp condition", source.GetOutputIntent()!.Identifier);
        }

        [Fact]
        public async Task Links_OfTheDrawnPage_AreCarriedAcross()
        {
            var pdf = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(NavigationFile()).SourcePages("1")));

            var links = PdfPageSource.Open(pdf).GetLinks(0);

            var web = Assert.Single(links, l => l.Uri is not null);
            Assert.Equal("https://example.com/stamp", web.Uri);
            Assert.Equal(10, web.Bounds.X, 0.5);
            Assert.Equal(10, web.Bounds.Y, 0.5);
            // The link to page 3 of the file leads nowhere here: that page was not drawn.
            Assert.Single(links);
        }

        [Fact]
        public async Task LinkToADrawnPage_LeadsToThePageItWasDrawnOn()
        {
            // Pages 1 and 3 of the file are drawn on the document's pages 2 and 3.
            var pdf = await BuildAsync(3, doc => doc.OverlayFile(layer => layer.File(NavigationFile()).TargetPages("2-3").SourcePages("1,3")));
            var source = PdfPageSource.Open(pdf);

            var link = Assert.Single(source.GetLinks(1), l => l.Destination is not null);

            Assert.Equal(2, link.Destination!.PageIndex);
            Assert.Equal(30, link.Destination.TopPoints, 0.5);
            Assert.Equal(2, source.GetNamedDestinations()["stamp-last"].PageIndex);
        }

        [Fact]
        public async Task Outline_KeepsTheEntriesOfDrawnPages_AndTheirParents()
        {
            // Only page 3 of the file is drawn: "Nested" survives, under "First", whose own page was not drawn.
            var pdf = await BuildAsync(1, doc => doc.OverlayFile(layer => layer.File(NavigationFile()).SourcePages("3")));

            var outline = PdfPageSource.Open(pdf).GetOutline();

            Assert.Equal(["First", "Nested"], outline.Select(o => o.Title));
            Assert.Equal([1, 2], outline.Select(o => o.Level));
            Assert.Null(outline[0].PageIndex);
            Assert.Equal(0, outline[1].PageIndex);
            Assert.Equal(30, outline[1].TopPoints, 0.5);
        }

        [Fact]
        public async Task InformationAndAttachments_FillWhatTheDocumentLacks_AndKeepWhatItHas()
        {
            var generator = new PdfGenerator();
            var document = await generator.CreateDocument(doc =>
            {
                doc.Page(page =>
                {
                    page.Size(200, 200).Margin(0);
                    page.Content(container => container.Text("body"));
                });
                doc.OverlayFile(layer => layer.File(NavigationFile()).SourcePages("1"));
            }, new PdfGenerateConfig { Metadata = new PdfDocumentMetadata { Title = "Own title" } });
            using var ms = new MemoryStream();
            document.Save(ms);

            var source = PdfPageSource.Open(ms.ToArray());

            Assert.Equal("Own title", source.GetMetadata().Title);
            Assert.Equal("Stamp author", source.GetMetadata().Author);
            Assert.Equal("stamp.txt", Assert.Single(source.GetAttachments()).FileName);
        }
    }
}
