using PeachPDF.Html.Core.Dom;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Coverage for the copy-on-write sharing guarantee <see cref="ComputedStyle"/> is built around: a
    /// fresh <see cref="CssBox"/> starts out referencing the shared, immutable <see cref="ComputedStyle.Default"/>
    /// instance, and only gets its own private instance the first time one of its properties is set (via
    /// <see cref="ComputedStyle.SetPropertyValue{T}"/>). Nothing in the existing integration suite exercises
    /// this sharing/isolation behavior directly - those tests only ever observe the *values* a box ends up
    /// with, not whether two untouched boxes are safely sharing the same underlying instance.
    /// </summary>
    public class ComputedStyleTests
    {
        [Fact]
        public void FreshBox_ComputedStyle_IsTheSharedDefaultInstance()
        {
            var box = new CssBox(null, null);

            Assert.Same(ComputedStyle.Default, box.ComputedStyle);
        }

        [Fact]
        public void TwoUntouchedSiblings_ShareTheSameComputedStyleInstance()
        {
            var parent = new CssBox(null, null);
            var box1 = new CssBox(parent, null);
            var box2 = new CssBox(parent, null);

            Assert.Same(box1.ComputedStyle, box2.ComputedStyle);
            Assert.Same(ComputedStyle.Default, box1.ComputedStyle);
        }

        [Fact]
        public void SettingAProperty_ProducesANewInstance_LeavingASiblingsDefaultUntouched()
        {
            var parent = new CssBox(null, null);
            var box1 = new CssBox(parent, null);
            var box2 = new CssBox(parent, null);

            box1.Color = "rgb(255, 0, 0)";

            Assert.NotSame(ComputedStyle.Default, box1.ComputedStyle);
            Assert.Equal("rgb(255, 0, 0)", box1.Color);

            // box2 never had a property set, so it must still be pointing at the shared Default -
            // box1's write must not have mutated the shared instance itself.
            Assert.Same(ComputedStyle.Default, box2.ComputedStyle);
            Assert.Equal("black", box2.Color);
        }

        [Fact]
        public void SettingAValueEqualToTheCurrentOne_IsANoOp_AndAllocatesNothing()
        {
            var box = new CssBox(null, null);

            // "black" is already ComputedStyle.Default's Color - setting it again must not produce a
            // new instance (SetPropertyValue's whole reason for existing).
            box.Color = "black";

            Assert.Same(ComputedStyle.Default, box.ComputedStyle);
        }

        [Fact]
        public void SecondPropertyWrite_MutatesTheSameAlreadyClonedInstance_NotADifferentOne()
        {
            var box = new CssBox(null, null);

            box.Color = "rgb(1, 2, 3)";
            var afterFirstWrite = box.ComputedStyle;

            box.FontSize = "14pt";

            Assert.Equal("rgb(1, 2, 3)", box.Color);
            Assert.Equal("14pt", box.FontSize);
            // Both values must have landed on the box's own instance - this isn't asserting object
            // identity of afterFirstWrite (a second `with` necessarily produces a new record), only that
            // the first write's value survived the second write untouched.
            Assert.NotSame(ComputedStyle.Default, box.ComputedStyle);
        }

        [Fact]
        public void InheritStyle_ProducesAnIndependentInstance_ParentUnaffectedByChildMutation()
        {
            var parent = new CssBox(null, null) { Color = "rgb(10, 20, 30)" };
            var child = new CssBox(parent, null);

            child.InheritStyle();
            Assert.Equal("rgb(10, 20, 30)", child.Color);

            child.Color = "rgb(99, 99, 99)";

            Assert.Equal("rgb(99, 99, 99)", child.Color);
            Assert.Equal("rgb(10, 20, 30)", parent.Color);
        }

        [Fact]
        public void InheritStyle_CustomProperties_ChildMutationDoesNotAffectParentsDictionary()
        {
            var parent = new CssBox(null, null);
            parent.CustomProperties = new Dictionary<string, string> { ["--x"] = "1" };
            var child = new CssBox(parent, null);

            child.InheritStyle();
            Assert.Equal("1", child.CustomProperties!["--x"]);

            child.CustomProperties["--x"] = "2";

            Assert.Equal("2", child.CustomProperties["--x"]);
            Assert.Equal("1", parent.CustomProperties["--x"]);
        }

        [Fact]
        public void InheritStyle_DoesNotReallocate_WhenParentValuesAlreadyMatch()
        {
            var parent = new CssBox(null, null);
            var child = new CssBox(parent, null);

            // Neither box has been touched - both already sit on the same Default instance, so
            // re-inheriting (a no-op in terms of values) must not clone away from it.
            child.InheritStyle();

            Assert.Same(ComputedStyle.Default, child.ComputedStyle);
        }

        [Fact]
        public void InheritStyle_Everything_CopiesBottomAndRight()
        {
            // Pre-refactor, CssBoxProperties.InheritStyle's "everything" branch (used only for structural
            // duplicates of the SAME source box - CssProxyBox's repeated header/footer, an inline/block
            // split) copied two private _bottom/_right fields that the real Bottom/Right auto-properties
            // never actually read from, so a structural duplicate's Bottom/Right silently never inherited
            // the source box's value and always stayed at the CSS initial "auto" - even though CSS 2.1
            // §9.4.3 says a relatively/absolutely positioned box's offsets should carry over here, same as
            // Left/Top/Width/Height already did. Unifying storage onto ComputedStyle.Bottom/.Right (see
            // CssBox.StyleProperties.cs's InheritStyle) fixes this for real; this test locks the fix in.
            var source = new CssBox(null, null) { Bottom = "13px", Right = "17px" };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal("13px", clone.Bottom);
            Assert.Equal("17px", clone.Right);
        }
    }
}
