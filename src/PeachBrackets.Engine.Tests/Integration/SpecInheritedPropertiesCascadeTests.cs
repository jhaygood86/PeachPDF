using PeachBrackets.Html.Dom;
using System.Threading.Tasks;
using static PeachBrackets.Engine.Tests.TestSupport.LayoutHarness;

namespace PeachBrackets.Engine.Tests.Integration
{
    /// <summary>
    /// Properties the specs define as Inherited: yes whose inheritance the registry/CSS-OM disagreed about, or
    /// whose storage area did not inherit. A child with no declaration of its own takes the parent's value.
    /// </summary>
    public class SpecInheritedPropertiesCascadeTests
    {
        [Fact]
        public async Task TextUnderlineOffset_IsInherited()
        {
            // css-text-decor-4 section 2.8: Inherited: yes.
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='p' style='text-underline-offset:5pt'><span id='c'>x</span></div>"));

            var parent = FindById(root, "p")!;
            var child = FindById(root, "c")!;

            Assert.Equal("5pt", parent.TextUnderlineOffset.ToString());
            Assert.Equal(parent.TextUnderlineOffset.ToString(), child.TextUnderlineOffset.ToString());
        }

        [Fact]
        public async Task TextUnderlineOffset_IsAutoWhenNothingIsDeclared()
        {
            var (root, _) = await LayoutAsync(Wrap("<div id='p'><span id='c'>x</span></div>"));

            Assert.Equal("auto", FindById(root, "c")!.TextUnderlineOffset.ToString());
        }

        [Fact]
        public async Task ImageRenderingAndOrientation_AreInherited()
        {
            var (root, _) = await LayoutAsync(Wrap(
                "<div id='p' style='image-rendering:pixelated;image-orientation:none'><span id='c'>x</span></div>"));

            var parent = FindById(root, "p")!;
            var child = FindById(root, "c")!;

            Assert.Equal(parent.ImageRendering.ToString(), child.ImageRendering.ToString());
            Assert.Equal(parent.ImageOrientation.ToString(), child.ImageOrientation.ToString());
            Assert.NotEqual("auto", parent.ImageRendering.ToString());
        }
    }
}
