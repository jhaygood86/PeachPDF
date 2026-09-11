using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.CSS;

/// <summary>
/// Generation-time support for HTML's <c>:placeholder-shown</c> state and the detached
/// <c>::placeholder</c> style used by interactive PDF text-field appearances.
/// </summary>
public class PlaceholderSelectorTests
{
    private const string Blue = "rgb(0, 0, 255)";

    [Theory]
    [InlineData("<input id='field' placeholder='hint'>", true)]
    [InlineData("<input id='field' placeholder='hint' value=''>", true)]
    [InlineData("<input id='field' placeholder=''>", true)]
    [InlineData("<input id='field' type='not-a-real-type' placeholder='hint'>", true)]
    [InlineData("<input id='field' placeholder='hint' value='filled'>", false)]
    [InlineData("<input id='field'>", false)]
    [InlineData("<input id='field' type='number' placeholder='hint'>", true)]
    [InlineData("<input id='field' type='checkbox' placeholder='hint'>", false)]
    [InlineData("<select id='field' placeholder='hint'><option>One</option></select>", false)]
    public async Task PlaceholderShown_MatchesTheStandardGenerationTimeState(string control, bool shouldMatch)
    {
        var box = await Box(Html("#field:placeholder-shown { color: #0000ff; }", control));

        Assert.Equal(shouldMatch, box.Color == Blue);
    }

    [Fact]
    public async Task PlaceholderShown_StylesTheFieldItself()
    {
        var box = await Box(Html(
            "input { border-color: #ff0000; } input:placeholder-shown { border-color: #0000ff; }",
            "<input id='field' placeholder='hint'>"));

        Assert.Equal(0, box.ActualBorderTopColor.R);
        Assert.Equal(0, box.ActualBorderTopColor.G);
        Assert.Equal(255, box.ActualBorderTopColor.B);
    }

    [Fact]
    public async Task PlaceholderPseudoElement_UsesTheNormalCascadeWithoutEnteringLayout()
    {
        var box = await Box(Html(
            "input::placeholder { color: #123456; font-style: italic; opacity: .4; }",
            "<input id='field' placeholder='hint'>"));

        var placeholder = Assert.IsType<CssBox>(box.ResolvedPlaceholderStyle);
        Assert.Equal("rgb(18, 52, 86)", placeholder.Color);
        Assert.Equal("italic", placeholder.FontStyle);
        Assert.Equal(0.4, placeholder.ActualOpacity, 3);
        Assert.DoesNotContain(box.Boxes, child => child.IsPlaceholderPseudoElement);
    }

    [Fact]
    public async Task PlaceholderPseudoElement_ComposesWithPlaceholderShown()
    {
        var box = await Box(Html(
            "input::placeholder { color: #ff0000; } input:placeholder-shown::placeholder { color: #0000ff; }",
            "<input id='field' placeholder='hint'>"));

        Assert.Equal(Blue, box.ResolvedPlaceholderStyle!.Color);
    }

    [Fact]
    public async Task PlaceholderPseudoElement_IsAbsentWhenTheControlHasAValue()
    {
        var box = await Box(Html(
            "input::placeholder { color: #0000ff; }",
            "<input id='field' placeholder='hint' value='filled'>"));

        Assert.Null(box.ResolvedPlaceholderStyle);
    }

    private static string Html(string css, string control) =>
        $"<!DOCTYPE html><html><head><style>{css}</style></head><body>{control}</body></html>";

    private static async Task<CssBox> Box(string html)
    {
        var container = new HtmlContainerInt(new PdfSharpAdapter());
        await container.SetHtml(html, null);

        var box = DomUtils.GetBoxById(container.Root!, "field");
        return Assert.IsType<CssBoxFormField>(box);
    }
}
