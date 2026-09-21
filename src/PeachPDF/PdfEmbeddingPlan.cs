#nullable enable

using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF
{
    /// <summary>
    /// The set of files one render call asks to embed in the document (<see cref="PdfGenerateConfig.Attachments"/>),
    /// validated and frozen. Embedding is a whole-document property - <c>AddPdfPages</c>/<c>AddPages</c>
    /// are repeatable, and a declarative document re-enters the render core once per page - so the plan is
    /// remembered on <see cref="PdfDocumentOptions"/>, compared on every later call, and applied exactly once.
    /// </summary>
    internal sealed class PdfEmbeddingPlan
    {
        /// <summary>A plan that embeds nothing.</summary>
        public static readonly PdfEmbeddingPlan Empty = new([], null);

        PdfEmbeddingPlan(IReadOnlyList<PdfAttachment> files, FacturXInvoice? facturX)
        {
            Files = files;
            FacturX = facturX;
        }

        /// <summary>The files to embed, in the order they are added to the document.</summary>
        public IReadOnlyList<PdfAttachment> Files { get; }

        /// <summary>
        /// The Factur-X invoice (<see cref="PdfGenerateConfig.FacturX"/>), whose XML is the first of
        /// <see cref="Files"/>, or <c>null</c> when the document is not an e-invoice.
        /// </summary>
        public FacturXInvoice? FacturX { get; }

        /// <summary>
        /// Builds the plan for <paramref name="config"/>, rejecting anything that cannot be embedded validly.
        /// Each <see cref="PdfAttachment"/> is copied, so later edits to the caller's objects (its name,
        /// type, relationship) cannot change what an already-established document promised. The
        /// <see cref="PdfAttachment.Data"/> array itself is shared, not duplicated - it is written once, and a
        /// caller who edits it in place afterwards is editing the bytes the document already holds.
        /// </summary>
        public static PdfEmbeddingPlan Create(PdfGenerateConfig config)
        {
            var isPdfA3 = config.PdfAConformance is PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A;

            FacturXInvoice? invoice = null;
            if (config.FacturX is { } facturX)
            {
                if (!isPdfA3)
                {
                    throw new InvalidOperationException(
                        $"PdfGenerateConfig.FacturX is set, but PdfAConformance is '{config.PdfAConformance}'. A " +
                        "Factur-X / ZUGFeRD invoice is a PDF/A-3 document - set PdfAConformance to PdfA3B, PdfA3U " +
                        "or PdfA3A (the specification recommends PdfA3A).");
                }

                invoice = FacturXInvoice.Create(facturX);
            }

            if (config.Attachments.Count == 0 && invoice is null)
                return Empty;

            if (config.PdfXConformance != PdfXConformance.None)
            {
                throw new InvalidOperationException(
                    $"PdfGenerateConfig.PdfXConformance is '{config.PdfXConformance}', but the document is asked to embed " +
                    "files (PdfGenerateConfig.Attachments or FacturX). PDF/X does not allow embedded files - remove " +
                    "one or the other.");
            }

            if (config.Attachments.Count > 0
                && config.PdfAConformance is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A
                    or PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A)
            {
                throw new InvalidOperationException(
                    $"PdfGenerateConfig.Attachments is not empty, but PdfAConformance is '{config.PdfAConformance}'. " +
                    "Embedding arbitrary files is what PDF/A-3 adds over PDF/A-1 and PDF/A-2 (PDF/A-1 forbids " +
                    "embedded files entirely; PDF/A-2 allows only PDF/A files) - use PdfAConformance.PdfA3B, " +
                    "PdfA3U or PdfA3A, or leave PdfAConformance at None.");
            }

            var files = new List<PdfAttachment>(config.Attachments.Count + 1);
            var names = new HashSet<string>(StringComparer.Ordinal);

            // The invoice XML goes first: the Factur-X specification calls it the first of the attachments.
            if (invoice is not null)
            {
                names.Add(invoice.FileName);
                files.Add(invoice.Attachment);
            }

            foreach (var attachment in config.Attachments)
            {
                ArgumentNullException.ThrowIfNull(attachment, nameof(config.Attachments));
                Validate(attachment);

                if (invoice is not null && FacturXInvoice.ReservedFileNames.Contains(attachment.FileName, StringComparer.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"PdfGenerateConfig.Attachments contains a file named '{attachment.FileName}', a name " +
                        "reserved for the Factur-X invoice XML - which PdfGenerateConfig.FacturX already embeds. A " +
                        "Factur-X document carries exactly one invoice data file.");
                }

                if (!names.Add(attachment.FileName))
                {
                    throw new InvalidOperationException(
                        $"PdfGenerateConfig.Attachments contains more than one file named '{attachment.FileName}'. " +
                        "Attachment file names must be unique within a document.");
                }

                files.Add(new PdfAttachment
                {
                    FileName = attachment.FileName,
                    Data = attachment.Data,
                    MimeType = attachment.MimeType,
                    Relationship = attachment.Relationship,
                    Description = attachment.Description,
                    ModificationDate = attachment.ModificationDate,
                });
            }

            return new PdfEmbeddingPlan(files, invoice);
        }

        static void Validate(PdfAttachment attachment)
        {
            if (string.IsNullOrWhiteSpace(attachment.FileName))
                throw new InvalidOperationException("A PdfAttachment has no FileName. Set PdfAttachment.FileName.");

            foreach (var c in attachment.FileName)
            {
                // The name is also the key of the /EmbeddedFiles name tree, whose keys are 8-bit strings.
                if (c > 0xFF)
                {
                    throw new InvalidOperationException(
                        $"PdfAttachment.FileName '{attachment.FileName}' contains a character outside Latin-1, " +
                        "which the PDF name tree that indexes attachments cannot carry. Use a Latin-1 file name.");
                }

                // A name is a leaf name, not a path: in a file specification '/' separates path components
                // (ISO 32000-1 §7.11.2), and a viewer that saves the attachment may treat either separator, or
                // a control character, as part of where it writes.
                if (c is '/' or '\\' || char.IsControl(c))
                {
                    throw new InvalidOperationException(
                        $"PdfAttachment.FileName '{attachment.FileName}' contains a path separator or control " +
                        "character. Use a plain file name, without any directory part.");
                }
            }

            if (attachment.FileName is "." or "..")
            {
                throw new InvalidOperationException(
                    $"PdfAttachment.FileName '{attachment.FileName}' is a directory reference, not a file name.");
            }

            if (attachment.Data is null)
            {
                throw new InvalidOperationException(
                    $"PdfAttachment '{attachment.FileName}' has no Data. Set PdfAttachment.Data (an empty array embeds an empty file).");
            }

            if (string.IsNullOrWhiteSpace(attachment.MimeType))
            {
                throw new InvalidOperationException(
                    $"PdfAttachment '{attachment.FileName}' has no MimeType. Set PdfAttachment.MimeType, e.g. \"text/xml\".");
            }
        }

        /// <summary>
        /// Whether <paramref name="other"/> asks for exactly the same files as this plan - same names,
        /// types, relationships, descriptions, dates and bytes, in the same order.
        /// </summary>
        public bool SameAs(PdfEmbeddingPlan other)
        {
            // The XMP block is rebuilt on every render call from this, so a call that dropped or changed the
            // e-invoice marking would silently rewrite the document's claim about itself.
            if (FacturX?.ConformanceLevel != other.FacturX?.ConformanceLevel)
                return false;

            if (Files.Count != other.Files.Count)
                return false;

            for (var i = 0; i < Files.Count; i++)
            {
                var a = Files[i];
                var b = other.Files[i];

                if (!string.Equals(a.FileName, b.FileName, StringComparison.Ordinal)
                    || !string.Equals(a.MimeType, b.MimeType, StringComparison.Ordinal)
                    || a.Relationship != b.Relationship
                    || !string.Equals(a.Description, b.Description, StringComparison.Ordinal)
                    || a.ModificationDate != b.ModificationDate
                    || !a.Data.AsSpan().SequenceEqual(b.Data))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// Embeds every file of the plan in <paramref name="document"/> (see
        /// <see cref="PdfCatalog.AddAttachment"/>). <paramref name="fallbackDate"/> stands in for a file
        /// with no modification date of its own.
        /// </summary>
        public void Apply(PdfDocument document, DateTimeOffset? fallbackDate)
        {
            foreach (var file in Files)
            {
                // .LocalDateTime for the same reason ApplyDocumentMetadata uses it: PdfDate writes the offset
                // of the DateTime's own Kind, so a UTC-Kind value would print the local machine's offset.
                var modified = (file.ModificationDate ?? fallbackDate ?? DateTimeOffset.UtcNow).LocalDateTime;

                var embeddedFile = new PdfEmbeddedFile(document, file.Data)
                {
                    MimeType = file.MimeType,
                    ModificationDate = modified,
                    CompressOnWrite = document.Options.CompressContentStreams,
                };

                var fileSpecification = new PdfFileSpecification(document, AsciiFileName(file.FileName), embeddedFile)
                {
                    UnicodeFileName = file.FileName,
                    AssociatedFileRelationship = RelationshipName(file.Relationship),
                };

                if (!string.IsNullOrEmpty(file.Description))
                    fileSpecification.Description = file.Description;

                document.Catalog.AddAttachment(file.FileName, fileSpecification);
            }
        }

        /// <summary>
        /// "/F" is the legacy, plain-ASCII file name; anything else in the name is written only to "/UF".
        /// </summary>
        static string AsciiFileName(string fileName)
        {
            var builder = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
                builder.Append(c <= 0x7F ? c : '_');

            return builder.ToString();
        }

        static string RelationshipName(PdfAttachmentRelationship relationship) => relationship switch
        {
            PdfAttachmentRelationship.Source => "/Source",
            PdfAttachmentRelationship.Data => "/Data",
            PdfAttachmentRelationship.Alternative => "/Alternative",
            PdfAttachmentRelationship.Supplement => "/Supplement",
            PdfAttachmentRelationship.Unspecified => "/Unspecified",
            _ => throw new ArgumentOutOfRangeException(nameof(relationship), relationship, null),
        };
    }
}
