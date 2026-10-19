using PeachDrawing.PDF.Engine;

namespace PeachPDF.Rendering
{
    /// <summary>
    /// Maps the public option types of <see cref="PdfGenerateConfig"/> onto the PDF writer's own. The two sets are kept apart on
    /// purpose: PeachPDF's are its public API, the writer's belong to the writer and change with it. Every enum is mirrored member for
    /// member (a test pins that), so a conversion is a cast.
    /// </summary>
    internal static class EngineOptions
    {
        public static PeachDrawing.PDF.Engine.PdfAConformance ToEngine(this PeachPDF.PdfAConformance value) => (PeachDrawing.PDF.Engine.PdfAConformance)(int)value;

        public static PeachDrawing.PDF.Engine.PdfXConformance ToEngine(this PeachPDF.PdfXConformance value) => (PeachDrawing.PDF.Engine.PdfXConformance)(int)value;

        public static PeachDrawing.PDF.Engine.PdfVersion ToEngine(this PeachPDF.PdfVersion value) => (PeachDrawing.PDF.Engine.PdfVersion)(int)value;

        public static PeachDrawing.PDF.Engine.ImageCompression ToEngine(this PeachPDF.ImageCompression value) => (PeachDrawing.PDF.Engine.ImageCompression)(int)value;

        public static PeachDrawing.PDF.Engine.PageSize ToEngine(this PeachPDF.PageSize value) => (PeachDrawing.PDF.Engine.PageSize)(int)value;

        public static PeachDrawing.PDF.Engine.PageOrientation ToEngine(this PeachPDF.PageOrientation value) => (PeachDrawing.PDF.Engine.PageOrientation)(int)value;

        public static PeachDrawing.PDF.Engine.PdfPermissions ToEngine(this PeachPDF.PdfPermissions value) => (PeachDrawing.PDF.Engine.PdfPermissions)(int)value;

        public static PeachDrawing.PDF.Engine.PdfEncryptionOptions? ToEngine(this PeachPDF.PdfEncryptionOptions? value) => value is null
            ? null
            : new PeachDrawing.PDF.Engine.PdfEncryptionOptions
            {
                UserPassword = value.UserPassword,
                OwnerPassword = value.OwnerPassword,
                Permissions = value.Permissions.ToEngine(),
                EncryptMetadata = value.EncryptMetadata,
            };

        public static PeachDrawing.PDF.Engine.ColorOptions? ToEngine(this PeachPDF.ColorOptions? value) => value is null
            ? null
            : new PeachDrawing.PDF.Engine.ColorOptions
            {
                OutputIntentProfile = value.OutputIntentProfile,
                OutputIntentIdentifier = value.OutputIntentIdentifier,
                BlackGeneration = (PeachDrawing.PDF.Engine.ColorBlackGeneration)(int)value.BlackGeneration,
                FallbackCmykProfile = value.FallbackCmykProfile,
                ConversionMode = (PeachDrawing.PDF.Engine.ColorConversionMode)(int)value.ConversionMode,
                ConvertToProfile = value.ConvertToProfile,
                RenderingIntent = (PeachDrawing.PDF.Engine.ColorRenderingIntent)(int)value.RenderingIntent,
                UseBlackPointCompensation = value.UseBlackPointCompensation,
            };
    }
}
