using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Tests.Integration
{
    public class InteractivePdfFormsAcroFormTests
    {
        [Fact]
        public async Task EnableInteractivePdfForms_False_AllocatesNoAcroFormObject()
        {
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='n' value='hi' /></body></html>", PageSize.A4);

            Assert.False(result.PdfDocument.Catalog.Elements.ContainsKey("/AcroForm"));
        }

        [Fact]
        public async Task TextField_ProducesTextFieldWithNameAndValue()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='email' value='a@b.com' /></body></html>", config);

            var fields = Fields(result.PdfDocument);
            var field = Assert.Single(fields);
            var textField = Assert.IsType<PdfTextField>(field);

            Assert.Equal("/Tx", textField.FieldType);
            Assert.Equal("email", textField.PartialFieldName);
            Assert.Equal("a@b.com", textField.Value);
            Assert.True(textField.Rectangle.Width > 0);
            Assert.True(textField.Rectangle.Height > 0);
        }

        [Fact]
        public async Task Checkbox_Checked_ProducesCheckboxFieldWithYesState()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input type='checkbox' name='agree' checked /></body></html>", config);

            var field = Assert.IsType<PdfCheckBoxField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal("/Btn", field.FieldType);
            Assert.Equal("/Yes", field.Value);
            Assert.Equal("/Yes", field.AppearanceState);
        }

        [Fact]
        public async Task Checkbox_Unchecked_ProducesOffState()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input type='checkbox' name='agree' /></body></html>", config);

            var field = Assert.IsType<PdfCheckBoxField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal("/Off", field.Value);
            Assert.Equal("/Off", field.AppearanceState);
        }

        [Fact]
        public async Task RadioGroup_SharedName_ProducesOneFieldWithTwoKids()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var html = "<html><body>" +
                       "<input type='radio' name='color' value='red' checked />" +
                       "<input type='radio' name='color' value='green' />" +
                       "</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var field = Assert.IsType<PdfRadioButtonField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal("/Btn", field.FieldType);
            Assert.Equal("/Red", field.Value, ignoreCase: true);
            Assert.Equal(2, field.Kids.Count);
            Assert.Equal("/Red", field.Kids[0].AppearanceState, ignoreCase: true);
            Assert.Equal("/Off", field.Kids[1].AppearanceState);
        }

        [Fact]
        public async Task Select_ProducesComboFieldWithOptionsAndSelectedValue()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var html = "<html><body><select name='color'>" +
                       "<option value='r'>Red</option>" +
                       "<option value='g' selected>Green</option>" +
                       "</select></body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, config);

            var field = Assert.IsType<PdfComboBoxField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal("/Ch", field.FieldType);
            Assert.Equal("g", field.Value);
            var opt = field.Elements.GetArray(PdfAcroField.Keys.Opt);
            Assert.NotNull(opt);
            Assert.Equal(2, opt.Elements.Count);
        }

        [Fact]
        public async Task Checkbox_Checked_WithValueLiterallyOff_StillDistinguishesFromUnchecked()
        {
            // A literal value="Off" would otherwise collide with PDF's own reserved "unchecked"
            // state name, making a checked box indistinguishable from an unchecked one.
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input type='checkbox' name='agree' value='Off' checked /></body></html>", config);

            var field = Assert.IsType<PdfCheckBoxField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.NotEqual("/Off", field.Value);
            Assert.Equal(field.Value, field.AppearanceState);
        }

        [Fact]
        public async Task TextField_AutoFontSize_SetsAutoSizeDefaultAppearance()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='n' value='hi' style='-peachpdf-pdf-form-field-auto-font-size:auto' /></body></html>", config);

            var field = Assert.IsType<PdfTextField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.Equal("/Helv 0 Tf 0 g", field.DefaultAppearance);
        }

        [Fact]
        public async Task TextField_WithoutAutoFontSize_SetsFixedSizeDefaultAppearance()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='n' value='hi' /></body></html>", config);

            var field = Assert.IsType<PdfTextField>(Assert.Single(Fields(result.PdfDocument)));

            Assert.DoesNotContain("0 Tf", field.DefaultAppearance);
            Assert.Contains("/Helv", field.DefaultAppearance);
        }

        [Fact]
        public async Task NoneOverride_ProducesNoField()
        {
            var config = new PdfGenerateConfig { PageSize = PageSize.A4, EnableInteractivePdfForms = true };
            var result = await new PdfGenerator().GeneratePdf(
                "<html><body><input name='n' style='-peachpdf-pdf-form-field:none' /></body></html>", config);

            Assert.Empty(Fields(result.PdfDocument));
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static List<PdfAcroField> Fields(PdfDocument document)
        {
            var array = document.Catalog.AcroForm.Fields;
            var result = new List<PdfAcroField>();
            foreach (var item in array)
            {
                var dict = item is PdfReference iref ? iref.Value : item;
                if (dict is PdfAcroField field)
                    result.Add(field);
            }
            return result;
        }
    }
}
