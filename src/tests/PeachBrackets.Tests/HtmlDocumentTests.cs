using PeachBrackets.Annotations;
using PeachBrackets.Tagging;
using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class HtmlDocumentTests
{
    [Fact]
    public async Task ABlock_IsPaintedWhereLayoutPutIt_InsideTheMargins()
    {
        var options = new HtmlDocumentOptions();
        options.SetMargins(20);
        var (context, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div style='background:rgb(255,0,0); width:100pt; height:50pt'></div>"), options);

        var page = Assert.Single(document.Pages);
        Assert.Equal(595, page.WidthPoints);
        Assert.Equal(842, page.HeightPoints);

        using var canvas = await TestDocument.PaintAsync(context, document);

        // Inside the box, 20pt in from the top-left corner of the sheet...
        Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(canvas, 30, 30));
        Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(canvas, 118, 68));
        // ...and white in the margin and beyond the box.
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, 10, 10));
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, 125, 30));
        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(canvas, 30, 75));
    }

    [Fact]
    public async Task Text_IsDrawnWithTheRegisteredFont()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p style='font-size:30pt'>Hello</p>"));
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.True(TestDocument.HasInk(canvas, 20, 20, 150, 60), "the text left no ink where the paragraph is");
        Assert.False(TestDocument.HasInk(canvas, 300, 300, 100, 100), "ink appeared where there is no content");
    }

    [Fact]
    public async Task LongContent_BreaksIntoSeveralPages_AndEachPageIsPaintable()
    {
        var body = string.Concat(Enumerable.Range(0, 6).Select(i =>
            $"<div style='height:300pt; background:rgb({40 * i}, 0, 0)'></div>"));
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(body));

        Assert.True(document.Pages.Count >= 3, $"expected several pages, got {document.Pages.Count}");
        Assert.Equal(Enumerable.Range(0, document.Pages.Count), document.Pages.Select(p => p.Index));

        for (var i = 1; i < document.Pages.Count; i++)
        {
            using var canvas = await TestDocument.PaintAsync(context, document, i);
            Assert.True(TestDocument.HasInk(canvas, 20, 20, 100, 100), $"page {i} is blank");
        }
    }

    [Fact]
    public async Task AnAtPageSizeRule_ReplacesTheConfiguredSheet()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<p>x</p>", "<style>@page { size: 300pt 200pt; margin: 10pt }</style>"));

        var page = Assert.Single(document.Pages);
        Assert.Equal(300, page.WidthPoints);
        Assert.Equal(200, page.HeightPoints);
    }

    [Fact]
    public async Task TheConfiguredPageSize_IsTheSheet()
    {
        var options = new HtmlDocumentOptions { PageWidthPoints = 400, PageHeightPoints = 300 };
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), options);

        var page = Assert.Single(document.Pages);
        Assert.Equal(400, page.WidthPoints);
        Assert.Equal(300, page.HeightPoints);
    }

    [Fact]
    public async Task ThePreferredColorScheme_DecidesWhichMediaQueryApplies()
    {
        const string head = "<style>div { width: 50pt; height: 50pt; background: rgb(255,0,0) } " +
                            "@media (prefers-color-scheme: dark) { div { background: rgb(0,0,255) } }</style>";

        var (lightContext, light) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head));
        using var lightCanvas = await TestDocument.PaintAsync(lightContext, light);
        Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(lightCanvas, 30, 30));

        var (darkContext, dark) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head),
            new HtmlDocumentOptions { ColorScheme = ColorSchemePreference.Dark });
        using var darkCanvas = await TestDocument.PaintAsync(darkContext, dark);
        Assert.Equal((0, 0, 255, 255), TestDocument.Pixel(darkCanvas, 30, 30));
    }

    [Fact]
    public async Task TheMediaOption_SelectsTheStylesheetEnvironment()
    {
        const string head = "<style>@media print { div { background: rgb(255,0,0) } } @media screen { div { background: rgb(0,255,0) } } " +
                            "div { width: 50pt; height: 50pt }</style>";

        var (printContext, print) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head));
        using var printCanvas = await TestDocument.PaintAsync(printContext, print);
        Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(printCanvas, 30, 30));

        var (screenContext, screen) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head), new HtmlDocumentOptions { Media = "screen" });
        using var screenCanvas = await TestDocument.PaintAsync(screenContext, screen);
        Assert.Equal((0, 255, 0, 255), TestDocument.Pixel(screenCanvas, 30, 30));
    }

    [Fact]
    public async Task AUserStyleSheet_AppliesWhenAuthorStylesAreIgnored()
    {
        const string head = "<style>div { width: 50pt; height: 50pt; background: rgb(255,0,0) }</style>";

        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head),
            new HtmlDocumentOptions { UserStyleSheet = "div { width: 50pt; height: 50pt; background: rgb(0,0,255) }", IgnoreAuthorStyleSheets = true });
        using var canvas = await TestDocument.PaintAsync(context, document);

        var (r, _, b, _) = TestDocument.Pixel(canvas, 30, 30);
        Assert.Equal(0, r);
        Assert.Equal(255, b);
    }

    [Fact]
    public async Task MetadataAndLanguage_AreReadFromTheDocument()
    {
        var (_, document) = await TestDocument.LoadAsync(
            "<!DOCTYPE html><html lang='fr'><head><title>Le titre</title><meta name='author' content='Jane Doe'></head><body><p>x</p></body></html>");

        Assert.Equal("fr", document.Language);
        Assert.Equal("Le titre", document.Metadata?.Title);
        Assert.Equal("Jane Doe", document.Metadata?.Author);
    }

    [Fact]
    public async Task TheDefaultLanguage_FillsInWhenTheDocumentDeclaresNone()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), new HtmlDocumentOptions { DefaultLanguage = "de" });

        Assert.Equal("de", document.Language);
    }

    [Fact]
    public async Task ElementBounds_AreReportedInPoints()
    {
        var (_, document) = await TestDocument.LoadAsync(
            TestDocument.Wrap("<div id='box' style='width:100pt; height:50pt'></div>"));

        var bounds = document.GetElementBounds("box");
        Assert.NotNull(bounds);
        Assert.Equal(100, bounds!.Value.Width, 1);
        Assert.Equal(50, bounds.Value.Height, 1);
        Assert.Null(document.GetElementBounds("missing"));
    }

    [Fact]
    public async Task TextCutOffByAnOverflowClip_IsReported()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<div style='width:30pt; overflow:hidden; white-space:nowrap; font-size:20pt'>A very long unbroken line of text</div>"));
        using var canvas = await TestDocument.PaintAsync(context, document);

        Assert.NotEmpty(document.ClippedText);
        Assert.All(document.ClippedText, entry => Assert.True(entry.VisibleWidth < entry.DrawnWidth || entry.VisibleHeight < entry.DrawnHeight));
    }

    [Fact]
    public async Task TheStructureSink_IsToldAboutTheContentThatIsPainted()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<h1>Title</h1><p>Body</p>"), layout: false);
        var sink = new RecordingSink();
        document.StructureTagSink = sink;
        using (var measuring = context.CreateCanvas(1, 1))
        {
            await document.LayoutAsync(measuring);
        }

        using var canvas = await TestDocument.PaintAsync(context, document);

        // Headings and paragraphs group the text boxes inside them; the text itself is the content.
        Assert.Contains("H1", sink.GroupTypes);
        Assert.Contains("P", sink.GroupTypes);
        Assert.NotEmpty(sink.ContentTypes);
        Assert.Equal(0, sink.Open);
    }

    [Fact]
    public async Task FormControls_AreCollectedOnlyWhenAskedFor()
    {
        const string body = "<input name='email' value='a@b.com'><input type='checkbox' name='agree' checked>";

        var (_, without) = await TestDocument.LoadAsync(TestDocument.Wrap(body));
        Assert.Empty(without.Annotations.FormFields);

        var (_, with) = await TestDocument.LoadAsync(TestDocument.Wrap(body), new HtmlDocumentOptions { CollectFormFields = true });
        var fields = with.Annotations.FormFields;
        Assert.Equal(2, fields.Count);

        var text = fields.Single(f => f.Classification.Kind == FormFieldKind.Text);
        Assert.Equal("email", text.Classification.Name);
        Assert.Equal("a@b.com", text.Classification.Value);
        Assert.Equal(0, text.PageIndex);
        Assert.True(text.Bounds.Width > 0 && text.Bounds.Height > 0);

        var box = fields.Single(f => f.Classification.Kind == FormFieldKind.Checkbox);
        Assert.True(box.Classification.Checked);
    }

    [Fact]
    public async Task ALinkAndAHeading_AppearInTheAnnotations()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<h1>Chapter</h1><p><a href='https://example.com/docs'>read</a> and <a href='#end'>jump</a></p><p id='end'>The end</p>"));

        var annotations = document.Annotations;

        var external = Assert.Single(annotations.Links, l => l.Uri == "https://example.com/docs");
        Assert.Equal(0, external.PageIndex);
        Assert.True(external.Bounds.Width > 0);

        var internalLink = Assert.Single(annotations.Links, l => l.Anchor == "end");
        Assert.NotNull(internalLink.Destination);
        Assert.Equal(0, internalLink.Destination!.Value.PageIndex);

        var bookmark = Assert.Single(annotations.Bookmarks);
        Assert.Equal("Chapter", bookmark.Title);
        Assert.Equal(1, bookmark.Level);
        Assert.NotNull(bookmark.Destination);
    }

    [Fact]
    public async Task ThePagesAreUnavailable_UntilTheDocumentIsLaidOut()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"), layout: false);

        Assert.Throws<InvalidOperationException>(() => document.Pages);
        Assert.Throws<InvalidOperationException>(() => document.Annotations);
        using var canvas = context.CreateCanvas(10, 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => document.PaintPageAsync(0, canvas));

        await document.LayoutAsync(canvas);
        await Assert.ThrowsAsync<InvalidOperationException>(() => document.LayoutAsync(canvas));
    }

    [Fact]
    public async Task APageThatDoesNotExist_IsRejected()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"));
        using var canvas = context.CreateCanvas(10, 10);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.PaintPageAsync(-1, canvas));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => document.PaintPageAsync(1, canvas));
    }

    [Fact]
    public async Task ADisposedDocument_RefusesFurtherUse()
    {
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap("<p>x</p>"));

        document.Dispose();
        document.Dispose();

        Assert.Throws<ObjectDisposedException>(() => document.Pages);
    }

    [Fact]
    public async Task Arguments_AreValidated()
    {
        var context = await TestDocument.NewContextAsync();

        await Assert.ThrowsAsync<ArgumentNullException>(() => HtmlDocument.LoadAsync(null!, context));
        await Assert.ThrowsAsync<ArgumentNullException>(() => HtmlDocument.LoadAsync("<p>x</p>", null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new HtmlDocumentOptions { AnimationProgress = 1.5 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new HtmlDocumentOptions { AnimationProgress = double.NaN });
        Assert.Null(new HtmlDocumentOptions().AnimationProgress);
    }

    [Fact]
    public async Task AnimationProgress_PaintsAnotherFrame()
    {
        const string head = "<style>@keyframes grow { from { width: 20pt } to { width: 100pt } } " +
                            "div { height: 20pt; background: rgb(255,0,0); animation: grow 1s linear forwards }</style>";

        var (startContext, start) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head), new HtmlDocumentOptions { AnimationProgress = 0 });
        using var startCanvas = await TestDocument.PaintAsync(startContext, start);
        var (endContext, end) = await TestDocument.LoadAsync(TestDocument.Wrap("<div></div>", head), new HtmlDocumentOptions { AnimationProgress = 1 });
        using var endCanvas = await TestDocument.PaintAsync(endContext, end);

        Assert.Equal((255, 255, 255, 255), TestDocument.Pixel(startCanvas, 90, 30));
        Assert.Equal((255, 0, 0, 255), TestDocument.Pixel(endCanvas, 90, 30));
    }

    private sealed class RecordingSink : IStructureTagSink
    {
        public List<string> ContentTypes { get; } = [];

        public List<string> GroupTypes { get; } = [];

        public int Open { get; private set; }

        public bool IsInMarkedContent => Open > 0;

        public IDisposable OpenGroupingElement(StructureNode node, string structureType)
        {
            GroupTypes.Add(structureType.TrimStart('/'));
            return new Scope(this, track: false);
        }

        public IDisposable OpenPhantomAncestors(IReadOnlyList<PhantomAncestor> ancestors) => new Scope(this, track: false);

        public IDisposable OpenListItemBodyElement(StructureNode node) => new Scope(this, track: false);

        public IDisposable OpenContentElement(Canvas g, StructureNode node, string structureType, string? altText = null)
        {
            ContentTypes.Add(structureType.TrimStart('/'));
            return new Scope(this, track: true);
        }

        public void AttachMathMlSource(StructureNode node, string mathMlSource)
        {
        }

        public IDisposable OpenArtifact(Canvas g) => new Scope(this, track: true);

        private sealed class Scope : IDisposable
        {
            private readonly RecordingSink _sink;
            private readonly bool _track;

            public Scope(RecordingSink sink, bool track)
            {
                _sink = sink;
                _track = track;
                if (track)
                    sink.Open++;
            }

            public void Dispose()
            {
                if (_track)
                    _sink.Open--;
            }
        }
    }
}
