using PeachPDF.Adapters;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;
using PeachPDF.PdfSharpCore.Drawing;
using System.Threading.Tasks;

namespace PeachPDF.Tests.Integration
{
    // NOTE: like -peachpdf-pdf-tag-type (see PdfTagTypeIntegrationTests), the CSS identifier
    // tokenizer normalizes every keyword value to lowercase at parse time, so CssBox.PdfFormField
    // and its three sub-setting longhands are always lowercase regardless of authored casing.
    public class PdfFormFieldPropertyIntegrationTests
    {
        [Fact]
        public async Task PdfFormField_DefaultsToAuto()
        {
            var box = await FindByIdAsync("<input id='p' />");
            Assert.Equal("auto", box.PdfFormField, ignoreCase: true);
        }

        [Theory]
        [InlineData("none")]
        [InlineData("text")]
        [InlineData("checkbox")]
        [InlineData("radio")]
        [InlineData("select")]
        [InlineData("auto")]
        public async Task PdfFormField_ParsesEachKeyword(string keyword)
        {
            var box = await FindByIdAsync($"<input id='p' style='-peachpdf-pdf-form-field:{keyword}' />");
            Assert.Equal(keyword, box.PdfFormField, ignoreCase: true);
        }

        [Fact]
        public async Task PdfFormField_InvalidValueFallsBackToAuto()
        {
            var box = await FindByIdAsync("<input id='p' style='-peachpdf-pdf-form-field:not-a-real-value' />");
            Assert.Equal("auto", box.PdfFormField, ignoreCase: true);
        }

        [Fact]
        public async Task PdfFormField_IsNotInherited()
        {
            var html = Wrap("<div style='-peachpdf-pdf-form-field:checkbox'><input id='p' /></div>");
            var (root, _) = await BuildAndLayout(html);
            var box = FindById(root, "p")!;
            Assert.Equal("auto", box.PdfFormField, ignoreCase: true);
        }

        [Fact]
        public async Task PdfFormFieldAutoFontSize_DefaultsToNone_AndParsesAuto()
        {
            var defaultBox = await FindByIdAsync("<input id='p' />");
            Assert.Equal("none", defaultBox.PdfFormFieldAutoFontSize, ignoreCase: true);

            var autoBox = await FindByIdAsync("<input id='p' style='-peachpdf-pdf-form-field-auto-font-size:auto' />");
            Assert.Equal("auto", autoBox.PdfFormFieldAutoFontSize, ignoreCase: true);
        }

        [Fact]
        public async Task PdfFormFieldComb_DefaultsToNone_AndParsesInteger()
        {
            var defaultBox = await FindByIdAsync("<input id='p' />");
            Assert.Equal("none", defaultBox.PdfFormFieldComb, ignoreCase: true);

            var combBox = await FindByIdAsync("<input id='p' style='-peachpdf-pdf-form-field-comb:6' />");
            Assert.Equal("6", combBox.PdfFormFieldComb);
        }

        [Fact]
        public async Task PdfFormFieldDoNotScroll_DefaultsToNone_AndParsesAuto()
        {
            var defaultBox = await FindByIdAsync("<input id='p' />");
            Assert.Equal("none", defaultBox.PdfFormFieldDoNotScroll, ignoreCase: true);

            var autoBox = await FindByIdAsync("<input id='p' style='-peachpdf-pdf-form-field-do-not-scroll:auto' />");
            Assert.Equal("auto", autoBox.PdfFormFieldDoNotScroll, ignoreCase: true);
        }

        [Fact]
        public async Task PdfFormField_IsSnapshottableAndRevertsToPriorOrigin()
        {
            var box = await FindByIdAsync("<input id='p' style='-peachpdf-pdf-form-field:checkbox; -peachpdf-pdf-form-field:revert' />");
            Assert.Contains("-peachpdf-pdf-form-field", CssUtils.SnapshotProperties(box).Keys);
            Assert.Equal("auto", box.PdfFormField, ignoreCase: true);
        }

        [Fact]
        public async Task PrinceShorthand_FieldTypeKeywordAlone_SetsKindAndResetsOtherLonghands()
        {
            var box = await FindByIdAsync("<input id='p' style='-prince-pdf-form-field-settings:checkbox' />");
            Assert.Equal("checkbox", box.PdfFormField, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldAutoFontSize, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldComb, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldDoNotScroll, ignoreCase: true);
        }

        [Fact]
        public async Task PrinceShorthand_None_SetsAllFourLonghandsToNone()
        {
            var box = await FindByIdAsync("<input id='p' style='-prince-pdf-form-field-settings:none' />");
            Assert.Equal("none", box.PdfFormField, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldAutoFontSize, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldComb, ignoreCase: true);
            Assert.Equal("none", box.PdfFormFieldDoNotScroll, ignoreCase: true);
        }

        [Fact]
        public async Task PrinceShorthand_CombWithoutFieldType_DefaultsKindToText()
        {
            var box = await FindByIdAsync("<input id='p' style='-prince-pdf-form-field-settings:comb(6)' />");
            Assert.Equal("text", box.PdfFormField, ignoreCase: true);
            Assert.Equal("6", box.PdfFormFieldComb);
        }

        [Fact]
        public async Task PrinceShorthand_MultipleSubOptionsInAnyOrder_AllApply()
        {
            var box = await FindByIdAsync("<input id='p' style='-prince-pdf-form-field-settings:do-not-scroll text auto-font-size' />");
            Assert.Equal("text", box.PdfFormField, ignoreCase: true);
            Assert.Equal("auto", box.PdfFormFieldAutoFontSize, ignoreCase: true);
            Assert.Equal("auto", box.PdfFormFieldDoNotScroll, ignoreCase: true);
        }

        [Fact]
        public async Task PrinceShorthand_TextLikeFieldTypeKeyword_CollapsesToText()
        {
            var box = await FindByIdAsync("<input id='p' style='-prince-pdf-form-field-settings:password' />");
            Assert.Equal("text", box.PdfFormField, ignoreCase: true);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        private static string Wrap(string body) =>
            $"<!DOCTYPE html><html><head></head><body>{body}</body></html>";

        private async Task<CssBox> FindByIdAsync(string fragment)
        {
            var (root, _) = await BuildAndLayout(Wrap(fragment));
            return FindById(root, "p")!;
        }

        private static async Task<(CssBox root, HtmlContainerInt container)> BuildAndLayout(string html)
        {
            var adapter = new PdfSharpAdapter();
            adapter.PixelsPerPoint = 1.0;
            var container = new HtmlContainerInt(adapter);
            await container.SetHtml(html, null);

            var size = new XSize(595, 842);
            container.PageSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);
            container.MaxSize = PeachPDF.Utilities.Utils.Convert(size, 1.0);

            var measure = XGraphics.CreateMeasureContext(size, XGraphicsUnit.Point, XPageDirection.Downwards);
            using var graphics = new GraphicsAdapter(adapter, measure, 1.0);
            await container.PerformLayout(graphics);

            Assert.NotNull(container.Root);
            return (container.Root!, container);
        }

        private static CssBox? FindById(CssBox box, string id)
        {
            var val = box.HtmlTag?.TryGetAttribute("id", "");
            if (val != null && val.Equals(id, System.StringComparison.OrdinalIgnoreCase))
                return box;
            foreach (var child in box.Boxes)
            {
                var found = FindById(child, id);
                if (found != null) return found;
            }
            return null;
        }
    }
}
