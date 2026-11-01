#nullable enable

using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using EngineEmbeddedFile = PeachDrawing.PDF.Engine.Advanced.PdfEmbeddedFile;

namespace PeachDrawing.PDF
{
    /// <summary>
    /// The set of files one application of <see cref="PdfComposerOptions"/> asks to embed, validated and frozen. Embedding is a
    /// whole-document property - options can be applied repeatedly - so the plan is remembered on the document's options, compared on
    /// every later application, and applied exactly once.
    /// </summary>
    internal sealed class EmbeddedFilePlan
    {
        /// <summary>A plan that embeds nothing.</summary>
        public static readonly EmbeddedFilePlan Empty = new([]);

        private EmbeddedFilePlan(IReadOnlyList<PdfEmbeddedFile> files)
        {
            Files = files;
        }

        /// <summary>The files to embed, in the order they are added to the document.</summary>
        public IReadOnlyList<PdfEmbeddedFile> Files { get; }

        /// <summary>
        /// Builds the plan for <paramref name="options"/>, rejecting anything that cannot be embedded validly. Each file is copied, so
        /// later edits to the caller's objects cannot change what an already-established document promised. The
        /// <see cref="PdfEmbeddedFile.Data"/> array itself is shared, not duplicated - it is written once.
        /// </summary>
        public static EmbeddedFilePlan Create(PdfComposerOptions options)
        {
            var isPdfA3 = options.Conformance is PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A;

            if (options.FacturX is not null && !isPdfA3)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.FacturX is set, but Conformance is '{options.Conformance}'. A " +
                    "Factur-X / ZUGFeRD invoice is a PDF/A-3 document - set Conformance to PdfA3B, PdfA3U " +
                    "or PdfA3A (the specification recommends PdfA3A).");
            }

            // ISO 19005-4 (veraPDF rule 6.9 test 5): a PDF/A-4f file must contain at least one embedded file - that is what the f
            // variant is for. Writing one without any would claim a conformance the file does not have.
            if (options.Conformance == PdfAConformance.PdfA4F && options.EmbeddedFiles.Count == 0)
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Conformance is PdfA4F, but PdfComposerOptions.EmbeddedFiles is empty. A PDF/A-4f file " +
                    "must embed at least one file - add one, or use PdfAConformance.PdfA4.");
            }

            if (options.EmbeddedFiles.Count == 0)
                return Empty;

            if (options.XConformance != PdfXConformance.None)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.XConformance is '{options.XConformance}', but the document is asked to embed " +
                    "files (PdfComposerOptions.EmbeddedFiles). PDF/X does not allow embedded files - remove " +
                    "one or the other.");
            }

            if (options.Conformance is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A
                or PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.EmbeddedFiles is not empty, but Conformance is '{options.Conformance}'. " +
                    "Embedding arbitrary files is what PDF/A-3 adds over PDF/A-1 and PDF/A-2 (PDF/A-1 forbids " +
                    "embedded files entirely; PDF/A-2 allows only PDF/A files) - use PdfAConformance.PdfA3B, " +
                    "PdfA3U or PdfA3A, or leave Conformance at None.");
            }

            if (options.Conformance is PdfAConformance.PdfA4 or PdfAConformance.PdfA4E)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.EmbeddedFiles is not empty, but Conformance is '{options.Conformance}'. " +
                    "Among the PDF/A-4 levels only PdfA4F permits embedded files - use PdfAConformance.PdfA4F, " +
                    "PdfA3B/PdfA3U/PdfA3A, or leave Conformance at None.");
            }

            var files = new List<PdfEmbeddedFile>(options.EmbeddedFiles.Count);
            var names = new HashSet<string>(StringComparer.Ordinal);

            foreach (var file in options.EmbeddedFiles)
            {
                ArgumentNullException.ThrowIfNull(file, nameof(options.EmbeddedFiles));
                Validate(file);

                if (!names.Add(file.FileName))
                {
                    throw new InvalidOperationException(
                        $"PdfComposerOptions.EmbeddedFiles contains more than one file named '{file.FileName}'. " +
                        "Embedded file names must be unique within a document.");
                }

                files.Add(new PdfEmbeddedFile
                {
                    FileName = file.FileName,
                    Data = file.Data,
                    MimeType = file.MimeType,
                    Relationship = file.Relationship,
                    Description = file.Description,
                    ModificationDate = file.ModificationDate,
                });
            }

            if (options.FacturX is { } facturX && !names.Contains(facturX.FileName))
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.FacturX names the invoice file '{facturX.FileName}', which is not one of " +
                    "PdfComposerOptions.EmbeddedFiles. Embed the invoice XML under that name.");
            }

            return new EmbeddedFilePlan(files);
        }

        private static void Validate(PdfEmbeddedFile file)
        {
            if (string.IsNullOrWhiteSpace(file.FileName))
                throw new InvalidOperationException("A PdfEmbeddedFile has no FileName. Set PdfEmbeddedFile.FileName.");

            foreach (var c in file.FileName)
            {
                // The name is also the key of the /EmbeddedFiles name tree, whose keys are 8-bit strings.
                if (c > 0xFF)
                {
                    throw new InvalidOperationException(
                        $"PdfEmbeddedFile.FileName '{file.FileName}' contains a character outside Latin-1, " +
                        "which the PDF name tree that indexes embedded files cannot carry. Use a Latin-1 file name.");
                }

                // A name is a leaf name, not a path: in a file specification '/' separates path components (ISO 32000-1 §7.11.2), and
                // a viewer that saves the file may treat either separator, or a control character, as part of where it writes.
                if (c is '/' or '\\' || char.IsControl(c))
                {
                    throw new InvalidOperationException(
                        $"PdfEmbeddedFile.FileName '{file.FileName}' contains a path separator or control " +
                        "character. Use a plain file name, without any directory part.");
                }
            }

            if (file.FileName is "." or "..")
            {
                throw new InvalidOperationException(
                    $"PdfEmbeddedFile.FileName '{file.FileName}' is a directory reference, not a file name.");
            }

            if (file.Data is null)
            {
                throw new InvalidOperationException(
                    $"PdfEmbeddedFile '{file.FileName}' has no Data. Set PdfEmbeddedFile.Data (an empty array embeds an empty file).");
            }

            if (string.IsNullOrWhiteSpace(file.MimeType))
            {
                throw new InvalidOperationException(
                    $"PdfEmbeddedFile '{file.FileName}' has no MimeType. Set PdfEmbeddedFile.MimeType, e.g. \"text/xml\".");
            }
        }

        /// <summary>
        /// Whether <paramref name="other"/> asks for exactly the same files as this plan - same names, types, relationships,
        /// descriptions, dates and bytes, in the same order.
        /// </summary>
        public bool SameAs(EmbeddedFilePlan other)
        {
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
        /// Embeds every file of the plan in <paramref name="document"/>. <paramref name="fallbackDate"/> stands in for a file with no
        /// modification date of its own.
        /// </summary>
        public void Apply(PdfDocument document, DateTimeOffset? fallbackDate)
        {
            foreach (var file in Files)
            {
                // .LocalDateTime for the same reason the creation date uses it: PdfDate writes the offset of the DateTime's own Kind, so
                // a UTC-Kind value would print the local machine's offset.
                var modified = (file.ModificationDate ?? fallbackDate ?? DateTimeOffset.UtcNow).LocalDateTime;

                var embeddedFile = new EngineEmbeddedFile(document, file.Data)
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

        /// <summary>"/F" is the legacy, plain-ASCII file name; anything else in the name is written only to "/UF".</summary>
        private static string AsciiFileName(string fileName)
        {
            var builder = new StringBuilder(fileName.Length);
            foreach (var c in fileName)
                builder.Append(c <= 0x7F ? c : '_');

            return builder.ToString();
        }

        private static string RelationshipName(PdfEmbeddedFileRelationship relationship) => relationship switch
        {
            PdfEmbeddedFileRelationship.Source => "/Source",
            PdfEmbeddedFileRelationship.Data => "/Data",
            PdfEmbeddedFileRelationship.Alternative => "/Alternative",
            PdfEmbeddedFileRelationship.Supplement => "/Supplement",
            PdfEmbeddedFileRelationship.Unspecified => "/Unspecified",
            _ => throw new ArgumentOutOfRangeException(nameof(relationship), relationship, null),
        };
    }
}
