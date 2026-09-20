using PeachPDF.Tests.TestSupport;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// An HTML attribute written without a value has the empty string as its value, not no value at
    /// all — so it is visible to <c>attr()</c> and matched by an <c>[attr]</c> presence selector
    /// (<a href="https://www.w3.org/TR/selectors-4/#attribute-representation">Selectors 4 §6.1</a>,
    /// which tests for the attribute, never for a value). The tokenizer reports such an attribute with
    /// a null value and <c>HtmlParser.ParseHtmlTag</c> used to store that null, which made every
    /// boolean attribute in the language invisible to its own selector while the identical attribute
    /// written as <c>disabled=""</c> matched.
    /// </summary>
    public class ValuelessAttributeIntegrationTests
    {
        [Theory]
        [InlineData("disabled")]
        [InlineData("hidden")]
        [InlineData("noshade")]
        [InlineData("required")]
        public async Task ValuelessAttribute_IsMatchedByItsPresenceSelector(string attribute)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>"
                + $"[{attribute}] {{ color: rgb(1, 2, 3) }}"
                + $"</style></head><body><p id='el' {attribute}>x</p></body></html>");

            Assert.Equal("rgb(1, 2, 3)", LayoutHarness.FindById(root, "el")!.Color);
        }

        [Fact]
        public async Task ValuelessAttribute_ReadsAsTheEmptyString()
        {
            // The same fact from the other side: the attribute is present and its value is "", which is
            // what an equality selector against the empty string and attr() both have to see.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>"
                + "[data-flag=''] { color: rgb(1, 2, 3) }"
                + "</style></head><body><p id='el' data-flag>x</p></body></html>");

            var box = LayoutHarness.FindById(root, "el")!;

            Assert.Equal("", box.HtmlTag!.TryGetAttribute("data-flag", "<absent>"));
            Assert.Equal("rgb(1, 2, 3)", box.Color);
        }

        [Theory]
        // Each of these threw before the parser stored the empty string: an ArgumentNullException from
        // the inline-style reparse, a NullReferenceException from a presentational-hint translator
        // dereferencing the value, or an HtmlRenderException wrapping one from inside layout.
        [InlineData("<p style>x</p>")]
        [InlineData("<p bgcolor>x</p>")]
        [InlineData("<p background>x</p>")]
        [InlineData("<div align>x</div>")]
        [InlineData("<body bgcolor>x</body>")]
        [InlineData("<font color>x</font>")]
        [InlineData("<font size>x</font>")]
        [InlineData("<font face>x</font>")]
        [InlineData("<img height>")]
        [InlineData("<img hspace>")]
        [InlineData("<img vspace>")]
        [InlineData("<hr size>")]
        [InlineData("<hr align>")]
        [InlineData("<hr color>")]
        [InlineData("<input type>")]
        [InlineData("<table cellspacing><tr><td>x</td></tr></table>")]
        [InlineData("<table bordercolor><tr><td>x</td></tr></table>")]
        [InlineData("<table><tr><td bgcolor>x</td></tr></table>")]
        [InlineData("<table><tr><td align>x</td></tr></table>")]
        [InlineData("<table><tr><td valign>x</td></tr></table>")]
        [InlineData("<table><tr><td width>x</td></tr></table>")]
        [InlineData("<table><tr><td height>x</td></tr></table>")]
        public async Task ValuelessAttribute_OnAnElementThatConsumesIt_DoesNotThrow(string markup)
        {
            // The null was only reachable through an element that actually READS the attribute - the
            // same attribute on a box that ignores it parses fine, which is what makes this easy to
            // probe for and miss. Every case here is one that threw on the release before the fix.
            var exception = await Record.ExceptionAsync(async () => await LayoutHarness.LayoutAsync(
                $"<!DOCTYPE html><html><body>{markup}</body></html>"));

            Assert.Null(exception);
        }

        [Fact]
        public async Task AbsentAttribute_StillDoesNotMatch()
        {
            // The guard on the fix: "" is not the same as absent, so a presence selector must still
            // reject an element that never carried the attribute at all.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>"
                + "[disabled] { color: rgb(1, 2, 3) }"
                + "</style></head><body><p id='el'>x</p></body></html>");

            Assert.NotEqual("rgb(1, 2, 3)", LayoutHarness.FindById(root, "el")!.Color);
        }
    }
}
