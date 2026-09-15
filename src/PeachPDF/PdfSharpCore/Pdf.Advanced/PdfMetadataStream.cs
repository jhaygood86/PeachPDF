#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// The document catalog's XMP metadata stream (<c>/Metadata</c>, ISO 32000-1 §14.3.2) - written
    /// whenever <see cref="PeachPDF.PdfGenerateConfig.EnableXmpMetadata"/>, a
    /// <see cref="PeachPDF.PdfGenerateConfig.PdfAConformance"/> level, or a
    /// <see cref="PeachPDF.PdfXConformance"/> level is requested. Built entirely with
    /// <see cref="System.Xml"/>/<see cref="System.Xml.Linq"/> (never hand-concatenated strings), so the
    /// packet is well-formed by construction.
    /// </summary>
    /// <remarks>
    /// Fields are populated from the already-populated <see cref="PdfDocumentInformation"/> object
    /// (call this after the Document Information dictionary has been filled in), not re-derived
    /// independently - this guarantees Info-dict/XMP consistency by construction, which PDF/A
    /// validators check.
    /// </remarks>
    internal sealed class PdfMetadataStream : PdfDictionary
    {
        static readonly XNamespace XNs = "adobe:ns:meta/";
        static readonly XNamespace RdfNs = "http://www.w3.org/1999/02/22-rdf-syntax-ns#";
        static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
        static readonly XNamespace PdfNs = "http://ns.adobe.com/pdf/1.3/";
        static readonly XNamespace XmpNs = "http://ns.adobe.com/xap/1.0/";
        static readonly XNamespace PdfaidNs = "http://www.aiim.org/pdfa/ns/id/";

        // The original Adobe-defined PDF/X identification schema (used by every PDF/X level, including
        // X-4 - confirmed against the widely-deployed LaTeX "pdfx" package's own XMP template, which is
        // the clearest real-world reference available short of the paywalled ISO 15930 text itself: see
        // PdfXIdentifiers's remarks) and the newer ISO-registered schema (additive, X-4-only).
        static readonly XNamespace PdfxNs = "http://ns.adobe.com/pdfx/1.3/";
        static readonly XNamespace PdfxidNs = "http://www.npes.org/pdfx/ns/id/";

        /// <summary>
        /// Creates the metadata stream and its XMP packet. <paramref name="creationDate"/> must be
        /// resolved (never "unknown") by the caller before this is constructed - see
        /// <c>PdfGenerator.ApplyDocumentMetadata</c>'s missing-date validation.
        /// </summary>
        public PdfMetadataStream(
            PdfDocument document,
            PdfDocumentInformation info,
            DateTimeOffset creationDate,
            PdfAConformance conformance,
            PeachPDF.PdfXConformance pdfXConformance,
            IEnumerable<XElement> customProperties)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/Metadata");
            Elements.SetName(Keys.Subtype, "/XML");

            var packetBytes = BuildPacket(info, creationDate, conformance, pdfXConformance, customProperties);

            // Per ISO 19005 §6.7.4 the metadata stream must not specify a /Filter - every other
            // stream writer in this codebase sets Elements[PdfStream.Keys.Filter] explicitly
            // per-stream, so simply not setting it here already leaves the stream uncompressed.
            Stream = new PdfStream(packetBytes, this);
            Elements[PdfStream.Keys.Length] = new PdfInteger(packetBytes.Length);
        }

        static byte[] BuildPacket(
            PdfDocumentInformation info,
            DateTimeOffset creationDate,
            PdfAConformance conformance,
            PeachPDF.PdfXConformance pdfXConformance,
            IEnumerable<XElement> customProperties)
        {
            var description = new XElement(RdfNs + "Description",
                new XAttribute(RdfNs + "about", ""),
                new XAttribute(XNamespace.Xmlns + "dc", DcNs),
                new XAttribute(XNamespace.Xmlns + "pdf", PdfNs),
                new XAttribute(XNamespace.Xmlns + "xmp", XmpNs));

            if (!string.IsNullOrEmpty(info.Title))
                description.Add(new XElement(DcNs + "title",
                    new XElement(RdfNs + "Alt",
                        new XElement(RdfNs + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), info.Title))));

            if (!string.IsNullOrEmpty(info.Author))
                description.Add(new XElement(DcNs + "creator",
                    new XElement(RdfNs + "Seq", new XElement(RdfNs + "li", info.Author))));

            if (!string.IsNullOrEmpty(info.Subject))
                description.Add(new XElement(DcNs + "description",
                    new XElement(RdfNs + "Alt",
                        new XElement(RdfNs + "li", new XAttribute(XNamespace.Xml + "lang", "x-default"), info.Subject))));

            if (!string.IsNullOrEmpty(info.Producer))
                description.Add(new XElement(PdfNs + "Producer", info.Producer));

            if (!string.IsNullOrEmpty(info.Keywords))
                description.Add(new XElement(PdfNs + "Keywords", info.Keywords));

            var xmpDate = creationDate.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture);
            description.Add(new XElement(XmpNs + "CreateDate", xmpDate));
            description.Add(new XElement(XmpNs + "ModifyDate", xmpDate));
            if (!string.IsNullOrEmpty(info.Creator))
                description.Add(new XElement(XmpNs + "CreatorTool", info.Creator));

            // pdfaid:part/pdfaid:conformance are derived from the requested conformance level, not
            // independently settable - see PdfDocumentMetadata.CustomXmpProperties's own remarks on
            // why that matters.
            if (conformance != PdfAConformance.None)
            {
                var (part, level) = PdfAConformanceIdentifiers(conformance);
                description.Add(new XAttribute(XNamespace.Xmlns + "pdfaid", PdfaidNs));
                description.Add(new XElement(PdfaidNs + "part", part));
                description.Add(new XElement(PdfaidNs + "conformance", level));
            }

            // pdfx:GTS_PDFXVersion/GTS_PDFXConformance mirror the Info-dictionary keys PdfGenerator writes
            // alongside this stream (PdfXIdentifiers is the shared source of truth for both) - written for
            // every PDF/X level under the original Adobe-defined "pdfx" schema. PDF/X-4 additionally gets
            // pdfxid:GTS_PDFXVersion under the newer ISO-registered schema, which ISO 15930-7 names as the
            // primary identification mechanism for that level (see PdfXIdentifiers's remarks).
            if (pdfXConformance != PeachPDF.PdfXConformance.None)
            {
                var (version, xConformance) = PdfXIdentifiers(pdfXConformance);
                description.Add(new XAttribute(XNamespace.Xmlns + "pdfx", PdfxNs));
                description.Add(new XElement(PdfxNs + "GTS_PDFXVersion", version));
                if (xConformance is not null)
                    description.Add(new XElement(PdfxNs + "GTS_PDFXConformance", xConformance));

                if (pdfXConformance == PeachPDF.PdfXConformance.X4)
                {
                    description.Add(new XAttribute(XNamespace.Xmlns + "pdfxid", PdfxidNs));
                    description.Add(new XElement(PdfxidNs + "GTS_PDFXVersion", version));
                }
            }

            var rdf = new XElement(RdfNs + "RDF", description);

            // Deep-copy (new XElement(custom), not custom itself) - an XElement already has a parent
            // once added to a tree, so reusing the caller's own element instance directly would strip
            // it out of whatever tree it already belongs to.
            foreach (var custom in customProperties)
                rdf.Add(new XElement(custom));

            var xmpMeta = new XElement(XNs + "xmpmeta", new XAttribute(XNamespace.Xmlns + "x", XNs), rdf);

            using var memoryStream = new MemoryStream();
            using (var writer = XmlWriter.Create(memoryStream, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
                OmitXmlDeclaration = true,
                Indent = false,
            }))
            {
                // The conventional XMP packet wrapper (ISO 16684-1) - "begin" carries a literal U+FEFF
                // so a reader can sniff the packet's byte order/encoding from the PI itself.
                writer.WriteProcessingInstruction("xpacket", "begin=\"﻿\" id=\"W5M0MpCehiHzreSzNTczkc9d\"");
                xmpMeta.WriteTo(writer);
                writer.WriteProcessingInstruction("xpacket", "end=\"w\"");
            }

            return memoryStream.ToArray();
        }

        static (string Part, string Level) PdfAConformanceIdentifiers(PdfAConformance conformance) => conformance switch
        {
            PdfAConformance.PdfA1B => ("1", "B"),
            PdfAConformance.PdfA1A => ("1", "A"),
            PdfAConformance.PdfA2B => ("2", "B"),
            PdfAConformance.PdfA2U => ("2", "U"),
            PdfAConformance.PdfA2A => ("2", "A"),
            PdfAConformance.PdfA3B => ("3", "B"),
            PdfAConformance.PdfA3U => ("3", "U"),
            PdfAConformance.PdfA3A => ("3", "A"),
            _ => throw new ArgumentOutOfRangeException(nameof(conformance), conformance, null),
        };

        /// <summary>
        /// The <c>GTS_PDFXVersion</c>/<c>GTS_PDFXConformance</c> identifier strings for a
        /// <see cref="PeachPDF.PdfXConformance"/> level - shared by this class's XMP <c>pdfx:</c> block and
        /// <c>PdfGenerator.RenderPagesCore</c>'s Info-dictionary <c>/GTS_PDFXVersion</c>/<c>/GTS_PDFXConformance</c>
        /// keys, so both mechanisms always agree (PDF/A validators check exactly this kind of Info-dict/XMP
        /// consistency; there is no reason a PDF/X reader wouldn't too).
        /// </summary>
        /// <remarks>
        /// <para>
        /// PeachPDF's <see cref="PeachPDF.PdfXConformance"/> levels target the 2003-era ISO revisions
        /// (ISO 15930-4 for X1a, ISO 15930-6 for X3 - PDF 1.4 base for both) rather than the original
        /// 2001/2002 revisions (PDF 1.3 base) - see <c>PdfGenerator.EstablishDocumentOptions</c>'s version
        /// -setting block. ISO 15930-7 (X4, PDF 1.6 base) has only the one revision.
        /// </para>
        /// <para>
        /// The real ISO 15930 text is paywalled, so this was verified by cross-referencing multiple
        /// independent secondary sources - ISO's own standard abstracts, IDEAlliance/Adobe PDF/X
        /// application notes, the widely-used <c>iText</c> library's <c>PdfXConformanceImp</c> reference
        /// implementation, and (most precisely) the LaTeX <c>pdfx</c> package's <c>pdfx.xmp</c> XMP
        /// template, whose real conditional logic gives an exact, internally-consistent picture: every
        /// level writes <c>pdfx:GTS_PDFXVersion</c> as <c>"PDF/X-{part}{conformance-letter}"</c>, with a
        /// trailing <c>":{year}"</c> only for a part below 4 (X1a/X3 get a year suffix, X4 doesn't);
        /// <c>pdfx:GTS_PDFXConformance</c> is written only for a part below 3 (X1a only, among PeachPDF's
        /// three levels - X3/X4 don't get it); and only X4 (part &gt; 3) additionally gets
        /// <c>pdfxid:GTS_PDFXVersion</c> under the newer ISO-registered schema. Called out here, and in the
        /// matching recent-fix note, as best-available verification rather than a primary-source citation -
        /// the same honesty this repo's PDF/A work already applied to its own veraPDF-only gaps.
        /// </para>
        /// </remarks>
        internal static (string Version, string? Conformance) PdfXIdentifiers(PeachPDF.PdfXConformance conformance) => conformance switch
        {
            PeachPDF.PdfXConformance.X1a => ("PDF/X-1a:2003", "PDF/X-1a:2003"),
            PeachPDF.PdfXConformance.X3 => ("PDF/X-3:2003", null),
            PeachPDF.PdfXConformance.X4 => ("PDF/X-4", null),
            _ => throw new ArgumentOutOfRangeException(nameof(conformance), conformance, null),
        };

        /// <summary>
        /// Predefined keys of this dictionary.
        /// </summary>
        internal sealed class Keys : KeysBase
        {
            /// <summary>(Required) Must be Metadata for a metadata stream dictionary.</summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "Metadata")]
            public const string Type = "/Type";

            /// <summary>(Required) Must be XML for an XMP metadata stream.</summary>
            [KeyInfo(KeyType.Name | KeyType.Required, FixedValue = "XML")]
            public const string Subtype = "/Subtype";

            /// <summary>
            /// Gets the KeysMeta for these keys.
            /// </summary>
            public static DictionaryMeta Meta
            {
                get { return _meta ??= CreateMeta(typeof(Keys)); }
            }
            static DictionaryMeta _meta = null!;
        }

        /// <summary>
        /// Gets the KeysMeta of this dictionary type.
        /// </summary>
        internal override DictionaryMeta Meta
        {
            get { return Keys.Meta; }
        }
    }
}
