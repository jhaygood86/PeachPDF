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
