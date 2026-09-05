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
    /// whenever <see cref="PeachPDF.PdfGenerateConfig.EnableXmpMetadata"/> or a
    /// <see cref="PeachPDF.PdfGenerateConfig.PdfAConformance"/> level is requested. Built entirely with
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
            IEnumerable<XElement> customProperties)
            : base(document)
        {
            Elements.SetName(Keys.Type, "/Metadata");
            Elements.SetName(Keys.Subtype, "/XML");

            var packetBytes = BuildPacket(info, creationDate, conformance, customProperties);

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
