using PeachPDF.PdfSharpCore;
using PeachPDF.PdfSharpCore.Drawing;

namespace PeachPDF.Tests.PdfSharpCoreTests
{
    public class PSSRTests
    {
        [Fact]
        public void Format_WithArgs_SubstitutesValues()
        {
            var result = PSSR.Format("Hello {0}, you are {1}", "World", 42);

            Assert.Equal("Hello World, you are 42", result);
        }

        [Fact]
        public void Format_NullFormat_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => PSSR.Format((string)null!));
        }

        [Fact]
        public void Format_InvalidFormatString_ReturnsErrorMessageInsteadOfThrowing()
        {
            var result = PSSR.Format("{0} {1}", "only one arg");

            Assert.Contains("UNEXPECTED ERROR", result);
        }

        [Theory]
        [InlineData("IndexOutOfRange")]
        [InlineData("ListEnumCurrentOutOfRange")]
        [InlineData("PageIndexOutOfRange")]
        [InlineData("OutlineIndexOutOfRange")]
        [InlineData("SetValueMustNotBeNull")]
        [InlineData("ObsoleteFunktionCalled")]
        [InlineData("OwningDocumentRequired")]
        [InlineData("FontDataReadOnly")]
        [InlineData("ErrorReadingFontData")]
        [InlineData("PointArrayEmpty")]
        [InlineData("NeedPenOrBrush")]
        [InlineData("InvalidPdf")]
        [InlineData("InvalidVersionNumber")]
        [InlineData("CannotHandleXRefStreams")]
        [InlineData("PasswordRequired")]
        [InlineData("InvalidPassword")]
        [InlineData("OwnerPasswordRequired")]
        [InlineData("CannotModify")]
        [InlineData("NameMustStartWithSlash")]
        [InlineData("MultiplePageInsert")]
        [InlineData("UnexpectedTokenInPdfFile")]
        public void StringProperties_AreNonEmpty(string propertyName)
        {
            var property = typeof(PSSR).GetProperty(propertyName, System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);
            Assert.NotNull(property);

            var value = (string)property!.GetValue(null)!;

            Assert.NotEmpty(value);
        }

        [Fact]
        public void InvalidValue_IncludesAllArguments()
        {
            var message = PSSR.InvalidValue(5, "MyProp", 0, 10);

            Assert.Contains("5", message);
            Assert.Contains("MyProp", message);
            Assert.Contains("0", message);
            Assert.Contains("10", message);
        }

        [Fact]
        public void FileNotFound_IncludesPath()
        {
            var message = PSSR.FileNotFound("C:/some/path.pdf");

            Assert.Contains("C:/some/path.pdf", message);
        }

        [Fact]
        public void PointArrayAtLeast_IncludesCount()
        {
            Assert.Contains("3", PSSR.PointArrayAtLeast(3));
        }

        [Fact]
        public void CannotChangeImmutableObject_IncludesTypeName()
        {
            Assert.Contains("XPen", PSSR.CannotChangeImmutableObject("XPen"));
        }

        [Fact]
        public void FontAlreadyAdded_IncludesFontName()
        {
            Assert.Contains("Arial", PSSR.FontAlreadyAdded("Arial"));
        }

        [Fact]
        public void NotImplementedForFontsRetrievedWithFontResolver_IncludesName()
        {
            Assert.Contains("MyFont", PSSR.NotImplementedForFontsRetrievedWithFontResolver("MyFont"));
        }

        [Fact]
        public void ImportPageNumberOutOfRange_IncludesAllArguments()
        {
            var message = PSSR.ImportPageNumberOutOfRange(5, 3, "doc.pdf");

            Assert.Contains("5", message);
            Assert.Contains("3", message);
            Assert.Contains("doc.pdf", message);
        }

        [Fact]
        public void CannotGetGlyphTypeface_IncludesFontName()
        {
            Assert.Contains("Times", PSSR.CannotGetGlyphTypeface("Times"));
        }

        [Fact]
        public void UnexpectedToken_ReturnsNonEmptyMessage()
        {
            var message = PSSR.UnexpectedToken("XYZ");

            Assert.NotEmpty(message);
        }

        [Fact]
        public void UnknownEncryption_ReturnsNonEmptyMessage()
        {
            Assert.NotEmpty(PSSR.UnknownEncryption);
        }

        [Fact]
        public void UserOrOwnerPasswordRequired_ReturnsNonEmptyMessage()
        {
            Assert.NotEmpty(PSSR.UserOrOwnerPasswordRequired);
        }

        [Fact]
        public void ResMngr_IsNotNull()
        {
            Assert.NotNull(PSSR.ResMngr);
        }

        [Fact]
        public void InappropriateColorSpace_NamesBothModeAndSpace()
        {
            var message = PSSR.InappropriateColorSpace(PeachPDF.PdfSharpCore.Pdf.PdfColorMode.Rgb, XColorSpace.Cmyk);

            Assert.Contains("RGB", message);
            Assert.Contains("CMYK", message);
        }

        [Fact]
        public void InappropriateColorSpace_UndefinedMode_UsesPlaceholder()
        {
            var message = PSSR.InappropriateColorSpace(PeachPDF.PdfSharpCore.Pdf.PdfColorMode.Undefined, XColorSpace.GrayScale);

            Assert.Contains("(undefined)", message);
            Assert.Contains("grayscale", message);
        }
    }
}
