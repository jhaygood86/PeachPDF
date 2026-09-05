using PeachPDF.PdfSharpCore.Pdf;
using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Xunit;

namespace PeachPDF.Tests.Integration
{
    /// <summary>
    /// PDF/A-1 is enforced by rejecting every currently-implemented feature that requires a PDF
    /// transparency group (see <c>PdfAConformanceTests</c>) - but <c>mix-blend-mode</c>,
    /// <c>isolation</c>, CSS <c>mask</c>, <c>filter</c>, and <c>backdrop-filter</c> are not implemented
    /// by PeachPDF at all today, so there is no code path to directly assert a rejection against yet.
    /// </summary>
    /// <remarks>
    /// These tests instead PIN today's true "no-op" behavior for each property: generation succeeds
    /// under <see cref="PdfAConformance.PdfA1B"/>, the page's own content stream is byte-identical to
    /// the same document without the property, and the saved PDF contains none of the constructs PDF/A
    /// -1 forbids (a <c>/Group &lt;&lt; /S /Transparency</c> dictionary, an <c>/SMask</c> key, or a
    /// non-default <c>/ca</c>/<c>/CA</c> alpha value in an ExtGState).
    /// <para>
    /// <b>If you are implementing one of these properties</b>, the assertions below will start
    /// failing the moment the property does something (the content-stream-equality check fails first,
    /// since the property now renders differently). <b>That failure is the intended tripwire</b> - it
    /// is the signal to add the same <c>PdfATransparencyGuard.RequireAllowed</c> call (see
    /// <c>PdfATransparencyGuard.cs</c> and its 7 existing call sites in <c>PdfGraphicsState.cs</c>/
    /// <c>XGraphicsPdfRenderer.cs</c>/<c>PdfImage.cs</c>) to whichever new paint path now actually
    /// creates a transparency group, as part of that implementation work - not to just update or
    /// delete this pinned expectation.
    /// </para>
    /// </remarks>
    public class PdfAUnimplementedTransparencyFeatureTests
    {
        [Theory]
        [InlineData("mix-blend-mode: multiply;")]
        [InlineData("isolation: isolate;")]
        [InlineData("mask: url(#m);")]
        [InlineData("filter: blur(2px);")]
        [InlineData("backdrop-filter: blur(2px);")]
        public async Task UnimplementedProperty_UnderPdfA1_IsStillANoOp(string declaration)
        {
            var withProperty = $"<html><body><div style=\"width: 50px; height: 50px; background: #ff0000; {declaration}\"></div></body></html>";
            var withoutProperty = "<html><body><div style=\"width: 50px; height: 50px; background: #ff0000;\"></div></body></html>";

            var config = new PdfGenerateConfig
            {
                PageSize = PageSize.A4,
                PdfAConformance = PdfAConformance.PdfA1B,
                CompressContentStreams = false,
                // A fixed date (not DateTimeOffset.UtcNow) so the two renders' XMP packets - and so
                // their full saved bytes wherever compared - don't spuriously differ by microseconds.
                Metadata = new PdfDocumentMetadata { CreationDate = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero) },
            };

            // Must not throw - confirms the property really is still unimplemented/inert today. If
            // this alone starts throwing, PdfATransparencyGuard is (correctly) already firing for it
            // and this test should be deleted rather than "fixed" to expect the throw.
            var withResult = await new PdfGenerator().GeneratePdf(withProperty, config);
            var withoutResult = await new PdfGenerator().GeneratePdf(withoutProperty, config);

            var withText = Save(withResult);
            var withoutText = Save(withoutResult);

            // (a) Pin today's true no-op: the page's own content stream (not the whole file - the
            // trailer /ID is a fresh random GUID every render, so whole-file equality is never
            // meaningful) renders identically whether or not the property is present.
            Assert.Equal(FirstContentStream(withoutText), FirstContentStream(withText));

            // (b) State directly what "PDF/A-1 compatible" means structurally, rather than relying
            // only on (a): none of the constructs PDF/A-1 forbids appear anywhere in the saved file.
            Assert.DoesNotContain("/Transparency", withText);
            Assert.DoesNotContain("/SMask", withText);
            Assert.DoesNotMatch(new Regex(@"/(ca|CA)\s+0(\.\d+)?\b"), withText);
        }

        static string Save(PeachPdfDocument document)
        {
            var ms = new MemoryStream();
            document.Save(ms);
            return Encoding.Latin1.GetString(ms.ToArray());
        }

        static string FirstContentStream(string pdfText)
        {
            var match = Regex.Match(pdfText, @"stream\r?\n(.*?)\r?\nendstream", RegexOptions.Singleline);
            Assert.True(match.Success, "No content stream found in generated PDF.");
            return match.Groups[1].Value;
        }
    }
}
