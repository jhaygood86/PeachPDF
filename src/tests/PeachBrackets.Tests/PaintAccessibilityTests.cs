namespace PeachBrackets.Tests;

public class PaintAccessibilityTests
{
    // Every state PaintAccessibility turns into a property, on elements that are on the page.
    private const string Body = """
        <nav aria-label="Sections"><a href="#a" aria-current="page">Jump</a></nav>
        <h2 id="a">Title</h2>
        <input type=checkbox checked aria-label="Agree" aria-required="true">
        <input type=checkbox aria-label="Mixed" aria-checked="mixed" disabled>
        <button aria-pressed="true" aria-expanded="false">Go</button>
        <div role="tab" aria-selected="true" aria-readonly="true" aria-invalid="true" aria-busy="true" aria-modal="true">Tab</div>
        <div role="status" aria-live="assertive">Saved</div>
        <div role="alert" aria-live="polite">Careful</div>
        <ul><li>one</li><li>two</li></ul>
        <p lang="fr" title="Longer">Bonjour</p>
        """;

    [Fact]
    public async Task APageWithEveryKindOfStateCanBePaintedOntoACanvasThatIgnoresIt_AndTheRightPageIsChecked()
    {
        var (context, document) = await TestDocument.LoadAsync(TestDocument.Wrap(Body), new HtmlDocumentOptions { MediaType = MediaType.Screen });
        using var canvas = context.CreateCanvas(300, 400);

        document.PaintAccessibility(0, canvas);

        Assert.Throws<ArgumentOutOfRangeException>(() => document.PaintAccessibility(5, canvas));
        Assert.Throws<ArgumentNullException>(() => document.PaintAccessibility(0, null!));
    }
}
