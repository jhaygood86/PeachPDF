using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Annotations;
using PeachPDF.Tests.TestSupport;
using System.Collections.Generic;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The box-model geometry an <c>&lt;input&gt;</c>/<c>&lt;select&gt;</c> resolves to — the single
    /// rectangle that <c>FormFieldFragmentPainter</c> paints its chrome into and that
    /// <c>PdfGenerator.HandleFormFields</c> turns into the AcroForm widget's own <c>/Rect</c>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// That rectangle comes from <see cref="CssLineBox.UpdateRectangle"/>, which splits the box-model
    /// arithmetic across the two axes according to the word's <c>IsImage</c> flag: the horizontal
    /// border+padding is added there, the vertical is not (because
    /// <c>CssLayoutEngine.MeasureIntrinsicSize</c> already folded it into the word's own height).
    /// <c>CssRectFormField</c> used to be the one phantom-word type leaving that flag false, so a
    /// field got the vertical inset twice and the horizontal not at all — a UA-default 13×13px
    /// checkbox came out 9.75pt × 16.75pt, i.e. a visibly tall rectangle where a browser draws a
    /// square, and a radio button was drawn as an ellipse. Every assertion below fails on that shape.
    /// </para>
    /// <para>
    /// Checkbox/radio squareness additionally depends on the UA stylesheet <em>not</em> handing those
    /// two controls the text-field padding (<c>CssDefaults.DefaultStyleSheet</c>): an asymmetric
    /// <c>1pt 2pt</c> makes a correctly-computed border box non-square all on its own.
    /// </para>
    /// </remarks>
    public class FormFieldGeometryIntegrationTests
    {
        // UA default: 13px intrinsic content box (= 9.75pt at 1px = 0.75pt), zero padding, and a
        // 0.75pt border on each side.
        const double UaCheckboxBorderBox = 13 * 0.75 + 0.75 * 2;

        [Theory]
        [InlineData("checkbox")]
        [InlineData("radio")]
        public async Task UnstyledCheckboxOrRadio_ResolvesToASquareBorderBox(string type)
        {
            var rect = await FieldRect($"<input id='f' type='{type}' name='f' />");

            Assert.Equal(UaCheckboxBorderBox, rect.Width, 0.01);
            Assert.Equal(UaCheckboxBorderBox, rect.Height, 0.01);
        }

        [Theory]
        [InlineData("checkbox")]
        [InlineData("radio")]
        public async Task ExplicitlySizedCheckboxOrRadio_StaysSquare_AndAddsItsBordersToTheContentBox(string type)
        {
            // box-sizing is content-box, so the 40pt is the content box and each 0.75pt border is
            // outside it. Both axes must pick up exactly one border on each side - no more, no less.
            var rect = await FieldRect($"<input id='f' type='{type}' name='f' style='width:40pt;height:40pt' />");

            Assert.Equal(41.5, rect.Width, 0.01);
            Assert.Equal(41.5, rect.Height, 0.01);
        }

        [Fact]
        public async Task TextField_ResolvesToItsBorderBox_NotItsContentBox()
        {
            // The horizontal inset is the axis that used to be dropped entirely, so a field declared
            // 160pt wide painted its chrome 160pt wide - the content box - while the vertical axis
            // was inflated by the same border+padding twice over.
            var rect = await FieldRect(
                "<input id='f' type='text' name='f' value='x' style='width:160pt;height:20pt;padding:1pt 2pt;border-width:0.75pt' />");

            Assert.Equal(160 + 2 * 2 + 2 * 0.75, rect.Width, 0.01);
            Assert.Equal(20 + 2 * 1 + 2 * 0.75, rect.Height, 0.01);
        }

        [Fact]
        public async Task CheckboxPadding_IsNotInheritedFromTheTextFieldUaRule()
        {
            // The `input, select` UA rule's `padding: 1pt 2pt` is a text-field look; the more
            // specific checkbox/radio rule must zero it. Asserted through the resolved box rather
            // than by re-reading the stylesheet text, so it covers the cascade too.
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<input id='f' type='checkbox' name='f' /><input id='t' type='text' name='t' />"));

            var checkbox = LayoutHarness.FindById(root, "f")!;
            var text = LayoutHarness.FindById(root, "t")!;

            Assert.Equal(0, checkbox.ActualPaddingLeft, 0.01);
            Assert.Equal(0, checkbox.ActualPaddingTop, 0.01);

            // The text field keeps it, proving the checkbox rule overrode rather than removed it.
            Assert.Equal(2, text.ActualPaddingLeft, 0.01);
            Assert.Equal(1, text.ActualPaddingTop, 0.01);
        }

        [Fact]
        public async Task Checkbox_GetsTheBrowserDefaultMargin_SoItDoesNotSitFlushAgainstItsLabel()
        {
            var (root, _) = await LayoutHarness.LayoutAsync(
                LayoutHarness.Wrap("<input id='f' type='checkbox' name='f' />"));

            var checkbox = LayoutHarness.FindById(root, "f")!;

            Assert.Equal(4 * 0.75, checkbox.ActualMarginLeft, 0.01);
            Assert.Equal(3 * 0.75, checkbox.ActualMarginRight, 0.01);
            Assert.Equal(3 * 0.75, checkbox.ActualMarginTop, 0.01);
            Assert.Equal(3 * 0.75, checkbox.ActualMarginBottom, 0.01);
        }

        [Fact]
        public async Task AMarginlessControl_ReservesNoGapBeforeWhatFollowsIt()
        {
            // CssRect.ActualWordSpacing gives any IsImage word one extra space's width after itself.
            // A form field's phantom word answers IsImage for the box-model arithmetic above, but it
            // has no source whitespace to stand in for, so it must opt out (CssRectFormField
            // .ReservesTrailingSpace) - otherwise making it IsImage silently inserts a whole space
            // between a checkbox and a label written immediately after it, which no browser does and
            // which `margin: 0` could not remove.
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(
                "<div style='font: 16px monospace'>"
                + "<input id='f' type='checkbox' style='margin:0;padding:0;border:0' /><span id='after'>X</span>"
                + "</div>"));

            var rect = CommonUtils.GetFirstValueOrDefault(
                Assert.IsType<CssBoxFormField>(LayoutHarness.FindById(root, "f")).Rectangles, RRect.Empty);
            var after = FirstWordIn(LayoutHarness.FindById(root, "after")!);

            Assert.Equal(rect.Right, after.Left, 0.01);
        }

        [Theory]
        [InlineData("checkbox")]
        [InlineData("radio")]
        public async Task AcroFormWidget_ForACheckboxOrRadio_GetsASquareRect(string type)
        {
            // End-to-end: HandleFormFields turns the same rectangle into the widget annotation's
            // /Rect, so a non-square box also produced an elliptical radio button in every reader
            // that renders the widget's own appearance stream rather than the page content.
            var html = $"<!DOCTYPE html><html><body><input type='{type}' name='f' /></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                EnableInteractivePdfForms = true
            });

            var pdfRect = WidgetRect(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal(UaCheckboxBorderBox, pdfRect.Width, 0.01);
            Assert.Equal(UaCheckboxBorderBox, pdfRect.Height, 0.01);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The one line rectangle a field box resolves to — the exact value
        /// <c>PdfGenerator.HandleFormFields</c> reads (<c>box.Rectangles</c>' first entry) and the
        /// one <c>Fragment.PrimaryRect</c> hands the painter.
        /// </summary>
        /// <summary>
        /// The first word anywhere under <paramref name="box"/> - an inline element's own text can
        /// sit on an anonymous child rather than on the element's box.
        /// </summary>
        static CssRect FirstWordIn(CssBox box)
        {
            if (box.Words.Count > 0) return box.Words[0];

            foreach (var child in box.Boxes)
            {
                if (FirstWordIn(child) is { } word) return word;
            }

            return null!;
        }

        static async Task<RRect> FieldRect(string inputHtml)
        {
            var (root, _) = await LayoutHarness.LayoutAsync(LayoutHarness.Wrap(inputHtml));
            var box = Assert.IsType<CssBoxFormField>(LayoutHarness.FindById(root, "f"));

            return CommonUtils.GetFirstValueOrDefault(box.Rectangles, box.Bounds);
        }

        /// <summary>
        /// A field's own widget rectangle. A radio button is the one kind whose <c>/Rect</c> is not on
        /// the field itself: <c>FormFieldBuilder.AddRadioButton</c> makes the field a
        /// <see cref="PdfRadioButtonField"/> group and hangs one
        /// <see cref="PdfSharpCore.Pdf.Annotations.PdfWidgetAnnotation"/> kid per button off it, so the
        /// geometry lives on the kid.
        /// </summary>
        static PdfRectangle WidgetRect(PdfAcroField field)
        {
            if (field.Elements.GetArray(PdfAcroField.Keys.Kids) is { Elements.Count: > 0 } kids)
            {
                var kid = (PdfDictionary)((PdfReference)kids.Elements[0]).Value;
                return kid.Elements.GetRectangle(PdfAnnotation.Keys.Rect);
            }

            return field.Elements.GetRectangle(PdfAnnotation.Keys.Rect);
        }

        static List<PdfAcroField> Fields(PdfDocument document)
        {
            var result = new List<PdfAcroField>();
            foreach (var item in document.Catalog.AcroForm.Fields)
            {
                var dict = item is PdfReference iref ? iref.Value : item;
                if (dict is PdfAcroField field)
                    result.Add(field);
            }
            return result;
        }
    }
}
