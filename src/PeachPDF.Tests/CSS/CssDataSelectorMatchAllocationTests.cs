using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// <c>CssData.DoesSelectorMatch(ListSelector, ICssDomNode?)</c>/<c>DoesSelectorMatch(CompoundSelector,
/// ICssDomNode?)</c> run once per box per candidate rule - the hottest path in the whole cascade. Both
/// used to enumerate through <c>ListSelector</c>/<c>CompoundSelector</c> via LINQ's <c>Any</c>/<c>All</c>,
/// which allocates a boxed <c>IEnumerator&lt;ISelector&gt;</c> and a closure per call, because
/// <c>Selectors</c> only implements <c>IEnumerable&lt;ISelector&gt;</c>, not <c>IList&lt;ISelector&gt;</c>
/// (see issue #971).
/// </summary>
/// <remarks>
/// Asserted by calling the (now internal) matching methods directly against a synthetic selector and a
/// real fixture box, rather than through a rendered document or the cascade
/// (<c>GetUserAgentStyleRules</c>/<c>GatherMatchedRules</c>). Measured end to end, the saving is real but
/// not separable: it is diffuse (roughly a fixed cost per box, not per rule or per selector complexity),
/// so any threshold built from document/box/rule counts is either loose or wrong depending on what else a
/// given document allocates (font loading, layout, per-document setup). Called directly, with a fixture
/// engineered so the only thing that can allocate is the loop itself, the assertion is exact and the same
/// everywhere - the same approach as <c>GposPositionerAllocationTests</c> for the identical
/// enumerator-through-an-interface problem in the GPOS positioner.
/// </remarks>
public class CssDataSelectorMatchAllocationTests
{
    [Fact]
    public async Task ListSelectorAny_AllocatesNothingPerCall()
    {
        var box = await BuildDivFixture();

        // None of these match a <div>, so Any's loop runs to completion every call rather than
        // short-circuiting on the first alternative.
        var list = new ListSelector();
        list.Add(TypeSelector.Create("span"));
        list.Add(TypeSelector.Create("p"));
        list.Add(TypeSelector.Create("a"));
        list.Add(TypeSelector.Create("ul"));
        list.Add(TypeSelector.Create("table"));

        // Warm: JIT the path and build/cache MatchOrder before anything is counted.
        for (var i = 0; i < 3; i++) CssData.DoesSelectorMatch(list, box);

        const int passes = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < passes; i++) CssData.DoesSelectorMatch(list, box);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4096,
            $"matching a 5-alternative ListSelector allocated {allocated} bytes over {passes:N0} calls. "
            + "It should allocate nothing: Selectors is IEnumerable-only, so LINQ's Any() boxes an "
            + "enumerator and allocates a closure on every call.");
    }

    [Fact]
    public async Task CompoundSelectorAll_PlainBranch_AllocatesNothingPerCall()
    {
        var box = await BuildDivFixture();

        // All of these match a <div>, so All's loop runs to completion every call rather than
        // short-circuiting on the first member. Deliberately all TypeSelector (rather than mixing in,
        // say, ClassSelector): ClassSelector's own match allocates a Split() array regardless of this
        // fix, which would swamp the signal this test is isolating - TypeSelector's match is a plain
        // string compare with nothing else to allocate.
        var compound = new CompoundSelector();
        compound.Add(TypeSelector.Create("div"));
        compound.Add(TypeSelector.Create("div"));
        compound.Add(TypeSelector.Create("div"));
        compound.Add(TypeSelector.Create("div"));

        for (var i = 0; i < 3; i++) CssData.DoesSelectorMatch(compound, box);

        const int passes = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < passes; i++) CssData.DoesSelectorMatch(compound, box);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4096,
            $"matching a 4-member CompoundSelector allocated {allocated} bytes over {passes:N0} calls. "
            + "It should allocate nothing: Selectors is IEnumerable-only, so LINQ's All() boxes an "
            + "enumerator and allocates a closure on every call.");
    }

    [Fact]
    public async Task CompoundSelectorAll_PseudoElementBranch_AllocatesNothingPerCall()
    {
        var box = await BuildDivFixture();

        // The trailing PseudoElementSelector is handled separately (CssData.DoesSelectorMatch's
        // pseudo-element branch); ::first-letter is used here (rather than ::before/::after/::marker)
        // because its side effect is an idempotent bool flag set, not box-tree synthesis, so it stays
        // safe to call thousands of times in a tight loop without mutating the fixture's box tree.
        var compound = new CompoundSelector();
        compound.Add(TypeSelector.Create("div"));
        compound.Add(PseudoElementSelector.Create(PseudoElementNames.FirstLetter));

        for (var i = 0; i < 3; i++) CssData.DoesSelectorMatch(compound, box);

        const int passes = 10_000;
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i = 0; i < passes; i++) CssData.DoesSelectorMatch(compound, box);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        Assert.True(allocated < 4096,
            $"matching a compound selector ending in ::first-letter allocated {allocated} bytes over "
            + $"{passes:N0} calls. It should allocate nothing: the non-pseudo-element members were "
            + "filtered with Where().All(), which boxes an enumerator and allocates a closure on every "
            + "call in addition to the Where iterator itself.");
    }

    private static async Task<CssBox> BuildDivFixture()
    {
        var adapter = new PdfSharpAdapter();
        var container = new HtmlContainerInt(adapter);
        await container.SetHtml("<!DOCTYPE html><html><body><div class='a b c'>text</div></body></html>", null);

        var size = new XSize(595, 842);
        container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
        container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

        var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
        using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
        await container.PerformLayout(graphics);

        var box = DomUtils.GetBoxByTagName(container.Root!, "div");
        Assert.NotNull(box);
        return box!;
    }
}
