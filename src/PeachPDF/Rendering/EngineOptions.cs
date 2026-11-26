namespace PeachPDF.Rendering
{
    /// <summary>
    /// Maps the public option types of <see cref="PdfGenerateConfig"/> onto the PDF writer's own (<c>PeachDrawing.PDF</c>). The two sets are
    /// kept apart on purpose: PeachPDF's are its public API, the writer's belong to the writer and change with it. Every enum is mirrored
    /// member for member (a test pins that), so a conversion is a cast.
    /// </summary>
    internal static class EngineOptions
    {
        public static PeachDrawing.PDF.PdfAConformance ToEngine(this PeachPDF.PdfAConformance value) => (PeachDrawing.PDF.PdfAConformance)(int)value;

        public static PeachDrawing.PDF.PdfXConformance ToEngine(this PeachPDF.PdfXConformance value) => (PeachDrawing.PDF.PdfXConformance)(int)value;

        public static PeachDrawing.PDF.PdfUAConformance ToEngine(this PeachPDF.PdfUAConformance value) => (PeachDrawing.PDF.PdfUAConformance)(int)value;

        public static PeachDrawing.PDF.PdfVersion ToEngine(this PeachPDF.PdfVersion value) => (PeachDrawing.PDF.PdfVersion)(int)value;

        public static PeachDrawing.PDF.ImageCompression ToEngine(this PeachPDF.ImageCompression value) => (PeachDrawing.PDF.ImageCompression)(int)value;

        public static PeachDrawing.PDF.PdfPermissions ToEngine(this PeachPDF.PdfPermissions value) => (PeachDrawing.PDF.PdfPermissions)(int)value;

        public static PeachDrawing.PDF.PdfEmbeddedFileRelationship ToEngine(this PeachPDF.PdfAttachmentRelationship value) => (PeachDrawing.PDF.PdfEmbeddedFileRelationship)(int)value;

        public static PeachDrawing.PDF.PdfPageMode ToEngine(this PeachPDF.PdfPageMode value) => (PeachDrawing.PDF.PdfPageMode)(int)value;

        public static PeachDrawing.PDF.PdfPageLayout ToEngine(this PeachPDF.PdfPageLayout value) => (PeachDrawing.PDF.PdfPageLayout)(int)value;

        public static PeachDrawing.PDF.PdfOpenAction ToEngine(this PeachPDF.PdfOpenAction value) => (PeachDrawing.PDF.PdfOpenAction)(int)value;

        public static PeachDrawing.PDF.PdfViewerOptions? ToEngine(this PeachPDF.PdfViewerOptions? value) => value is null
            ? null
            : new PeachDrawing.PDF.PdfViewerOptions
            {
                DisplayDocTitle = value.DisplayDocTitle,
                HideToolbar = value.HideToolbar,
                HideMenubar = value.HideMenubar,
                HideWindowUI = value.HideWindowUI,
                FitWindow = value.FitWindow,
                CenterWindow = value.CenterWindow,
                PrintScaling = value.PrintScaling is { } scaling ? (PeachDrawing.PDF.PdfPrintScaling)(int)scaling : null,
                Duplex = value.Duplex is { } duplex ? (PeachDrawing.PDF.PdfDuplex)(int)duplex : null,
            };

        public static PeachDrawing.PDF.PdfEncryptionOptions? ToEngine(this PeachPDF.PdfEncryptionOptions? value) => value is null
            ? null
            : new PeachDrawing.PDF.PdfEncryptionOptions
            {
                UserPassword = value.UserPassword,
                OwnerPassword = value.OwnerPassword,
                Permissions = value.Permissions.ToEngine(),
                EncryptMetadata = value.EncryptMetadata,
            };

        public static PeachDrawing.PDF.PdfSigningOptions? ToEngine(this PeachPDF.PdfSigningOptions? value)
        {
            if (value is null)
                return null;

            var signing = new PeachDrawing.PDF.PdfSigningOptions
            {
                Certificate = value.Certificate,
                PrivateKey = value.PrivateKey,
                KeyPin = value.KeyPin,
                AllowKeyPrompt = value.AllowKeyPrompt,
                HashAlgorithm = value.HashAlgorithm,
                Format = (PeachDrawing.PDF.PdfSignatureFormat)(int)value.Format,
                Timestamp = value.TimestampAuthority is null
                    ? null
                    : PeachDrawing.PDF.PdfTimestampOptions.FromHttpAuthority(value.TimestampAuthority, value.TimestampClient ?? new System.Net.Http.HttpClient()),
                Box = value.Box is null
                    ? null
                    : new PeachDrawing.PDF.PdfSignatureBox
                    {
                        PageNumber = value.Box.PageNumber,
                        Bounds = new PeachDrawing.Core.Rect(value.Box.X, value.Box.Y, value.Box.Width, value.Box.Height),
                        Text = value.Box.Text,
                        FontSize = value.Box.FontSize,
                    },
                SignerName = value.SignerName,
                Reason = value.Reason,
                Location = value.Location,
                ContactInfo = value.ContactInfo,
            };

            foreach (var certificate in value.AdditionalCertificates)
                signing.AdditionalCertificates.Add(certificate);

            return signing;
        }

        public static PeachDrawing.PDF.PdfEmbeddedFile ToEngine(this PeachPDF.PdfAttachment value) => new()
        {
            FileName = value.FileName,
            Data = value.Data,
            MimeType = value.MimeType,
            Relationship = value.Relationship.ToEngine(),
            Description = value.Description,
            ModificationDate = value.ModificationDate,
        };

        public static PeachDrawing.PDF.ColorOptions? ToEngine(this PeachPDF.ColorOptions? value) => value is null
            ? null
            : new PeachDrawing.PDF.ColorOptions
            {
                OutputIntentProfile = value.OutputIntentProfile,
                OutputIntentIdentifier = value.OutputIntentIdentifier,
                BlackGeneration = (PeachDrawing.PDF.ColorBlackGeneration)(int)value.BlackGeneration,
                FallbackCmykProfile = value.FallbackCmykProfile,
                ConversionMode = (PeachDrawing.PDF.ColorConversionMode)(int)value.ConversionMode,
                ConvertToProfile = value.ConvertToProfile,
                RenderingIntent = (PeachDrawing.PDF.ColorRenderingIntent)(int)value.RenderingIntent,
                UseBlackPointCompensation = value.UseBlackPointCompensation,
            };
    }
}
