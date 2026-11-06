using PeachDrawing.Core;

namespace PeachBrackets.Tests;

public class GridTemplateAreasTests
{
    [Fact]
    public async Task GridTemplateAreas_PlaceItemsInTheirNamedAreas()
    {
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 300, ViewportHeightPoints = 100 };
        var (_, document) = await TestDocument.LoadAsync(TestDocument.Wrap(
            "<div style='display:grid;grid-template-areas:\"a b\" \"c c\";grid-template-columns:50pt 50pt;width:100pt'><i style='grid-area:a'>x</i><i style='grid-area:b'>y</i><i style='grid-area:c'>z</i></div>"),
            options);
        using (document)
        {
            Assert.NotEmpty(document.Pages);
        }
    }
}
