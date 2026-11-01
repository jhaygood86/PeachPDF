using PeachPDF.Rendering;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachDrawing.PDF;
using PeachDrawing.PDF.Engine.Drawing;

namespace PeachBrackets.Engine.Tests.Integration;

/// <summary>
/// <c>PassRewind.RollBackTo</c> resets every child a re-run pass was going to lay out, which on a keep-with-next or widows
/// rewind includes every paragraph layout has not reached yet. Resetting one used to report its geometry as changed, which
/// is also how a box is marked as reached - so the first rewind, a few pages in, marked the whole rest of the document as
/// reached, and from then on each page's emission walked and recursed into all of it: a 1,315-page prose document allocated
/// 24.5GB per render instead of 10GB.
/// </summary>
public class RefillOfUnreachedBoxesTests
{
    private static async Task<HtmlContainerInt> SetUp(string body)
    {
        var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml($"<!DOCTYPE html><html><body>{body}</body></html>", null);
        return container;
    }

    private static CssBox? Find(CssBox box, string id)
    {
        if (box.HtmlTag?.TryGetAttribute("id") == id) return box;
        foreach (var child in box.Boxes)
        {
            if (Find(child, id) is { } found) return found;
        }

        return null;
    }

    [Fact]
    public async Task ResettingABoxLayoutHasNotReached_LeavesItUnreached()
    {
        var container = await SetUp("<p id=a>one</p><p id=b>two</p>");
        var box = Find(container.Root!, "b")!;
        Assert.True(box.NeverTouchedThisLayout);

        box.ResetForRefill();

        Assert.True(box.NeverTouchedThisLayout);
    }

    [Fact]
    public async Task ResettingABoxLayoutHasReached_StillReportsItsGeometryChanged()
    {
        var renderContext = new HtmlRenderContext { PixelsPerPoint = 1.0 };
        var container = new HtmlContainerInt(renderContext);
        await container.SetHtml("<!DOCTYPE html><html><body><p id=a>one</p></body></html>", null);

        var size = new Size(595, 842);
        container.PageSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        container.MaxSize = PeachBrackets.Utilities.Utils.ToPixels(size, 1.0);
        using var graphics = PdfCanvas.CreateMeasuring(renderContext, size);
        await container.PerformLayout(graphics);

        var box = Find(container.Root!, "a")!;
        Assert.False(box.NeverTouchedThisLayout);

        // A reached box carries observations and fragments that a reset has to retire, so it must still notify.
        box.RecordEmittedNothingAt(3, box, 0);
        box.ResetForRefill();

        Assert.False(box.EmittedNothingAtOrBefore(3, box, new PeachBrackets.Html.Fragmentation.InvalidationHistory()));
        Assert.False(box.NeverTouchedThisLayout);
    }
}
