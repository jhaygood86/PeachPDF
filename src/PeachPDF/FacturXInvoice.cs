#nullable enable

using System;
using System.IO;
using System.Linq;
using System.Xml;
using System.Xml.Linq;

namespace PeachPDF
{
    /// <summary>
    /// A validated <see cref="FacturXOptions"/>: the profile (derived from the XML, and checked against any
    /// explicit setting), the file name and <c>/AFRelationship</c> the specification calls for, and the
    /// <see cref="PdfAttachment"/> that embeds the XML. Encodes the Factur-X 1.09.2 / ZUGFeRD 2.5.2 rules
    /// (chapters 6.2 and 7.7 and the HYBRID business rules); see <c>docs/usage-examples.md</c> for the
    /// user-facing description.
    /// </summary>
    internal sealed class FacturXInvoice
    {
        const string CiiNamespace = "urn:un:unece:uncefact:data:standard:CrossIndustryInvoice:100";

        // The guideline identifiers (BT-24) each profile's XML carries, from the Schematron shipped with the
        // specification. The ZUGFeRD 2.x spellings are accepted by that Schematron too.
        const string MinimumGuideline = "urn:factur-x.eu:1p0:minimum";
        const string BasicWlGuideline = "urn:factur-x.eu:1p0:basicwl";
        const string En16931Guideline = "urn:cen.eu:en16931:2017";

        // XRechnung is defined by KoSIT, not by the Factur-X package, so it is matched by prefix - the
        // version suffix ("3.0", ...) changes with every XRechnung release.
        const string XRechnungGuidelinePrefix = "urn:cen.eu:en16931:2017#compliant#urn:xeinkauf.de:kosit:xrechnung_";

        /// <summary>The names the specification reserves for the embedded invoice XML.</summary>
        public static readonly string[] ReservedFileNames = ["factur-x.xml", "xrechnung.xml"];

        FacturXInvoice(FacturXProfile profile, PdfAttachment attachment)
        {
            Profile = profile;
            Attachment = attachment;
        }

        public FacturXProfile Profile { get; }

        /// <summary>The attachment that embeds the invoice XML (<c>text/xml</c>, named per the profile).</summary>
        public PdfAttachment Attachment { get; }

        /// <summary><c>xrechnung.xml</c> for the XRechnung profile, <c>factur-x.xml</c> for all the others.</summary>
        public string FileName => Attachment.FileName;

        /// <summary>The <c>fx:ConformanceLevel</c> XMP value for <see cref="Profile"/>.</summary>
        public string ConformanceLevel => ConformanceLevelOf(Profile);

        public static string ConformanceLevelOf(FacturXProfile profile) => profile switch
        {
            FacturXProfile.Minimum => "MINIMUM",
            FacturXProfile.BasicWl => "BASIC WL",
            FacturXProfile.Basic => "BASIC",
            FacturXProfile.En16931 => "EN 16931",
            FacturXProfile.Extended => "EXTENDED",
            FacturXProfile.XRechnung => "XRECHNUNG",
            _ => throw new ArgumentOutOfRangeException(nameof(profile), profile, null),
        };

        /// <summary>
        /// Validates <paramref name="options"/> and builds the invoice attachment. Throws
        /// <see cref="InvalidOperationException"/> for anything the specification does not allow.
        /// </summary>
        public static FacturXInvoice Create(FacturXOptions options)
        {
            if (options.Xml is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    "PdfGenerateConfig.FacturX is set, but FacturXOptions.Xml is empty. Supply the invoice's " +
                    "Cross Industry Invoice XML.");
            }

            var guideline = ReadGuideline(options.Xml);
            var derived = ProfileOfGuideline(guideline);

            FacturXProfile profile;
            if (options.Profile is { } requested)
            {
                if (derived is { } actual && actual != requested)
                {
                    throw new InvalidOperationException(
                        $"FacturXOptions.Profile is '{requested}', but the invoice XML declares the guideline " +
                        $"'{guideline}', which is the '{actual}' profile. A document must not claim a profile " +
                        "its XML does not meet - remove Profile to derive it, or correct one of them.");
                }

                profile = requested;
            }
            else if (derived is { } actual)
            {
                profile = actual;
            }
            else
            {
                throw new InvalidOperationException(
                    $"The invoice XML declares the guideline '{guideline}', which PeachPDF does not recognise as a " +
                    "Factur-X profile. Set FacturXOptions.Profile to state the profile explicitly.");
            }

            var relationship = options.Relationship ?? DefaultRelationship(profile);
            if (!IsPermitted(profile, relationship))
            {
                throw new InvalidOperationException(
                    $"FacturXOptions.Relationship '{relationship}' is not allowed for the '{profile}' profile. " +
                    $"The specification permits: {string.Join(", ", Permitted(profile))}.");
            }

            var attachment = new PdfAttachment
            {
                FileName = profile == FacturXProfile.XRechnung ? "xrechnung.xml" : "factur-x.xml",
                Data = options.Xml,
                MimeType = "text/xml",
                Relationship = relationship,
                Description = options.Description,
            };

            return new FacturXInvoice(profile, attachment);
        }

        /// <summary>
        /// Reads the guideline identifier (<c>GuidelineSpecifiedDocumentContextParameter/ID</c>, BT-24) from a
        /// Cross Industry Invoice. Throws when the XML is malformed, is not a CII invoice, or declares none.
        /// </summary>
        static string ReadGuideline(byte[] xml)
        {
            XDocument document;
            try
            {
                using var stream = new MemoryStream(xml, writable: false);
                using var reader = XmlReader.Create(stream, new XmlReaderSettings
                {
                    // An invoice never needs a DTD, and refusing one closes the XML-bomb/XXE class of input.
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                });
                document = XDocument.Load(reader);
            }
            catch (XmlException ex)
            {
                throw new InvalidOperationException(
                    $"FacturXOptions.Xml is not well-formed XML: {ex.Message}", ex);
            }

            var root = document.Root;
            if (root is null || root.Name != XName.Get("CrossIndustryInvoice", CiiNamespace))
            {
                throw new InvalidOperationException(
                    "FacturXOptions.Xml is not a Cross Industry Invoice: its root element must be " +
                    $"'CrossIndustryInvoice' in the namespace '{CiiNamespace}'.");
            }

            var guideline = root.Descendants()
                .Where(e => e.Name.LocalName == "GuidelineSpecifiedDocumentContextParameter")
                .SelectMany(e => e.Elements())
                .FirstOrDefault(e => e.Name.LocalName == "ID")?.Value.Trim();

            if (string.IsNullOrEmpty(guideline))
            {
                throw new InvalidOperationException(
                    "The invoice XML declares no guideline identifier (ExchangedDocumentContext/" +
                    "GuidelineSpecifiedDocumentContextParameter/ID, business term BT-24), so its Factur-X profile " +
                    "cannot be determined. A Factur-X invoice must carry one.");
            }

            return guideline;
        }

        static FacturXProfile? ProfileOfGuideline(string guideline)
        {
            if (guideline == MinimumGuideline)
                return FacturXProfile.Minimum;

            if (guideline == BasicWlGuideline)
                return FacturXProfile.BasicWl;

            if (guideline == En16931Guideline)
                return FacturXProfile.En16931;

            if (guideline is "urn:cen.eu:en16931:2017#compliant#urn:factur-x.eu:1p0:basic"
                or "urn:cen.eu:en16931:2017#compliant#urn:zugferd.de:2p0:basic")
            {
                return FacturXProfile.Basic;
            }

            if (guideline is "urn:cen.eu:en16931:2017#conformant#urn:factur-x.eu:1p0:extended"
                or "urn:cen.eu:en16931:2017#conformant#urn:zugferd.de:2p0:extended")
            {
                return FacturXProfile.Extended;
            }

            if (guideline.StartsWith(XRechnungGuidelinePrefix, StringComparison.Ordinal))
                return FacturXProfile.XRechnung;

            return null;
        }

        static PdfAttachmentRelationship DefaultRelationship(FacturXProfile profile) =>
            profile is FacturXProfile.Minimum or FacturXProfile.BasicWl
                ? PdfAttachmentRelationship.Data
                : PdfAttachmentRelationship.Alternative;

        static bool IsPermitted(FacturXProfile profile, PdfAttachmentRelationship relationship) =>
            Permitted(profile).Contains(relationship);

        // Chapter 6.2.2's table: the union of what France and Germany allow. Germany permits only
        // Alternative for every profile that is an invoice there; that is the default, so it needs no check.
        static PdfAttachmentRelationship[] Permitted(FacturXProfile profile) => profile switch
        {
            FacturXProfile.Minimum or FacturXProfile.BasicWl => [PdfAttachmentRelationship.Data],
            FacturXProfile.XRechnung => [PdfAttachmentRelationship.Alternative],
            _ => [PdfAttachmentRelationship.Alternative, PdfAttachmentRelationship.Source, PdfAttachmentRelationship.Data],
        };
    }
}
