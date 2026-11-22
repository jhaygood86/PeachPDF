using PeachDrawing.PDF.Tests.Import;

namespace PeachDrawing.PDF.Tests.Operations
{
    /// <summary>A signature covers the exact bytes of a file, so an operation that writes a new file must say a source's was not carried.</summary>
    public class PdfSignedSourceTests
    {
        const string SignatureValue = "<< /Type /Sig /Filter /Adobe.PPKLite /SubFilter /adbe.pkcs7.detached /ByteRange [0 0 0 0] /Contents <00> >>";

        /// <summary>A one-page file whose AcroForm is object 5 and whose other objects (6, 7...) are <paramref name="objects"/>.</summary>
        static byte[] WithForm(string acroForm, params string[] objects) =>
            TestPdf.Build("", catalogExtras: "/AcroForm 5 0 R", extraObjects: [acroForm, .. objects]);

        static byte[] Signed() => WithForm("<< /Fields [6 0 R] /SigFlags 3 >>", "<< /FT /Sig /T (Sig1) /V 7 0 R >>", SignatureValue);

        [Fact]
        public void HasSignatures_IsFalse_WithoutAnAcroForm() =>
            Assert.False(PdfPageSource.Open(TestPdf.BuildPages(1)).HasSignatures);

        [Fact]
        public void HasSignatures_IsFalse_ForAnUnsignedSignatureField() =>
            Assert.False(PdfPageSource.Open(WithForm("<< /Fields [6 0 R] >>", "<< /FT /Sig /T (Sig1) >>")).HasSignatures);

        [Fact]
        public void HasSignatures_IsTrue_ForASignatureFieldWithAValue() =>
            Assert.True(PdfPageSource.Open(WithForm("<< /Fields [6 0 R] >>", "<< /FT /Sig /T (Sig1) /V 7 0 R >>", SignatureValue)).HasSignatures);

        [Fact]
        public void HasSignatures_IsTrue_WhenTheFormSaysSignaturesExist() =>
            Assert.True(PdfPageSource.Open(WithForm("<< /Fields [] /SigFlags 1 >>")).HasSignatures);

        [Fact]
        public void HasSignatures_FindsAValueInAKidThatInheritsItsFieldType() =>
            Assert.True(PdfPageSource.Open(WithForm(
                "<< /Fields [6 0 R] >>",
                "<< /FT /Sig /T (group) /Kids [7 0 R] >>",
                "<< /Parent 6 0 R /T (Sig1) /V 8 0 R >>",
                SignatureValue)).HasSignatures);

        [Fact]
        public void HasSignatures_SurvivesFieldsThatLoopThroughTheirKids() =>
            Assert.False(PdfPageSource.Open(WithForm(
                "<< /Fields [6 0 R] >>",
                "<< /FT /Tx /T (a) /Kids [7 0 R] >>",
                "<< /Parent 6 0 R /T (b) /Kids [6 0 R] >>")).HasSignatures);

        [Theory]
        [InlineData(PdfCopyMode.Redraw)]
        [InlineData(PdfCopyMode.ObjectCopy)]
        public void ASignedSource_IsReportedAsNotCarried(PdfCopyMode mode)
        {
            var operation = PdfOperation.LoadBytes(Signed()).WithCopyMode(mode);
            var result = PdfPageSource.Open(operation.ToArray());

            Assert.Contains(operation.Warnings, w => w.StartsWith("digital signature not carried", StringComparison.Ordinal));
            Assert.False(result.HasSignatures);
        }

        [Fact]
        public void ASignedMergedFile_IsReportedOnce()
        {
            var signed = Signed();
            var operation = PdfOperation.LoadBytes(signed).MergeBytes(signed);
            operation.ToArray();

            Assert.Single(operation.Warnings, w => w.StartsWith("digital signature not carried", StringComparison.Ordinal));
        }

        [Fact]
        public void AnUnsignedSource_IsNotReported()
        {
            var operation = PdfOperation.LoadBytes(WithForm("<< /Fields [6 0 R] >>", "<< /FT /Sig /T (Sig1) >>"));
            operation.ToArray();

            Assert.Empty(operation.Warnings);
        }
    }
}
