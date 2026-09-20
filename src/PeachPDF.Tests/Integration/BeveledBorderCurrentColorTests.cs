using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Tests.TestSupport;
using System.Linq;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// A border side whose color is <c>currentColor</c> and whose line style is beveled
    /// (<c>inset</c>/<c>outset</c>/<c>groove</c>/<c>ridge</c>) resolves against a fixed light base,
    /// <c>rgb(238, 238, 238)</c>, rather than the box's own <c>color</c> - Blink's
    /// <c>ComputedStyleUtils::BorderSideColor</c>, which PeachPDF had no equivalent of (issue #1226).
    /// Shading a bevel from the text color produces no usable edge at either end of the range: default
    /// black text would paint a black-on-black frame.
    /// <para>
    /// Two layers are asserted separately here, because the substitution belongs to the first and it
    /// is the second a reader sees: <c>ActualBorder*Color</c> is the RESOLVED base, still unshaded,
    /// and <c>BorderBevelColors</c> derives the two faces from it at paint time. So a beveled
    /// currentColor border resolves to <c>#eee</c> and paints <c>#9a9a9a</c>/<c>#eeeeee</c>.
    /// </para>
    /// <para>
    /// Every expectation below was measured against Chrome 153 by rendering the same declaration
    /// headless and sampling the painted edge, not derived from the Blink source alone.
    /// </para>
    /// </summary>
    public class BeveledBorderCurrentColorTests
    {
        /// <summary>The base a beveled currentColor border resolves to, before any shading.</summary>
        private static readonly RColor Base = RColor.FromArgb(238, 238, 238);

        /// <summary>Its two faces - Chrome's bytes for any beveled currentColor border.</summary>
        private static readonly RColor Darkened = RColor.FromArgb(154, 154, 154);
        private static readonly RColor Lit = RColor.FromArgb(238, 238, 238);

        private static readonly RColor Red = RColor.FromArgb(255, 0, 0);
        private static readonly RColor Gray = RColor.FromArgb(128, 128, 128);

        [Theory]
        // Chrome paints the same two greys for every one of these, however different the colors are:
        // the base is fixed, so `color` contributes nothing at all to a beveled border.
        [InlineData("")]
        [InlineData("color: red")]
        [InlineData("color: white")]
        [InlineData("color: #808080")]
        [InlineData("color: #00ff00")]
        [InlineData("color: #222")]
        // Fully opaque base, so even a color carrying alpha loses it rather than shading translucent.
        [InlineData("color: rgba(255, 0, 0, 0.5)")]
        [InlineData("color: transparent")]
        public async Task BeveledBorderWithNoDeclaredColor_ShadesTheFixedBase_WhateverColorIs(string colorDeclaration)
        {
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='width: 60pt; height: 12pt; border: 4pt inset; {colorDeclaration}'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Base, box.ActualBorderTopColor);
            Assert.Equal(Base, box.ActualBorderBottomColor);

            // ...and the two faces derived from it are what actually reach the page.
            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, graphics);
            Assert.Equal([Darkened, Lit], graphics.FilledShapes.Select(shape => shape.Color).Distinct().ToList());
        }

        [Theory]
        // Every style that derives two faces takes the base, not just the two obvious ones.
        [InlineData("inset")]
        [InlineData("outset")]
        [InlineData("groove")]
        [InlineData("ridge")]
        public async Task EveryBeveledStyle_TakesTheFixedBase(string style)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='width: 60pt; height: 12pt; color: red; border: 4pt {style}'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Base, box.ActualBorderTopColor);
            Assert.Equal(Base, box.ActualBorderRightColor);
            Assert.Equal(Base, box.ActualBorderBottomColor);
            Assert.Equal(Base, box.ActualBorderLeftColor);
        }

        [Theory]
        // ...and no style that paints its color as declared does. These resolve through `color` as
        // they always have, which is what keeps `border: 1px solid` inheriting the text color.
        [InlineData("solid")]
        [InlineData("dashed")]
        [InlineData("dotted")]
        [InlineData("double")]
        public async Task ANonBeveledStyle_StillResolvesThroughColor(string style)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='width: 60pt; height: 12pt; color: red; border: 4pt {style}'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Red, box.ActualBorderTopColor);
            Assert.Equal(Red, box.ActualBorderBottomColor);
        }

        [Fact]
        public async Task ADeclaredBorderColor_IsShadedAsDeclared()
        {
            // The control the substitution must not swallow: only `currentColor` is redirected, so a
            // named base still derives its own faces. Chrome's own bytes for `border: 4pt inset
            // #808080` are #2c2c2c over #d4d4d4 - and notably NOT the two greys above, which is the
            // whole reason issue #1226 was originally filed on a wrong premise.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border: 4pt inset #808080'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Gray, box.ActualBorderTopColor);

            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, graphics);
            Assert.Equal(
                [RColor.FromArgb(44, 44, 44), RColor.FromArgb(212, 212, 212)],
                graphics.FilledShapes.Select(shape => shape.Color).Distinct().ToList());
        }

        [Fact]
        public async Task ASolidChildInheritingBorderColor_ResolvesAgainstItsOwnColor()
        {
            // `border-color: inherit` inherits the COMPUTED value, which is still `currentcolor` - so
            // the child resolves it against its own color, not the parent's, and not the parent's
            // bevel base. Chrome 153: red for the first child, blue for the second.
            //
            // Substituting during the cascade broke this twice over. Writing the bevel base into the
            // parent's own border-color made a solid child inherit #eee and paint a near-invisible
            // border; before that, writing the parent's resolved `color` there made the child paint
            // the PARENT's red where a browser uses the child's blue.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div style='border: 20pt inset; color: red'>"
                + "  <div id='sameColor' style='border: 10pt solid; border-color: inherit'>x</div>"
                + "  <div id='ownColor' style='color: blue; border: 10pt solid; border-color: inherit'>x</div>"
                + "</div></body></html>");

            Assert.Equal(Red, LayoutHarness.FindById(root, "sameColor")!.ActualBorderTopColor);
            Assert.Equal(RColor.FromArgb(0, 0, 255), LayoutHarness.FindById(root, "ownColor")!.ActualBorderTopColor);
        }

        [Fact]
        public async Task ABevelledChildInheritingBorderColor_StillTakesTheBase()
        {
            // The other half: inheriting the unresolved keyword means a child that is ITSELF bevelled
            // takes the base on its own account, whatever its parent was.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div style='border: 20pt solid; color: red'>"
                + "<div id='el' style='color: blue; border: 10pt inset; border-color: inherit'>x</div>"
                + "</div></body></html>");

            Assert.Equal(Base, LayoutHarness.FindById(root, "el")!.ActualBorderTopColor);
        }

        [Fact]
        public async Task EachSideResolvesAgainstItsOwnStyle_AcrossAllFour()
        {
            // Four sides, two bevelled and two flat, arranged so that reading ANY one side's style for
            // another side's colour gets a different answer. Without this a right/left longhand wired
            // to the top style passes every other test in the file.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border-width: 4pt;"
                + " border-style: inset solid solid inset'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Base, box.ActualBorderTopColor);     // inset
            Assert.Equal(Red, box.ActualBorderRightColor);    // solid
            Assert.Equal(Red, box.ActualBorderBottomColor);   // solid
            Assert.Equal(Base, box.ActualBorderLeftColor);    // inset

            // ...and the mirror image, so neither arrangement can be satisfied by a constant.
            var (mirrored, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border-width: 4pt;"
                + " border-style: solid inset inset solid'></div>"
                + "</body></html>");

            var mirroredBox = LayoutHarness.FindById(mirrored, "el")!;
            Assert.Equal(Red, mirroredBox.ActualBorderTopColor);
            Assert.Equal(Base, mirroredBox.ActualBorderRightColor);
            Assert.Equal(Base, mirroredBox.ActualBorderBottomColor);
            Assert.Equal(Red, mirroredBox.ActualBorderLeftColor);
        }

        [Fact]
        public async Task TheSubstitutionIsPerSide_NotPerBox()
        {
            // Blink resolves each border longhand against its OWN side's style, so a box that mixes
            // them takes both bases at once. A per-box decision would be indistinguishable on every
            // test above and wrong here.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border-width: 4pt;"
                + " border-top-style: inset; border-bottom-style: solid'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Base, box.ActualBorderTopColor);
            Assert.Equal(Red, box.ActualBorderBottomColor);
        }

        [Fact]
        public async Task ALogicalBorderStyle_IsSeenByTheSubstitution()
        {
            // `border-block-start-style` only reaches BorderTopStyle in ResolveLogicalProperties, which
            // used to run AFTER currentColor resolution - so the substitution read the physical
            // longhand's initial `none`, decided the side was not beveled, and resolved to `color`.
            // The border then painted a bevel derived from the text color anyway.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border-width: 4pt;"
                + " border-block-start-style: inset'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Base, box.ActualBorderTopColor);
        }

        [Theory]
        // Blink guards the substitution with style.IsDisplayTableType(), so a table and everything
        // inside it bevels from its own currentColor. Confirmed in Chrome 153 for each of these: the
        // painted edge is a shaded red (#ab0000 / #ff0000), not the two greys.
        [InlineData("table")]
        [InlineData("inline-table")]
        [InlineData("table-caption")]
        [InlineData("table-cell")]
        [InlineData("table-row")]
        [InlineData("table-row-group")]
        [InlineData("table-header-group")]
        [InlineData("table-footer-group")]
        [InlineData("table-column")]
        [InlineData("table-column-group")]
        public async Task ATableDisplayType_IsExemptAndBevelsItsOwnColor(string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><div style='display: table'>"
                + $"<div id='el' style='display: {display}; color: red; border: 4pt inset'>x</div>"
                + "</div></body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Red, box.ActualBorderTopColor);
            Assert.Equal(Red, box.ActualBorderBottomColor);
        }

        [Theory]
        // ...and nothing else is exempt, however table-like it may look from a layout standpoint.
        [InlineData("block")]
        [InlineData("inline-block")]
        [InlineData("flex")]
        [InlineData("grid")]
        [InlineData("list-item")]
        public async Task ANonTableDisplayType_TakesTheFixedBase(string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='display: {display}; width: 60pt; height: 12pt; color: red; border: 4pt inset'></div>"
                + "</body></html>");

            Assert.Equal(Base, LayoutHarness.FindById(root, "el")!.ActualBorderTopColor);
        }

        [Theory]
        // A table-internal display that CSS Display 3 §2.7 blockifies is no longer a table display
        // type by the time the colour is resolved, so it loses the exemption. Blink consults an
        // already-blockified ComputedStyle; the equivalent here is DerivedStyle.ActualDisplay (which
        // blockifies a float) plus NormalizeFlexOrGridItem (which rewrites an item's own Display
        // before this runs). Reading the raw cascaded `box.Display` instead gets every row wrong.
        //
        // Measured in Chrome 153: each of these paints #9a9a9a/#eeeeee, where the same declaration on
        // a statically-positioned table-cell paints a shaded red.
        [InlineData("float: left", "table-cell")]
        [InlineData("float: left", "table-row")]
        [InlineData("float: right", "table-caption")]
        public async Task AFloatedTableInternalBox_IsBlockifiedAndLosesTheExemption(
            string floatDeclaration, string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='{floatDeclaration}; display: {display}; width: 60pt; height: 12pt;"
                + " color: red; border: 4pt inset'></div>"
                + "</body></html>");

            Assert.Equal(Base, LayoutHarness.FindById(root, "el")!.ActualBorderTopColor);
        }

        [Theory]
        [InlineData("flex", "table-cell")]
        [InlineData("grid", "table-row")]
        public async Task AFlexOrGridItemWithATableInternalDisplay_IsBlockifiedAndLosesTheExemption(
            string containerDisplay, string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div style='display: {containerDisplay}'>"
                + $"<div id='el' style='display: {display}; width: 60pt; height: 12pt;"
                + " color: red; border: 4pt inset'></div></div>"
                + "</body></html>");

            Assert.Equal(Base, LayoutHarness.FindById(root, "el")!.ActualBorderTopColor);
        }

        [Theory]
        // `display: table`/`inline-table` blockify to `table`, which is still a table display type,
        // so an out-of-flow or floated one keeps the exemption. The arm that must NOT be broken by
        // the blockification handling above.
        [InlineData("float: left", "table")]
        [InlineData("position: absolute", "table")]
        [InlineData("position: absolute", "inline-table")]
        public async Task ABlockifiedTableBoxIsStillATable_AndKeepsTheExemption(
            string outOfFlowDeclaration, string display)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + $"<div id='el' style='{outOfFlowDeclaration}; display: {display}; width: 60pt;"
                + " color: red; border: 4pt inset'><div style='height: 12pt'>x</div></div>"
                + "</body></html>");

            Assert.Equal(Red, LayoutHarness.FindById(root, "el")!.ActualBorderTopColor);
        }

        [Fact]
        public async Task OutlineColor_IsNeverSubstituted_EvenWhenTheOutlineIsBeveled()
        {
            // The substitution lives on the four border longhands only: Blink's
            // OutlineColor::ColorIncludingFallback resolves against GetCurrentColor() whatever the
            // outline's style is, and Chrome 153 paints `outline: 20px inset; color: red` as a shaded
            // red. An implementation that keyed off "beveled line style" alone would get this wrong.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; outline: 4pt inset'></div>"
                + "</body></html>");

            Assert.Equal(Red, LayoutHarness.FindById(root, "el")!.ActualOutlineColor);
        }

        [Fact]
        public async Task ColumnRuleColor_IsNeverSubstituted_EvenWhenTheRuleIsBeveled()
        {
            // Same as the outline: ColumnRuleColor::ColorIncludingFallback resolves against the box's
            // own color, and Chrome 153 paints `column-rule: 20px inset; color: red` shaded red.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 120pt; columns: 2; column-rule: 4pt inset; color: red'>x</div>"
                + "</body></html>");

            Assert.Equal(Red, LayoutHarness.FindById(root, "el")!.ActualColumnRuleColor);
        }

        [Fact]
        public async Task BackgroundColor_IsNeverSubstituted()
        {
            // `background-color: currentcolor` has no line style to be beveled by, and shares the
            // resolution pass with the borders - a regression guard on the one property in that pass
            // whose behaviour must not have moved at all.
            var (root, _) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body>"
                + "<div id='el' style='width: 60pt; height: 12pt; color: red; border: 4pt inset;"
                + " background-color: currentcolor'></div>"
                + "</body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Red, box.ActualBackgroundColor);
            Assert.Equal(Base, box.ActualBorderTopColor);
        }

        [Fact]
        public async Task AnAuthorBorderStyleOnARule_ResolvesThroughTheRulesOwnGray()
        {
            // The residue the UA sheet's declared base used to leave (issue #1226): with `#eee` written
            // into `hr { border: 1px inset #eee }`, an author who replaced only the STYLE kept the base
            // as a colour, and a dashed rule painted #eee - near-invisible on white. Chrome paints the
            // rule's own gray, because nothing is derived from a flat style.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><head><style>hr { border-style: dashed }</style></head>"
                + "<body><hr id='el'></body></html>");

            var box = LayoutHarness.FindById(root, "el")!;
            Assert.Equal(Gray, box.ActualBorderTopColor);

            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, box, graphics);
            Assert.Contains(graphics.Log.OfType<TestRecordingGraphics.DrawLineCall>(),
                line => line.Color == Gray && line.DashPattern is { Count: > 0 });
        }

        [Fact]
        public async Task TheUaSheetDeclaresNoBorderColorForARule()
        {
            // The sheet is back to the HTML Standard's literal text, which is only correct because the
            // engine now derives the base. Pinned as a property of the RESOLVED rule rather than by
            // grepping the sheet: a default rule must still land on Chrome's two greys while an
            // author-flattened one lands on gray - the pair of facts that a re-declared base would
            // silently split apart again.
            var (root, container) = await LayoutHarness.LayoutAsync(
                "<!DOCTYPE html><html><body><div style='color: green'>"
                + "<hr id='beveled'><hr id='flat' noshade></div></body></html>");

            var beveled = LayoutHarness.FindById(root, "beveled")!;
            Assert.Equal(Base, beveled.ActualBorderTopColor);

            var graphics = new TestRecordingGraphics();
            FragmentPaintHarness.PaintBox(container, beveled, graphics);
            Assert.Equal([Darkened, Lit], graphics.FilledShapes.Select(shape => shape.Color).Distinct().ToList());

            // ...and the inherited green reaches neither rule, because the sheet's own `color: gray`
            // is what a flat one resolves through.
            Assert.Equal(Gray, LayoutHarness.FindById(root, "flat")!.ActualBorderTopColor);
        }
    }
}
