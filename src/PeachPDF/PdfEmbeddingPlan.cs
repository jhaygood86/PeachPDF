#nullable enable

using PeachPDF.Rendering;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF
{
    /// <summary>
    /// The files one render call asks to embed (<see cref="PdfGenerateConfig.Attachments"/> and the Factur-X invoice of
    /// <see cref="PdfGenerateConfig.FacturX"/>), as the PDF writer's own embedded-file list and e-invoice marking. Building it validates what
    /// is specific to PeachPDF - the invoice XML and the file names it reserves; the writer validates the rest (file names, conformance
    /// levels, a file embedded twice) when the options are applied.
    /// </summary>
    internal sealed class PdfEmbeddingPlan
    {
        private PdfEmbeddingPlan(IReadOnlyList<PeachDrawing.PDF.PdfEmbeddedFile> files, PeachDrawing.PDF.PdfFacturXInfo? facturX)
        {
            Files = files;
            FacturX = facturX;
        }

        /// <summary>The files to embed, the invoice XML (when there is one) first.</summary>
        public IReadOnlyList<PeachDrawing.PDF.PdfEmbeddedFile> Files { get; }

        /// <summary>The e-invoice marking for the document's XMP metadata, or <see langword="null"/> when it is not an e-invoice.</summary>
        public PeachDrawing.PDF.PdfFacturXInfo? FacturX { get; }

        /// <summary>
        /// Builds the plan for <paramref name="config"/>, rejecting an invoice or attachment that is not valid. Each attachment is
        /// converted to a new object, so later edits to the caller's own cannot change what an already-established document promised.
        /// </summary>
        public static PdfEmbeddingPlan Create(PdfGenerateConfig config)
        {
            FacturXInvoice? invoice = null;
            if (config.FacturX is { } facturX)
            {
                invoice = FacturXInvoice.Create(facturX);
            }

            var files = new List<PeachDrawing.PDF.PdfEmbeddedFile>(config.Attachments.Count + 1);

            // The invoice XML goes first: the Factur-X specification calls it the first of the attachments.
            if (invoice is not null)
            {
                files.Add(invoice.Attachment.ToEngine());
            }

            foreach (var attachment in config.Attachments)
            {
                ArgumentNullException.ThrowIfNull(attachment, nameof(config.Attachments));

                if (invoice is not null && FacturXInvoice.ReservedFileNames.Contains(attachment.FileName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"PdfGenerateConfig.Attachments contains a file named '{attachment.FileName}', a name " +
                        "reserved for the Factur-X invoice XML - which PdfGenerateConfig.FacturX already embeds. A " +
                        "Factur-X document carries exactly one invoice data file.");
                }

                files.Add(attachment.ToEngine());
            }

            return new PdfEmbeddingPlan(files, invoice is null ? null : new PeachDrawing.PDF.PdfFacturXInfo(invoice.FileName, invoice.ConformanceLevel));
        }
    }
}
