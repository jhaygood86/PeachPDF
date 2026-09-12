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
    /// The <c>/Tx BMC</c> … <c>EMC</c> marked-content sequence ISO 32000-1 §12.7.3.3 requires around
    /// the value a variable-text field's appearance stream draws.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the contract by which a reader updates a field the user edits: it keeps everything
    /// before <c>/Tx BMC</c> and after <c>EMC</c> — the field's CSS border and background — and
    /// replaces only what lies between them. With no such sequence a reader has no region to
    /// replace, so it draws the new value <em>over</em> the generated one; observed in Adobe Reader
    /// as the old value still legible behind the new one, plus mojibake where the reader re-rendered
    /// the generated string's codes (written against PeachPDF's own embedded subset font) through
    /// the <c>/DA</c> Helvetica instead.
    /// </para>
    /// <para>
    /// Everything here asserts on operator <em>order and nesting</em> in the appearance stream rather
    /// than on a token merely being present (CLAUDE.md's testing conventions): a <c>/Tx BMC</c>
    /// emitted in the wrong place — after the text, inside the <c>BT</c>/<c>ET</c> text object, or
    /// wrapping the border — is exactly as broken as one that is missing, and a presence check passes
    /// for all of them.
    /// </para>
    /// </remarks>
    public class FormFieldVariableTextMarkedContentTests
    {
        [Fact]
        public async Task TextField_EnclosesItsValue_AndOnlyItsValue_InATxMarkedContentSequence()
        {
            var stream = await AppearanceStream("<input name='n' value='hi' />");

            var bmc = stream.IndexOf("/Tx BMC", System.StringComparison.Ordinal);
            var emc = stream.IndexOf("EMC", System.StringComparison.Ordinal);
            var bt = stream.IndexOf("BT", System.StringComparison.Ordinal);
            var et = stream.LastIndexOf("ET", System.StringComparison.Ordinal);

            Assert.True(bmc >= 0, "The value must be enclosed in a /Tx marked-content sequence.");

            // The whole text object sits strictly inside the sequence: §14.6 forbids a marked-content
            // sequence from straddling a BT/ET boundary, and a reader replacing the sequence's
            // contents must get the complete text object, not half of one.
            Assert.True(bmc < bt, "/Tx BMC must precede the text object it encloses.");
            Assert.True(et < emc, "EMC must follow the text object's own ET.");

            // The border/background is drawn before the sequence opens, so a reader regenerating the
            // value keeps the field's CSS chrome. "re" + "f" is the background fill rectangle
            // FormFieldChrome.PaintBorderAndBackground emits first.
            var background = stream.IndexOf(" re\nf", System.StringComparison.Ordinal);
            Assert.True(background >= 0 && background < bmc,
                "The background/border chrome must be drawn outside (before) the replaceable sequence.");
        }

        [Fact]
        public async Task TextField_WithAnEmptyValue_StillEmitsTheSequence()
        {
            // The sequence is what gives a reader a region to replace. A field that starts empty and
            // is then typed into needs one just as much as one that starts with text - without it,
            // the first thing typed is appended rather than placed.
            var stream = await AppearanceStream("<input name='n' value='' />");

            var bmc = stream.IndexOf("/Tx BMC", System.StringComparison.Ordinal);
            Assert.True(bmc >= 0, "An empty field still needs a replaceable /Tx sequence.");
            Assert.True(stream.IndexOf("EMC", System.StringComparison.Ordinal) > bmc);
        }

        [Fact]
        public async Task AFieldWithNoRoomToDrawIn_StillEmitsTheSequence()
        {
            // Padding and border can leave a field with no content box at all. That is the case where
            // the appearance draws nothing, so it looks like the sequence has nothing to wrap - but a
            // reader still needs a region to put the typed value in, and a field too small to show
            // its own text is the hardest place to notice the value being appended instead.
            var stream = await AppearanceStream("<input name='n' value='hi' style='height:1pt;padding:6pt' />");

            var bmc = stream.IndexOf("/Tx BMC", System.StringComparison.Ordinal);
            Assert.True(bmc >= 0, "A field with no drawable content box still needs a replaceable /Tx sequence.");
            Assert.True(stream.IndexOf("EMC", System.StringComparison.Ordinal) > bmc);
        }

        [Fact]
        public async Task SelectField_EnclosesItsSelectedLabelInTheSequence()
        {
            var stream = await AppearanceStream(
                "<select name='s'><option value='us'>United States</option><option value='ca' selected>Canada</option></select>");

            var bmc = stream.IndexOf("/Tx BMC", System.StringComparison.Ordinal);
            Assert.True(bmc >= 0);
            Assert.True(bmc < stream.IndexOf("BT", System.StringComparison.Ordinal));
            Assert.True(stream.LastIndexOf("ET", System.StringComparison.Ordinal)
                < stream.IndexOf("EMC", System.StringComparison.Ordinal));
        }

        [Fact]
        public async Task CombField_KeepsItsCellDividersOutsideTheSequence()
        {
            // A comb field's dividers are chrome, not value - they must survive the user retyping the
            // code. Drawing them with the characters (as the original DrawComb did) put them inside
            // the replaceable region, so a reader regenerating the value would erase the cells.
            var stream = await AppearanceStream(
                "<input name='n' value='AB' style='-peachpdf-pdf-form-field-comb: 6' />");

            var bmc = stream.IndexOf("/Tx BMC", System.StringComparison.Ordinal);
            Assert.True(bmc >= 0);

            // Each divider is a stroked line: "m ... l ... S". The last one must close before the
            // sequence opens.
            var lastStroke = stream[..bmc].LastIndexOf("\nS\n", System.StringComparison.Ordinal);
            Assert.True(lastStroke >= 0, "The comb dividers must be drawn before the replaceable sequence opens.");
        }

        [Theory]
        [InlineData("checkbox")]
        [InlineData("radio")]
        public async Task ButtonField_DoesNotEmitTheSequence(string type)
        {
            // §12.7.3.3 is about variable *text*. A checkbox/radio's appearance is a whole pre-baked
            // state a reader swaps between, never a region it regenerates character by character, so
            // marking one up as replaceable text would only invite a reader to overwrite the glyph.
            var stream = await AppearanceStream($"<input type='{type}' name='b' checked />");

            Assert.DoesNotContain("/Tx BMC", stream);
        }

        // ─── Helpers ─────────────────────────────────────────────────────────────

        /// <summary>
        /// The field's "/AP /N" appearance-stream content, decoded as operator text. Compression is
        /// off so the stream bytes are the operators themselves.
        /// </summary>
        static async Task<string> AppearanceStream(string inputHtml)
        {
            var html = $"<!DOCTYPE html><html><body>{inputHtml}</body></html>";
            var result = await new PdfGenerator().GeneratePdf(html, new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                CompressContentStreams = false,
                EnableInteractivePdfForms = true
            });

            return Encoding.Latin1.GetString(ResolveNormalAppearance(Single(result.PdfDocument)).Stream.Value);
        }

        /// <summary>
        /// The document's one field - a radio button's geometry and appearance live on the group's
        /// single widget kid rather than on the field itself.
        /// </summary>
        static PdfDictionary Single(PdfDocument document)
        {
            var fields = new List<PdfAcroField>();
            foreach (var item in document.Catalog.AcroForm.Fields)
            {
                var dict = item is PdfReference iref ? iref.Value : item;
                if (dict is PdfAcroField field)
                    fields.Add(field);
            }

            var only = Assert.Single(fields);
            if (only.Elements.GetArray(PdfAcroField.Keys.Kids) is { Elements.Count: > 0 } kids)
                return (PdfDictionary)((PdfReference)kids.Elements[0]).Value;

            return only;
        }

        static PdfFormXObject ResolveNormalAppearance(PdfDictionary widget)
        {
            var ap = widget.Elements.GetDictionary(PdfAnnotation.Keys.AP);
            if (ap.Elements["/N"] is PdfReference { Value: PdfFormXObject direct })
                return direct;

            var states = ap.Elements.GetDictionary("/N");
            var state = widget.Elements.GetName("/AS");
            return (PdfFormXObject)states.Elements.GetReference(state).Value;
        }
    }
}
