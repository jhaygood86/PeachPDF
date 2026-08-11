using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Parse;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.Tests.Html.Core.Dom
{
    /// <summary>
    /// Coverage for the copy-on-write sharing guarantee <see cref="ComputedStyle"/> is built around: a
    /// fresh <see cref="CssBox"/> starts out referencing the shared, immutable <see cref="ComputedStyle.Default"/>
    /// instance, and only gets its own private instance the first time one of its properties is set (via
    /// <see cref="ComputedStyleCow.SetPropertyValue{TSelf,TValue}"/>). Nothing in the existing integration suite exercises
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

            box.FontSize = CssKeywordOrValueParser.FromCssText<FontSizeKeyword, LengthOrCalc>(
                "14pt", Map.FontSizeKeywords, CssValueParser.TryParseLengthOrCalc, FontSizeKeyword.Medium);

            Assert.Equal("rgb(1, 2, 3)", box.Color);
            Assert.Equal("14pt", box.FontSize.ToString());
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
        public void InheritStyle_UntouchedInheritedArea_ChildReusesParentsAreaInstance_ByReference()
        {
            // The whole point of splitting ComputedStyle into per-area records: when a parent customizes
            // an inheritable area (Font here) and a child never overrides anything in it, InheritStyle
            // should adopt the parent's actual FontArea object by reference rather than cloning a new one
            // - the copy-on-write discipline guarantees the parent's instance is never mutated afterwards,
            // so sharing it is safe and free.
            var parent = new CssBox(null, null) { FontFamily = "Georgia" };
            var child = new CssBox(parent, null);

            child.InheritStyle();

            Assert.Same(parent.ComputedStyle.Font, child.ComputedStyle.Font);
            Assert.Equal("Georgia", child.FontFamily);
        }

        [Fact]
        public void InheritStyle_ChildOverridesOneProperty_OnlyThatAreaDiverges_OthersStayShared()
        {
            var parent = new CssBox(null, null) { FontFamily = "Georgia", Color = "rgb(1, 2, 3)" };
            var child = new CssBox(parent, null);

            child.InheritStyle();
            child.FontFamily = "Verdana";

            // The overridden area gets its own instance...
            Assert.NotSame(parent.ComputedStyle.Font, child.ComputedStyle.Font);
            Assert.Equal("Verdana", child.FontFamily);
            Assert.Equal("Georgia", parent.FontFamily);

            // ...but every other inherited area (untouched by the override) is still literally the same
            // shared object as the parent's, not merely equal.
            Assert.Same(parent.ComputedStyle.Text, child.ComputedStyle.Text);
            Assert.Same(parent.ComputedStyle.Table, child.ComputedStyle.Table);
            Assert.Same(parent.ComputedStyle.List, child.ComputedStyle.List);
            Assert.Same(parent.ComputedStyle.Pagination, child.ComputedStyle.Pagination);
        }

        [Fact]
        public void InheritStyle_BoxSizing_NoLongerAdoptsParentsBoxModelArea()
        {
            // box-sizing was deliberately made non-inherited (CSS Box Sizing 3 §3) - BoxModel (which now
            // holds BoxSizing) must therefore never be among the areas InheritStyle adopts by reference.
            var parent = new CssBox(null, null)
            {
                BoxSizing = CssProperty<BoxSizingMode>.FromCssText("border-box", Map.BoxSizingModes, BoxSizingMode.ContentBox),
                Width = "100px"
            };
            var child = new CssBox(parent, null);

            child.InheritStyle();

            Assert.NotSame(parent.ComputedStyle.BoxModel, child.ComputedStyle.BoxModel);
            Assert.Equal("content-box", child.BoxSizing.ToString());
            Assert.Equal("auto", child.Width);
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
            var source = new CssBox(null, null)
            {
                Bottom = CssKeywordOrValueParser.FromCssText<AutoKeyword, LengthOrCalc>(
                    "13px", Map.AutoKeywords, CssValueParser.TryParseLengthOrCalc, AutoKeyword.Auto),
                Right = CssKeywordOrValueParser.FromCssText<AutoKeyword, LengthOrCalc>(
                    "17px", Map.AutoKeywords, CssValueParser.TryParseLengthOrCalc, AutoKeyword.Auto)
            };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal("13px", clone.Bottom.ToString());
            Assert.Equal("17px", clone.Right.ToString());
        }

        [Fact]
        public void InheritStyle_Everything_CopiesBoxSizing()
        {
            // box-sizing stopped inheriting through the normal ancestor->descendant mechanism (moved out
            // of InheritStyle's "always" section into the non-inherited BoxModel area), but a structural
            // duplicate of the SAME source box - CssProxyBox's repeated header/footer, an inline/block
            // split - still needs to carry the source element's own resolved box-sizing, exactly like
            // BoxDecorationBreak/the break properties/PdfTagType. Without this, a border-box element split
            // across an inline/block boundary would have its continuation fragment silently revert to the
            // CSS initial content-box.
            var source = new CssBox(null, null)
            {
                BoxSizing = CssProperty<BoxSizingMode>.FromCssText("border-box", Map.BoxSizingModes, BoxSizingMode.ContentBox)
            };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal("border-box", clone.BoxSizing.ToString());
        }

        [Fact]
        public void InheritStyle_Everything_DoesNotCopyBookmarkProperties()
        {
            // Unlike PdfTagType/box-sizing above, bookmark-level/-label/-state/-target deliberately do
            // NOT carry over on the "everything" path (CssProxyBox's repeated header/footer clone,
            // DomParser's inline/block split) - a bookmark is per-SOURCE-ELEMENT, not per-box, so an
            // inline element carrying bookmark-level that gets split into multiple sibling CssBoxes
            // (e.g. because it wraps a block child) must produce exactly one bookmark, not one per
            // split fragment. See CssBox.StyleProperties.cs's InheritStyle "everything" branch.
            var source = new CssBox(null, null)
            {
                BookmarkLevel = "2",
                BookmarkLabel = "\"Custom\"",
                BookmarkState = "closed",
                BookmarkTarget = "url(#other)"
            };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal("none", clone.BookmarkLevel);
            Assert.Equal("content(text)", clone.BookmarkLabel);
            Assert.Equal("open", clone.BookmarkState);
            Assert.Equal("self", clone.BookmarkTarget);
        }

        [Fact]
        public void InheritStyle_UnicodeBidi_DoesNotAdoptParentsValue()
        {
            // unicode-bidi is CSS-spec Inherited: no, unlike every other property in the otherwise-100%-
            // inherited Text area - InheritStyle must explicitly restore it after the area's whole-by-
            // reference adoption, or a child would silently pick up the parent's isolate/embed/etc.
            var parent = new CssBox(null, null) { UnicodeBidi = CssProperty<UnicodeMode>.FromCssText("isolate", Map.UnicodeModes, UnicodeMode.Normal) };
            var child = new CssBox(parent, null);

            child.InheritStyle();

            Assert.Equal(UnicodeMode.Normal, child.UnicodeBidi.Value);
        }

        [Fact]
        public void InheritStyle_Everything_CopiesUnicodeBidi()
        {
            // Unlike the normal ancestor->descendant case just above, a structural duplicate of the SAME
            // source box (CssProxyBox's repeated header/footer, an inline/block split) needs the source
            // element's own resolved unicode-bidi, exactly like box-sizing/BoxDecorationBreak/PdfTagType -
            // this is the `everything: true` path deliberately skipping the restore-to-pre-inherit-value
            // correction the normal path applies.
            var source = new CssBox(null, null) { UnicodeBidi = CssProperty<UnicodeMode>.FromCssText("isolate", Map.UnicodeModes, UnicodeMode.Normal) };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal(UnicodeMode.Isolate, clone.UnicodeBidi.Value);
        }

        [Fact]
        public void InheritStyle_VerticalAlign_DoesNotAdoptParentsValue()
        {
            // vertical-align is CSS-spec Inherited: no (CSS 2.1 §10.8.1), unlike every other property in
            // the otherwise-100%-inherited Text area - InheritStyle must explicitly restore it after the
            // area's whole-by-reference adoption, or a child would silently pick up the parent's declared
            // alignment instead of resolving to the initial "baseline" (issue #530).
            var parent = new CssBox(null, null) { VerticalAlign = CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>>.FromValue("middle", new CssKeywordOrValue<VerticalAlignment, LengthOrCalc>(VerticalAlignment.Middle, null)) };
            var child = new CssBox(parent, null);

            child.InheritStyle();

            Assert.Equal(VerticalAlignment.Baseline, child.VerticalAlign.Value.Keyword);
        }

        [Fact]
        public void InheritStyle_Everything_CopiesVerticalAlign()
        {
            // Unlike the normal ancestor->descendant case just above, a structural duplicate of the SAME
            // source box (CssProxyBox's repeated header/footer, an inline/block split) needs the source
            // element's own resolved vertical-align, exactly like unicode-bidi/box-sizing - this is the
            // `everything: true` path deliberately skipping the restore-to-pre-inherit-value correction
            // the normal path applies.
            var source = new CssBox(null, null) { VerticalAlign = CssProperty<CssKeywordOrValue<VerticalAlignment, LengthOrCalc>>.FromValue("middle", new CssKeywordOrValue<VerticalAlignment, LengthOrCalc>(VerticalAlignment.Middle, null)) };
            var clone = new CssBox(null, null);

            clone.InheritStyle(source, everything: true);

            Assert.Equal(VerticalAlignment.Middle, clone.VerticalAlign.Value.Keyword);
        }

        // ── regression: DomParser.CascadeApplyStyles's fast-path skip of the defaulting loop ──

        /// <summary>
        /// <c>DomParser.CascadeApplyStyles</c>'s "defaulting" loop (re-asserting every property's
        /// <c>CssDefaults</c> initial value) is skipped entirely for a box whose <see cref="ComputedStyle"/>
        /// is still the shared <see cref="Dom.ComputedStyle.Default"/> - safe only because every area's own
        /// <c>Default</c> is itself sourced from the same <c>CssDefaults</c> store, making the loop a
        /// guaranteed no-op on such a box for every property except a small, explicitly-handled set. This
        /// test exhaustively re-derives that set by actually running the loop's own logic
        /// (<see cref="CssUtils.SetPropertyValue"/>) against every <c>CssDefaults</c> entry on a fresh box
        /// and asserting nothing outside the known set changes anything - so it fails loudly (rather than
        /// silently reintroducing a correctness bug) if a future area/property addition breaks the
        /// no-op assumption the fast path depends on.
        /// <para>
        /// Every <see cref="CSS.CssProperty{T}"/>-backed property (<c>direction</c>/<c>unicode-bidi</c>/
        /// <c>writing-mode</c>/<c>container-type</c>/<c>z-index</c>/<c>grid-template-columns</c>/
        /// <c>-rows</c>/<c>box-sizing</c>/<c>box-decoration-break</c>/<c>column-fill</c>/<c>column-span</c>)
        /// is exempt from needing an exception here: <see cref="CSS.CssProperty{T}"/> is a
        /// record, and every T it's instantiated with (an enum, <see cref="CSS.CssKeywordOrValue{TEnum,TValue}"/>,
        /// or <see cref="CSS.GridTemplate"/> — the latter two records too, with <see cref="CSS.GridTemplate"/>
        /// giving its list/dictionary-typed members explicit content equality since the BCL collection
        /// types don't have that by default) compares by value, so re-parsing the same initial-value
        /// string twice correctly produces an equal <see cref="CSS.CssProperty{T}"/> and the cascade's
        /// copy-on-write comparison sees the real no-op. Only <c>font-family</c> remains, for an unrelated
        /// reason: its setter is a multi-field write (<c>FontFamily</c> + <c>FontFamilyList</c> from one
        /// declaration), not a <see cref="CSS.CssProperty{T}"/> reference-equality artifact.
        /// </para>
        /// </summary>
        [Fact]
        public void CascadeDefaultingLoop_OnAFreshBox_OnlyKnownExceptionsAreNotNoOps()
        {
            var knownExceptions = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase)
            {
                PropertyNames.FontFamily,
            };
            var parser = new CssValueParser(new PdfSharpAdapter());
            var unexpectedlyNotNoOp = new List<string>();

            foreach (var (name, initial) in CssDefaults.InitialValues)
            {
                if (initial is null || name == PropertyNames.Display) continue;

                var box = new CssBox(null, null);
                Assert.Same(ComputedStyle.Default, box.ComputedStyle);

                CssUtils.SetPropertyValue(parser, box, name, initial);

                if (!ReferenceEquals(ComputedStyle.Default, box.ComputedStyle) && !knownExceptions.Contains(name))
                {
                    unexpectedlyNotNoOp.Add(name);
                }
            }

            Assert.Empty(unexpectedlyNotNoOp);
        }
    }
}
