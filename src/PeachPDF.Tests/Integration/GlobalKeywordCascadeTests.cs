using PeachPDF.Adapters;
using PeachPDF.CSS;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// Integration tests for the five global CSS keywords (inherit, initial, unset, revert,
    /// revert-layer) at the cascade-resolution level, plus regression tests confirming that
    /// the 3-phase cascade refactor did not break existing behaviour.
    ///
    /// Each test renders a small HTML document, walks the box tree to find the target element,
    /// and asserts on the string CSS-value properties that the cascade writes to CssBox.
    /// </summary>
    public class GlobalKeywordCascadeTests
    {
        // ── inherit ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Inherit_ColorFromParent_ChildGetsParentColor()
        {
            // Child explicitly forces inheritance of the parent's author-set color.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: red">
                  <span id="child" style="color: inherit">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(255, 0, 0)", child!.Color);
        }

        [Fact]
        public async Task Inherit_NonInheritedProperty_ChildGetsParentValue()
        {
            // margin-top is NOT inherited by default; explicit "inherit" should force it.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="margin-top: 30px">
                  <div id="child" style="margin-top: inherit">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("30px", child!.MarginTop);
        }

        [Fact]
        public async Task Inherit_ColorWithoutExplicitParent_UsesInitialBlack()
        {
            // When the nearest ancestor has no explicit color set, inherit still resolves to
            // the cascade-computed value (which is "black" for the initial default).
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="color: inherit">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── initial ───────────────────────────────────────────────────────────

        [Fact]
        public async Task Initial_Color_ResetsToBlackIgnoringParent()
        {
            // "initial" for color is "black" regardless of what the parent set.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: blue">
                  <span id="child" style="color: initial">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("black", child!.Color);
        }

        [Fact]
        public async Task Initial_FontSize_ResetsToMedium()
        {
            // "initial" for font-size is "medium", overriding any inherited/UA value.
            var html = """
                <!DOCTYPE html><html><body>
                <h1 id="el" style="font-size: initial">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("medium", el!.FontSize);
        }

        [Fact]
        public async Task Initial_MarginTop_ResetsToZero()
        {
            // "initial" for margin-top is "0", even when an author rule sets it higher.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 50px; }
                </style></head><body>
                <div id="el" style="margin-top: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        [Fact]
        public async Task Initial_Display_ResetsToInline()
        {
            // "initial" for display is "inline", regardless of block-level UA defaults.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="display: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(DisplayMode.Inline, el!.Display.Value);
        }

        // ── unset ─────────────────────────────────────────────────────────────

        [Fact]
        public async Task Unset_InheritedProperty_BehavesLikeInherit()
        {
            // "unset" on an inherited property (color) acts like "inherit".
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: green">
                  <span id="child" style="color: unset">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(0, 128, 0)", child!.Color);
        }

        [Fact]
        public async Task Unset_NonInheritedProperty_BehavesLikeInitial()
        {
            // "unset" on a non-inherited property (margin-top) acts like "initial" → "0".
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 50px; }
                </style></head><body>
                <div id="el" style="margin-top: unset">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        [Fact]
        public async Task Unset_InheritedPropertyWithNoParentValue_UsesInitial()
        {
            // "unset" on an inherited property when parent has no explicit value → initial.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="el" style="color: unset">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        // ── revert ────────────────────────────────────────────────────────────

        [Fact]
        public async Task Revert_InAuthorRule_RevertsToUaValue()
        {
            // An author rule sets color to blue. A later (higher-specificity) author rule
            // uses "revert", which should restore the UA-phase value — black for plain divs.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { color: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
        }

        [Fact]
        public async Task Revert_H1FontSizeInAuthorRule_RevertsToUaFontSize()
        {
            // Author sets h1 font-size to 50px, then a later author rule reverts it.
            // The UA stylesheet defines h1 { font-size: 2em }; PeachPDF eagerly converts em to points at
            // cascade time (using the parent's ActualFont.Size, in true CSS points), so revert restores
            // the already-converted pt value rather than the original "2em" string - 2 * the 11pt "medium"
            // default the parent resolves to.
            var html = """
                <!DOCTYPE html><html><head><style>
                  h1 { font-size: 50px; }
                  #el { font-size: revert; }
                </style></head><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("22pt", el!.FontSize);
        }

        [Fact]
        public async Task Revert_InInlineStyle_RevertsToAuthorValue()
        {
            // "revert" in an inline style rolls back to the author-set value ("blue"),
            // NOT all the way to the UA value.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: revert">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task Revert_MarginTopInAuthorRule_RevertsToUaValue()
        {
            // Author sets margin-top; revert takes it back to the UA-phase value.
            // For a plain div the UA stylesheet doesn't set margin-top, so the result
            // is the initial value "0".
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { margin-top: 40px; }
                  #el  { margin-top: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.MarginTop);
        }

        // ── revert-layer ──────────────────────────────────────────────────────

        [Fact]
        public async Task RevertLayer_WithoutLayers_BehavesLikeRevert()
        {
            // Without @layer support, "revert-layer" must fall back to "revert" behaviour.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { color: revert-layer; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            // Same as "revert" from an author rule: UA value for div color = "black".
            Assert.Equal("black", el!.Color);
        }

        [Fact]
        public async Task RevertLayer_InInlineStyle_BehavesLikeRevert()
        {
            // "revert-layer" in inline should behave identically to "revert" in inline
            // (author-phase snapshot) when no layers are defined.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: revert-layer">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        // ── transform (non-inherited) ────────────────────────────────────────────

        [Fact]
        public async Task Initial_Transform_ResetsToNone()
        {
            // "initial" for transform is "none", regardless of an author rule.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { transform: rotate(45deg); }
                </style></head><body>
                <div id="el" style="transform: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("none", el!.Transform);
        }

        [Fact]
        public async Task Unset_Transform_BehavesLikeInitial()
        {
            // transform is NOT inherited, so "unset" must act like "initial" ("none"),
            // even though the parent has a non-default transform.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="transform: rotate(45deg)">
                  <div id="child" style="transform: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("none", child!.Transform);
        }

        [Fact]
        public async Task Revert_Transform_InInlineStyle_RestoresAuthorValue()
        {
            // "revert" in an inline style rolls back to the author-set value, NOT all the
            // way to the UA/initial value ("none").
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { transform: rotate(30deg); }
                </style></head><body>
                <div id="el" style="transform: revert">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rotate(30deg)", el!.Transform);
        }

        [Fact]
        public async Task Revert_Transform_InAuthorRule_RestoresUaValue()
        {
            // "revert" within the author phase itself rolls back to the pre-author (UA/initial)
            // value. Plain divs get no transform from the UA stylesheet, so this is "none".
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { transform: rotate(30deg); }
                  #el { transform: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("none", el!.Transform);
        }

        [Fact]
        public async Task Inherit_Transform_ForcesChildToPickUpParentValue()
        {
            // transform is not inherited by default, but an explicit "inherit" keyword
            // must still force the child to pick up the parent's computed value.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="transform: scale(2)">
                  <div id="child" style="transform: inherit">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("scale(2)", child!.Transform);
        }

        // ── regression: properties CssDefaults was missing entirely (flex/grid/etc.) ──
        // ── previously made "initial"/"unset"/"revert" a silent no-op for them ────────

        [Fact]
        public async Task Initial_FlexGrow_ResetsToZero()
        {
            // Before CssDefaults grew a "flex-grow" entry, GetInitialValue returned null for it and
            // DomParser.AssignCssBlock's `if (value is null) continue;` silently skipped the
            // declaration entirely - so "initial" left flex-grow at whatever the last-applied rule
            // set it to (here, "3"), instead of resetting it to the real initial value "0".
            var html = """
                <!DOCTYPE html><html><head><style>
                  .item { flex-grow: 3; }
                </style></head><body>
                <div id="el" class="item" style="flex-grow: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("0", el!.FlexGrow);
        }

        [Fact]
        public async Task Initial_JustifyItems_ResetsToNormal()
        {
            // Same class of gap as flex-grow above, for a Grid-area property.
            var html = """
                <!DOCTYPE html><html><head><style>
                  .item { justify-items: center; }
                </style></head><body>
                <div id="el" class="item" style="justify-items: initial">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal(JustifyItem.Normal, el!.JustifyItems.Value);
        }

        [Fact]
        public async Task Unset_ObjectFit_BehavesLikeInitial()
        {
            // object-fit is not inherited, so "unset" must act like "initial" ("fill") - another
            // property CssDefaults was previously missing entirely.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="object-fit: cover">
                  <div id="child" style="object-fit: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("fill", child!.ObjectFit);
        }

        // ── regression: box-sizing is spec-correctly not inherited ───────────────────

        [Fact]
        public async Task BoxSizing_DoesNotInheritFromParent()
        {
            // CSS Box Sizing 3 defines box-sizing as inherited: no. It used to be listed in
            // CssDefaults.InheritedProperties (a PeachPDF-specific deviation), so a child with no
            // explicit box-sizing picked up its parent's border-box instead of the real initial
            // value content-box.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="box-sizing: border-box">
                  <div id="child">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("content-box", child!.BoxSizing.ToString());
        }

        [Fact]
        public async Task Inherit_BoxSizing_StillWorksWhenExplicitlyRequested()
        {
            // Even though box-sizing no longer inherits by default, the explicit "inherit" keyword
            // must still force the child to pick up the parent's computed value.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="box-sizing: border-box">
                  <div id="child" style="box-sizing: inherit">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("border-box", child!.BoxSizing.ToString());
        }

        // ── regression: vertical-align is spec-correctly not inherited (issue #530) ──

        [Fact]
        public async Task VerticalAlign_DoesNotInheritFromParent()
        {
            // CSS 2.1 §10.8.1 defines vertical-align as Inherited: no. It used to be listed in
            // CssDefaults.InheritedProperties (a PeachPDF-specific deviation), so a child with no
            // explicit vertical-align picked up its parent's "middle" instead of the real initial
            // value "baseline".
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="vertical-align: middle">
                  <div id="child">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(VerticalAlignment.Baseline, child!.VerticalAlign.Value);
        }

        [Fact]
        public async Task Inherit_VerticalAlign_StillWorksWhenExplicitlyRequested()
        {
            // Even though vertical-align no longer inherits by default, the explicit "inherit"
            // keyword must still force the child to pick up the parent's computed value.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="vertical-align: middle">
                  <div id="child" style="vertical-align: inherit">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(VerticalAlignment.Middle, child!.VerticalAlign.Value);
        }

        [Fact]
        public async Task Unset_VerticalAlign_BehavesLikeInitial()
        {
            // vertical-align is NOT inherited, so "unset" must act like "initial" ("baseline"),
            // even though the parent has a non-default vertical-align.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="vertical-align: middle">
                  <div id="child" style="vertical-align: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal(VerticalAlignment.Baseline, child!.VerticalAlign.Value);
        }

        // ── regression: letter-spacing/word-spacing/font-palette are genuinely inherited, ──
        // ── so "unset" on them must resolve to the parent's value, not the initial value ──

        [Fact]
        public async Task Unset_LetterSpacing_ResolvesToParentsValue()
        {
            // letter-spacing is inherited (CSS Text 3 §5.1), but was missing from
            // CssDefaults.InheritedProperties - so "unset" incorrectly fell back to the initial
            // "normal" instead of the parent's declared value.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="letter-spacing: 2px">
                  <div id="child" style="letter-spacing: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("2px", child!.LetterSpacing.ToString());
        }

        [Fact]
        public async Task Unset_WordSpacing_ResolvesToParentsValue()
        {
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="word-spacing: 3px">
                  <div id="child" style="word-spacing: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("3px", child!.WordSpacing.ToString());
        }

        [Fact]
        public async Task Unset_FontPalette_ResolvesToParentsValue()
        {
            // font-palette is inherited (CSS Fonts 4 §16), but was missing from
            // CssDefaults.InheritedProperties.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="font-palette: dark">
                  <div id="child" style="font-palette: unset">text</div>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("dark", child!.FontPalette);
        }

        // ── regression: lazy revert-snapshot (only computed when a matched rule ──
        // ── actually uses revert/revert-layer) must still resolve correctly ─────

        [Fact]
        public async Task Revert_AmongMultipleAuthorRulesOnlyOneUsingRevert_StillFindsUaSnapshot()
        {
            // Several author-phase declarations apply to #el; only one of them uses "revert".
            // The snapshot must still be captured before the author pass runs, not skipped just
            // because most declarations in this pass are plain values.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { background-color: yellow; font-weight: bold; color: revert; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("black", el!.Color);
            Assert.Equal("rgb(255, 255, 0)", el.BackgroundColor);
            Assert.Equal("bold", el.FontWeight);
        }

        [Fact]
        public async Task Revert_CustomProperty_RevertsToUaValue()
        {
            // Custom property (--foo) declarations are routed separately from regular properties
            // but share the same rule set scanned for the revert/revert-layer keyword - confirm
            // that scan actually looks at custom-property values too, not just known properties.
            var html = """
                <!DOCTYPE html><html><head><style>
                  #el { --accent: blue; }
                  #el.override { --accent: revert; }
                </style></head><body>
                <div id="el" class="override">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.False(el!.CustomProperties?.ContainsKey("--accent") ?? false, "revert with no prior origin snapshot value should leave the custom property absent");
        }

        [Fact]
        public async Task Revert_NoRuleUsesRevertKeyword_SnapshotSkippedWithoutBreakingCascade()
        {
            // Plain sanity check that skipping the (now-lazy) snapshot entirely, for the common
            // case where nothing in the matched rules ever uses revert/revert-layer, still lets
            // every property cascade normally.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; margin-top: 12px; }
                  #el { background-color: yellow; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
            Assert.Equal("12px", el.MarginTop);
            Assert.Equal("rgb(255, 255, 0)", el.BackgroundColor);
        }

        // ── regression: 3-phase cascade must not break existing behaviour ──────

        [Fact]
        public async Task Regression_ColorInheritsFromParentWithoutKeyword()
        {
            // Standard CSS inheritance — no keyword required for inherited properties.
            var html = """
                <!DOCTYPE html><html><body>
                <div id="parent" style="color: purple">
                  <span id="child">text</span>
                </div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var child = FindById(root, "child");

            Assert.NotNull(child);
            Assert.Equal("rgb(128, 0, 128)", child!.Color);
        }

        [Fact]
        public async Task Regression_InlineStyleOverridesAuthorRule()
        {
            // Inline style must still win over author stylesheet rules.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                </style></head><body>
                <div id="el" style="color: green">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 128, 0)", el!.Color);
        }

        [Fact]
        public async Task Regression_AuthorRuleOverridesUaDefault()
        {
            // Author-set font-size on h1 must override the UA default of "2em".
            var html = """
                <!DOCTYPE html><html><head><style>
                  h1 { font-size: 10px; }
                </style></head><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("10px", el!.FontSize);
        }

        [Fact]
        public async Task Regression_UaStylesheetSetsH1FontSize()
        {
            // The UA stylesheet defines h1 { font-size: 2em }. PeachPDF eagerly converts em values to
            // points at cascade time, so the stored value is the converted pt string - 2 * the 11pt
            // "medium" default the parent resolves to.
            var html = """
                <!DOCTYPE html><html><body>
                <h1 id="el">heading</h1>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("22pt", el!.FontSize);
        }

        [Fact]
        public async Task Regression_ImportantAuthorRuleBeatsInlineStyle()
        {
            // A !important author rule must not be overridden by an inline style.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue !important; }
                </style></head><body>
                <div id="el" style="color: red">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task InlineImportant_BeatsAuthorImportantRule()
        {
            // Per spec, inline style sits at the top of its origin's !important tier too, same as
            // it does for the normal tier - an inline !important declaration must beat an author
            // stylesheet !important declaration for the same property.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue !important; }
                </style></head><body>
                <div id="el" style="color: red !important">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(255, 0, 0)", el!.Color);
        }

        [Fact]
        public async Task AuthorImportantRevert_RollsBackToInlineNormalValue_NotAllTheWayToUa()
        {
            // Documents a deliberate design choice in the 6-phase cascade restructure: an
            // author-!important declaration's "revert" rolls back to the state right before ANY
            // !important ran (which includes inline-normal's contribution), not all the way back to
            // the UA-origin snapshot as a stricter per-origin reading of the spec might imply. This
            // pins the behavior down so it can't drift silently - see the comment in
            // DomParser.CascadeApplyStyles above the author-!important phase for the full rationale.
            //
            // Expected chain: UA has no color rule for div (stays at the initial "black") -> author-
            // normal "div{color:blue}" applies -> inline-normal "color:green" applies on top -> the
            // author-!important "revert" should land on "green" (the value right before any
            // !important ran), not "blue" (author-normal-only) or "black" (UA-only).
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: blue; }
                  #el { color: revert !important; }
                </style></head><body>
                <div id="el" style="color: green">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 128, 0)", el!.Color);
        }

        [Fact]
        public async Task Regression_LaterAuthorRuleOverridesEarlierSameSpecificity()
        {
            // When two rules share the same specificity, source order decides: last wins.
            var html = """
                <!DOCTYPE html><html><head><style>
                  div { color: red; }
                  div { color: blue; }
                </style></head><body>
                <div id="el">text</div>
                </body></html>
                """;

            var root = await BuildBoxTree(html);
            var el = FindById(root, "el");

            Assert.NotNull(el);
            Assert.Equal("rgb(0, 0, 255)", el!.Color);
        }

        [Fact]
        public async Task Regression_UaBodyMarginStillApplied()
        {
            // The UA stylesheet sets body { margin: 8px }. This must still flow through.
            var html = """
                <!DOCTYPE html><html><body id="b">text</body></html>
                """;

            var root = await BuildBoxTree(html);
            var body = FindById(root, "b");

            Assert.NotNull(body);
            Assert.Equal("8px", body!.MarginTop);
        }

        // ── helpers ───────────────────────────────────────────────────────────

        private static async Task<CssBox> BuildBoxTree(string html)
        {
            var adapter = new PdfSharpAdapter();
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return container.Root!;
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            if (box.HtmlTag?.Attributes?.TryGetValue("id", out var boxId) == true
                && string.Equals(boxId, id, StringComparison.OrdinalIgnoreCase))
                return box;

            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found is not null) return found;
            }
            return null;
        }
    }
}
