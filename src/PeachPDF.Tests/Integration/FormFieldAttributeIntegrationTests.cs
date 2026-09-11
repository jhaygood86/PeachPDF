using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.AcroForms;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using PeachPDF.PdfSharpCore.Pdf.Annotations;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// The HTML form-control attributes that become PDF field entries rather than a field's kind or
    /// value: <c>readonly</c>, <c>disabled</c>, <c>required</c>, <c>maxlength</c>,
    /// <c>type=password</c> and <c>placeholder</c>. Before this, the only <c>/Ff</c> bits PeachPDF
    /// ever set were the two its own CSS extensions drive (comb and do-not-scroll), so every one of
    /// these attributes was silently dropped — most consequentially <c>type=password</c>, whose value
    /// was drawn legibly onto the page.
    /// </summary>
    public class FormFieldAttributeIntegrationTests
    {
        // ISO 32000-1 Table 221 (all field types) and Table 228 (text fields).
        const int ReadOnly = 1;
        const int Required = 1 << 1;
        const int NoExport = 1 << 2;
        const int Password = 1 << 13;
        const int Radio = 1 << 15;
        const int Combo = 1 << 17;
        const int Comb = 1 << 24;

        [Theory]
        [InlineData("<input name='f' readonly />", ReadOnly)]
        [InlineData("<input name='f' required />", Required)]
        // HTML defines a disabled control as neither editable nor submitted, which PDF spells as two
        // separate bits - so `disabled` is not simply a synonym for `readonly`.
        [InlineData("<input name='f' disabled />", ReadOnly | NoExport)]
        [InlineData("<input name='f' readonly required />", ReadOnly | Required)]
        [InlineData("<input type='password' name='f' />", Password)]
        [InlineData("<input name='f' />", 0)]
        public async Task TextField_MapsItsAttributesToFieldFlags(string html, int expected)
        {
            Assert.Equal(expected, (await Field(html)).Elements.GetInteger(PdfAcroField.Keys.Ff));
        }

        [Theory]
        [InlineData("<input type='checkbox' name='f' required />", Required)]
        [InlineData("<input type='checkbox' name='f' disabled />", ReadOnly | NoExport)]
        public async Task Checkbox_MapsTheSharedAttributesToFieldFlags(string html, int expected)
        {
            Assert.Equal(expected, (await Field(html)).Elements.GetInteger(PdfAcroField.Keys.Ff));
        }

        [Fact]
        public async Task RadioGroup_CarriesTheSharedFlagsOnTheGroup_WithoutLosingItsRadioBit()
        {
            // The group is the field; its kid widgets are only geometry and appearance. Putting the
            // flags on the widget would leave the field itself unflagged.
            //
            // Asserted as the WHOLE value rather than masked to Required: the Radio bit comes from
            // the field's own constructor, so an assignment where the shared flags should be OR'd in
            // drops it - and a /FT /Btn group without bit 16 is read as a set of checkboxes, losing
            // the mutual exclusion that makes it a radio group at all.
            var group = await Field("<input type='radio' name='f' required /><input type='radio' name='f' />");

            Assert.Equal(Radio | Required, group.Elements.GetInteger(PdfAcroField.Keys.Ff));
        }

        [Fact]
        public async Task RadioGroup_IsRequired_WhenAnyMemberIsRequired()
        {
            // Per the HTML Standard a radio group is required if ANY of its members says so, and
            // `required` is conventionally written on the last button as often as the first. The
            // group is created from whichever button is seen first, so reading the flags only there
            // would silently drop it from half the ways authors spell the same form.
            var group = await Field("<input type='radio' name='f' /><input type='radio' name='f' required />");

            Assert.Equal(Radio | Required, group.Elements.GetInteger(PdfAcroField.Keys.Ff));
        }

        [Fact]
        public async Task Select_KeepsItsComboFlagWhenTheSharedFlagsAreAdded()
        {
            // The Combo bit comes from the field's own constructor; the shared flags must be OR'd in
            // rather than assigned over it, or the field stops being a drop-down.
            var field = await Field("<select name='f' disabled><option>One</option></select>");

            Assert.Equal(Combo | ReadOnly | NoExport, field.Elements.GetInteger(PdfAcroField.Keys.Ff));
        }

        [Fact]
        public async Task Maxlength_BecomesMaxLen()
        {
            Assert.Equal(8, (await Field("<input name='f' maxlength='8' />")).Elements.GetInteger(PdfAcroField.Keys.MaxLen));
        }

        [Theory]
        [InlineData("<input name='f' />")]
        [InlineData("<input name='f' maxlength='0' />")]
        [InlineData("<input name='f' maxlength='-3' />")]
        [InlineData("<input name='f' maxlength='lots' />")]
        public async Task AnAbsentOrUnusableMaxlength_WritesNoMaxLen(string html)
        {
            // A zero would forbid every character, and the HTML Standard defines maxlength as a valid
            // non-negative integer - anything else is treated as absent rather than clamped.
            Assert.False((await Field(html)).Elements.ContainsKey(PdfAcroField.Keys.MaxLen));
        }

        [Fact]
        public async Task CombWinsOverADisagreeingMaxlength()
        {
            // A comb field's cell count IS its /MaxLen (Table 228), so the two cannot both be honoured.
            var field = await Field("<input name='f' maxlength='20' style='-peachpdf-pdf-form-field-comb: 6' />");

            Assert.Equal(6, field.Elements.GetInteger(PdfAcroField.Keys.MaxLen));
            Assert.Equal(Comb, field.Elements.GetInteger(PdfAcroField.Keys.Ff) & Comb);
        }

        [Theory]
        [InlineData("<input name='f' placeholder='Your name' />")]
        [InlineData("<input type='checkbox' name='f' placeholder='Your name' />")]
        [InlineData("<select name='f' placeholder='Your name'><option>One</option></select>")]
        public async Task Placeholder_BecomesTheFieldTooltip(string html)
        {
            // /TU is the tooltip a reader shows on hover and the accessible name assistive technology
            // reads instead of the machine-oriented /T - the closest durable home a placeholder has,
            // and one that can never be mistaken for the field's value.
            // Read back through the typed property, not the raw dictionary, so the accessor that
            // wrote it is proven to round-trip - /TU is stored UTF-16BE, not as the plain bytes.
            Assert.Equal("Your name", (await Field(html)).AlternateFieldName);
        }

        [Fact]
        public async Task AnAbsentPlaceholder_WritesNoTooltip()
        {
            Assert.False((await Field("<input name='f' />")).Elements.ContainsKey(PdfAcroField.Keys.TU));
        }

        [Fact]
        public async Task PasswordField_DrawsAsterisksRatherThanItsValue()
        {
            // Asserted by equivalence rather than by decoding glyphs: the appearance a password field
            // bakes must be byte-identical to the one a plain field with that many literal asterisks
            // bakes - same font, same positions, same operators - which is only true if the value was
            // never drawn. A "does the stream differ from the plain one" check would also pass for a
            // field that drew the value in a slightly different place.
            var masked = await AppearanceStream("<input type='password' name='f' value='hunter2' />");
            var asterisks = await AppearanceStream("<input name='f' value='*******' />");

            Assert.Equal(asterisks, masked);
        }

        [Fact]
        public async Task PasswordField_StillCarriesItsRealValue()
        {
            // The masking is presentation only - the author wrote the value into the element, and the
            // field is genuinely prefilled with it.
            Assert.Equal("hunter2", (await Field("<input type='password' name='f' value='hunter2' />")).Elements.GetString(PdfAcroField.Keys.V));
        }

        [Fact]
        public async Task Placeholder_IsNotDrawnUnlessTheCssPropertyAsksForIt()
        {
            // Opt-in by design: a drawn hint makes an unfilled field look filled. Without the
            // property the /Tx sequence is empty, so the appearance matches a field with no
            // placeholder at all.
            var withHint = await AppearanceStream("<input name='f' placeholder='Your name' />");
            var bare = await AppearanceStream("<input name='f' />");

            Assert.Equal(bare, withHint);
        }

        [Fact]
        public async Task Placeholder_IsDrawnMuted_WhenTheCssPropertyAsksForIt()
        {
            var hint = await AppearanceStream(
                "<input name='f' placeholder='Your name' style='-peachpdf-pdf-form-field-placeholder: auto' />");
            var value = await AppearanceStream("<input name='f' value='Your name' />");

            // Same text, drawn - so the hint is not the empty appearance...
            Assert.Contains("Tj", hint);
            // ...but muted, so it is not simply the value's appearance either. The only difference is
            // the fill colour, which a value at full opacity can never produce.
            Assert.NotEqual(value, hint);
        }

        [Fact]
        public async Task AValue_WinsOverAPlaceholder()
        {
            var both = await AppearanceStream(
                "<input name='f' value='Jane' placeholder='Your name' style='-peachpdf-pdf-form-field-placeholder: auto' />");
            var valueOnly = await AppearanceStream("<input name='f' value='Jane' />");

            Assert.Equal(valueOnly, both);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        static async Task<PdfDocument> RenderAsync(string bodyHtml)
        {
            var html = $"<!DOCTYPE html><html><body>{bodyHtml}</body></html>";
            return (await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                EnableInteractivePdfForms = true
            })).PdfDocument;
        }

        /// <summary>The document's one field. A radio group is the field, not its widget kids.</summary>
        static async Task<PdfAcroField> Field(string bodyHtml)
        {
            var document = await RenderAsync(bodyHtml);
            var fields = new List<PdfAcroField>();
            foreach (var item in document.Catalog.AcroForm.Fields)
            {
                var dict = item is PdfReference iref ? iref.Value : item;
                if (dict is PdfAcroField field)
                    fields.Add(field);
            }
            return Assert.Single(fields);
        }

        /// <summary>The field's "/AP /N" appearance stream, uncompressed, as operator text.</summary>
        static async Task<string> AppearanceStream(string bodyHtml)
        {
            var field = await Field(bodyHtml);
            var ap = field.Elements.GetDictionary(PdfAnnotation.Keys.AP);
            var form = (PdfFormXObject)((PdfReference)ap.Elements["/N"]!).Value;
            return Encoding.Latin1.GetString(form.Stream.Value);
        }
    }
}
