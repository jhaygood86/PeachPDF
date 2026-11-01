#nullable enable

using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachDrawing.PDF
{
    public sealed partial class PdfComposer
    {
        /// <summary>
        /// Applies <paramref name="options"/> to the document: fixes its format (version, conformance, colour management,
        /// encryption, embedded files, compression), writes the document information, and writes whatever the claimed conformance
        /// level requires (the output intent, the XMP packet).
        /// </summary>
        /// <remarks>
        /// <para>
        /// The constructor calls this with the options it is given. Call it again to write more or newer document information: the
        /// information is replaced by the non-empty values of the new options, and the XMP packet is rewritten. The format settings
        /// are a whole-document property and are fixed by the first call; a later call must ask for the same ones, and one that does
        /// not is rejected rather than leaving a file whose header or conformance claim disagrees with how some pages were written.
        /// </para>
        /// <para>
        /// Everything that can be checked from the options alone is checked here, before anything is drawn, so a document that is
        /// about to be rejected wastes no work.
        /// </para>
        /// </remarks>
        /// <param name="options">the options to apply; read now, not kept</param>
        /// <exception cref="InvalidOperationException">
        /// the options contradict each other (a PDF/A level with encryption, a PDF 2.0 file with a PDF/A-1 level, an XMP packet with
        /// no creation date, ...) or differ from the format an earlier call fixed
        /// </exception>
        public void Configure(PdfComposerOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);
            EstablishFormat(options);
            ApplyInformation(options);
            _configured = true;
        }

        private void EnsureConfigured()
        {
            if (!_configured)
                Configure(new PdfComposerOptions());
        }

        /// <summary>
        /// Fixes the whole-document format settings, checking each against what an earlier call fixed.
        /// </summary>
        private void EstablishFormat(PdfComposerOptions options)
        {
            var settings = _document.Options;
            settings.CompressContentStreams = options.CompressContentStreams;
            settings.Linearize = options.Linearize;
            settings.BrotliCompression = options.BrotliCompression;
            settings.JxlPassthrough = options.JxlPassthrough;
            // Undefined (not the engine default of Rgb) lets each colour write in whichever space it actually carries - RGB as
            // /DeviceRGB, CMYK as /DeviceCMYK - rather than every CMYK colour being collapsed to a lossy RGB round trip. For an
            // all-RGB document this is byte-identical to Rgb mode.
            settings.ColorMode = PdfColorMode.Undefined;
            settings.DownscaleImages = options.DownscaleImages;
            settings.FlattenTransparency = options.FlattenTransparency;
            settings.PortableColorFilters = options.PortableColorFilters;
            settings.DownscaleQuality = options.DownscaleQuality;
            settings.MaximumDownscaleMultiplier = options.MaximumDownscaleMultiplier;
            settings.ImageCompression = options.ImageCompression;

            // PDF/A conformance is a whole-document property, but options can be applied repeatedly (a caller can append more pages
            // to a document): a second application requesting a different level than the first would leave the document's
            // already-written /OutputIntents and XMP conformance claim disagreeing with how some of its pages were painted (earlier
            // pages under a different transparency guard than the level the file now claims). Reject that outright rather than
            // produce a self-contradictory document; the same level requested again is fine.
            if (settings.PdfAConformanceEstablished && settings.PdfAConformance != options.Conformance)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.Conformance must be the same every time options are applied to a document - this document was " +
                    $"already established as '{settings.PdfAConformance}' by an earlier call, and this call specifies '{options.Conformance}'. " +
                    "A single PDF document can only claim one PDF/A conformance level (or none) as a whole.");
            }

            settings.PdfAConformance = options.Conformance;
            settings.PdfAConformanceEstablished = true;

            // ISO 19005-2/3 (PDF/A-2/3) are defined in terms of PDF 1.7. ISO 19005-1 (PDF/A-1) is defined in terms of PDF 1.4, the
            // header the writer emits by default, so PDF/A-1 needs no version change at all.
            if (options.Conformance is PdfAConformance.PdfA2B or PdfAConformance.PdfA2U or PdfAConformance.PdfA2A
                or PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A)
            {
                _document.Version = 17;
            }

            // PDF/A-4 and PDF/X-6 are defined against PDF 2.0; every other PDF/A or PDF/X level is defined against PDF 1.4, 1.6 or 1.7.
            // A caller who left the version alone gets the version the requested standard needs; one who set it to something the
            // standard cannot use has a contradiction to resolve, not something to silently pick a winner for.
            var requiresPdf20 = options.Conformance is PdfAConformance.PdfA4 or PdfAConformance.PdfA4E or PdfAConformance.PdfA4F
                || options.XConformance == PdfXConformance.X6;
            var requestsEarlierLevel = !requiresPdf20
                && (options.Conformance != PdfAConformance.None || options.XConformance != PdfXConformance.None);
            var requestedVersion = options.Version
                ?? (requiresPdf20 ? PdfVersion.Pdf20 : requestsEarlierLevel ? PdfVersion.Pdf17 : PdfVersion.Pdf20);

            if (requiresPdf20 && requestedVersion != PdfVersion.Pdf20)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.Version is set to {requestedVersion}, but Conformance ({options.Conformance}) " +
                    $"or XConformance ({options.XConformance}) is a level defined against PDF 2.0 (PDF/A-4, PDF/X-6). " +
                    "Set Version to Pdf20, or leave it unset.");
            }

            if (!requiresPdf20 && requestedVersion == PdfVersion.Pdf20 && options.Conformance != PdfAConformance.None && options.Version is not null)
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Version is set to Pdf20, but Conformance is also set to a " +
                    "level defined against PDF 1.4 or 1.7. Only PDF/A-4 (PdfA4, PdfA4E, PdfA4F) is a PDF 2.0 level; " +
                    "request PdfVersion.Pdf17 (or leave Version unset) for the earlier ones.");
            }

            if (!requiresPdf20 && requestedVersion == PdfVersion.Pdf20 && options.XConformance != PdfXConformance.None && options.Version is not null)
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Version is set to Pdf20, but XConformance is also set to a " +
                    "level defined against PDF 1.4 (X1a/X3) or 1.6 (X4). Only PdfXConformance.X6 is a PDF 2.0 " +
                    "level; request PdfVersion.Pdf17 (or leave Version unset) for the earlier ones.");
            }

            // A PDF file has exactly one header version: a second application requesting a different version than the first would
            // leave the already-written header disagreeing with how some pages and structure elements were painted.
            if (settings.PdfVersionEstablished && settings.PdfVersion != requestedVersion)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.Version must be the same every time options are applied to a document - this document was " +
                    $"already established as '{settings.PdfVersion}' by an earlier call, and this call specifies '{requestedVersion}'. " +
                    "A single PDF file can only have one header version.");
            }

            settings.PdfVersion = requestedVersion;
            settings.PdfVersionEstablished = true;

            if (requestedVersion == PdfVersion.Pdf20)
                _document.Version = 20;

            // PDF/A (archival) and PDF/X (print production) are different documents in practice - no single file conformance-claims
            // both at once, so requesting both is a contradiction for the caller to resolve.
            if (options.Conformance != PdfAConformance.None && options.XConformance != PdfXConformance.None)
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Conformance and XConformance are both set to a level other than None. A single PDF " +
                    "document can only claim one conformance family - archival (PDF/A) or print production (PDF/X), not both.");
            }

            if (settings.PdfXConformanceEstablished && settings.PdfXConformance != options.XConformance)
            {
                throw new InvalidOperationException(
                    $"PdfComposerOptions.XConformance must be the same every time options are applied to a document - this document was " +
                    $"already established as '{settings.PdfXConformance}' by an earlier call, and this call specifies '{options.XConformance}'. " +
                    "A single PDF document can only claim one PDF/X conformance level (or none) as a whole.");
            }

            settings.PdfXConformance = options.XConformance;
            settings.PdfXConformanceEstablished = true;
            settings.ColorOptions = options.ColorOptions;

            // Embedded files are a whole-document property too, validated here so a document about to be rejected wastes no work.
            var plan = EmbeddedFilePlan.Create(options);
            if (settings.EmbeddingPlan is EmbeddedFilePlan establishedPlan && !establishedPlan.SameAs(plan))
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.EmbeddedFiles must be the same every time options are applied to a document - this call asks for " +
                    "a different set of embedded files than an earlier call did. A document's embedded files are established once, by its first call.");
            }

            settings.EmbeddingPlan = plan;

            // /UF (PDF 1.7), /Desc (1.6) and /AFRelationship are newer than the historical 1.4 header, and a PDF/A-3 level already asks
            // for 1.7 above - so a plain embedded file gets the same, not a 1.4 file that uses 1.7 features.
            if (plan.Files.Count > 0 && _document.Version < 17)
                _document.Version = 17;

            // ISO 15930-4/6 (PDF/X-1a:2003/PDF/X-3:2003) target PDF 1.4, already the writer's historical default, so no change is needed
            // for those two levels; ISO 15930-7 (PDF/X-4) targets PDF 1.6, a real bump from that default.
            if (options.XConformance == PdfXConformance.X4)
                _document.Version = 16;

            // Encryption is a whole-document property like the conformance levels, and both PDF/A (ISO 19005, 6.1.3) and PDF/X
            // (ISO 15930) forbid an /Encrypt entry - a document cannot claim one and carry the other. The snapshot keeps a caller who
            // edits their options object afterwards from changing a document in progress.
            var encryption = options.Encryption?.Clone();
            if (encryption is not null && (options.Conformance != PdfAConformance.None || options.XConformance != PdfXConformance.None))
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Encryption cannot be combined with Conformance or XConformance: " +
                    "PDF/A and PDF/X documents must not be encrypted.");
            }

            if (settings.EncryptionEstablished
                && !(settings.Encryption is null ? encryption is null : settings.Encryption.SameAs(encryption)))
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Encryption must be the same every time options are applied to a document - " +
                    "a PDF file is encrypted as a whole, with one set of passwords and permissions.");
            }

            settings.Encryption = encryption;
            settings.EncryptionEstablished = true;

            // AES-256 (revision 6) is a PDF 2.0 feature that PDF 1.7 readers also accept (Adobe extension level 8); a file that would
            // otherwise carry an older header is raised to 1.7.
            if (encryption is not null && _document.Version < 17)
                _document.Version = 17;
        }

        /// <summary>
        /// Writes the document information and what the claimed conformance level requires of it.
        /// </summary>
        private void ApplyInformation(PdfComposerOptions options)
        {
            var info = _document.Info;
            info.Producer = options.Producer ?? PdfComposerProductInfo.Generator;

            // A non-empty value replaces what an earlier call wrote; an empty one leaves it.
            if (!string.IsNullOrEmpty(options.Creator)) info.Creator = options.Creator;
            if (!string.IsNullOrEmpty(options.Title)) info.Title = options.Title;
            if (!string.IsNullOrEmpty(options.Author)) info.Author = options.Author;
            if (!string.IsNullOrEmpty(options.Subject)) info.Subject = options.Subject;
            if (!string.IsNullOrEmpty(options.Keywords)) info.Keywords = options.Keywords;

            // .LocalDateTime: the date is written with the machine's UTC offset, which would mislabel the instant of a value carrying
            // another offset; converting to local time first makes the offset written the right one for that same instant.
            if (options.CreationDate is { } created)
                info.CreationDate = created.LocalDateTime;

            // The accessible PDF/A levels (a) build on tagged output and additionally require a real document language.
            var isAccessibleConformance = options.Conformance is PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A;
            if (!string.IsNullOrEmpty(options.Language))
            {
                _document.Language = options.Language;
            }
            else if (isAccessibleConformance && string.IsNullOrEmpty(_document.Language))
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.Conformance is set to an accessible ('A') PDF/A level, which requires a document language, " +
                    "but PdfComposerOptions.Language is not set.");
            }

            ValidateColorOptions(options.ColorOptions);

            var pdfAConformanceRequested = options.Conformance != PdfAConformance.None;
            var pdfXConformanceRequested = options.XConformance != PdfXConformance.None;

            // Every PDF/X level requires a real output intent, and the writer bundles no default press profile (unlike PDF/A's bundled
            // sRGB one - there is no single correct default for a print output intent): fail loudly rather than silently omit
            // /OutputIntents.
            if (pdfXConformanceRequested && options.ColorOptions?.OutputIntentProfile is not { Length: > 0 })
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.XConformance is set, but PdfComposerOptions.ColorOptions.OutputIntentProfile " +
                    "is not set. Every PDF/X level requires a real ICC output-intent profile - the writer " +
                    "bundles no default press profile, so supply one via ColorOptions.OutputIntentProfile " +
                    "(and ColorOptions.OutputIntentIdentifier).");
            }

            if (pdfXConformanceRequested && string.IsNullOrEmpty(options.ColorOptions?.OutputIntentIdentifier))
            {
                throw new InvalidOperationException(
                    "PdfComposerOptions.XConformance is set, but PdfComposerOptions.ColorOptions.OutputIntentIdentifier " +
                    "is not set. Set it to a human-readable name for the output-intent profile's condition " +
                    "(e.g. \"Coated FOGRA39\").");
            }

            PeachImage.IccColorProfile? outputIntentProfile = null;
            if (pdfXConformanceRequested)
            {
                PeachImage.IccColorProfile.TryCreate(options.ColorOptions!.OutputIntentProfile!, out outputIntentProfile);

                if (options.XConformance == PdfXConformance.X1a && outputIntentProfile?.DataColorSpace != PeachImage.IccColorSpace.Cmyk)
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.XConformance is PdfXConformance.X1a, which requires a CMYK " +
                        "output-intent profile, but ColorOptions.OutputIntentProfile is not a valid CMYK " +
                        "ICC profile. PdfXConformance.X3/X4 accept a CMYK, RGB, or Gray output intent instead.");
                }
            }

            // An XMP metadata stream needs a real xmp:CreateDate - fail loudly rather than write a placeholder date. PDF/X needs it
            // too: PDF/X-4's identification is primarily an XMP property (ISO 15930-7), not just an Info-dictionary one the way
            // PDF/X-1a/X-3's is.
            var writeXmpMetadata = options.WriteXmpMetadata || pdfAConformanceRequested || pdfXConformanceRequested
                || options.CustomXmpProperties.Count > 0;
            if (writeXmpMetadata && options.CreationDate is not { })
            {
                throw new InvalidOperationException(
                    "An XMP metadata stream is being written (WriteXmpMetadata, Conformance, XConformance, or CustomXmpProperties is set), " +
                    "but no creation date is available. Set PdfComposerOptions.CreationDate.");
            }

            var plan = (EmbeddedFilePlan)_document.Options.EmbeddingPlan!;
            if (writeXmpMetadata)
            {
                // Rewritten on every call - unlike the output intent below, the packet's content (title, dates, custom properties) can
                // legitimately differ call to call, so refreshing it is more useful than freezing it at whatever the first call saw.
                var facturX = options.FacturX is { } marker ? (marker.FileName, marker.ConformanceLevel) : ((string, string)?)null;
                var metadataStream = new PdfMetadataStream(
                    _document, _document.Info, options.CreationDate!.Value, options.Conformance, options.XConformance,
                    options.CustomXmpProperties, facturX);
                _document.Catalog.SetMetadata(metadataStream);
            }

            // Guarded on "not already present" (rather than unconditionally, like SetMetadata above): the sRGB output intent never
            // varies call to call, so re-adding it would only leave the first call's ICC-profile stream orphaned.
            if (pdfAConformanceRequested && !_document.Catalog.Elements.ContainsKey("/OutputIntents"))
            {
                var outputIntent = new PdfOutputIntent(_document, PdfAResources.SRgbIccProfile);
                _document.Catalog.SetOutputIntent(outputIntent);
            }

            // A document with no PDF/A or PDF/X level can still say what its colours are meant for: an output intent with the profile and
            // condition it was given. It uses the same subtype a press-ready file does; the level claims (the XMP packet's identification
            // and the information dictionary's version) are what say a file conforms, and none is written here.
            if (!pdfAConformanceRequested && !pdfXConformanceRequested && options.ColorOptions?.OutputIntentProfile is { Length: > 0 } plainProfile
                && !_document.Catalog.Elements.ContainsKey("/OutputIntents"))
            {
                if (!PeachImage.IccColorProfile.TryCreate(plainProfile, out var plainParsed))
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.OutputIntentProfile could not be parsed as a valid ICC profile.");
                }

                var condition = options.ColorOptions.OutputIntentIdentifier is { Length: > 0 } named ? named : "Custom";
                _document.Catalog.SetOutputIntent(new PdfOutputIntent(_document, plainProfile, "/GTS_PDFX", condition, condition, explicitN: plainParsed?.ChannelCount));
            }

            // Same "not already present" dedup as PDF/A - ColorOptions.OutputIntentProfile was validated (non-null, and CMYK under X1a)
            // above.
            if (pdfXConformanceRequested && !_document.Catalog.Elements.ContainsKey("/OutputIntents"))
            {
                var outputIntent = new PdfOutputIntent(_document, options.ColorOptions!.OutputIntentProfile!,
                    "/GTS_PDFX", options.ColorOptions.OutputIntentIdentifier!, options.ColorOptions.OutputIntentIdentifier!,
                    explicitN: outputIntentProfile?.ChannelCount);
                _document.Catalog.SetOutputIntent(outputIntent);

                // The legacy PDF/X identification mechanism (predates XMP - still what many prepress RIPs and preflight tools check for
                // X1a/X3): /GTS_PDFXVersion (all levels) and, only for the part-1 "a" variant, /GTS_PDFXConformance.
                // PdfMetadataStream.PdfXIdentifiers is the shared source of the exact identifier strings, so this and the XMP pdfx:
                // block always agree. PDF/X-6 identifies itself in XMP only (pdfxid), like PDF 2.0's own deprecation of the Info
                // dictionary.
                if (options.XConformance != PdfXConformance.X6)
                {
                    var (gtsVersion, gtsConformance) = PdfMetadataStream.PdfXIdentifiers(options.XConformance);
                    _document.Info.Elements.SetString("/GTS_PDFXVersion", gtsVersion, PdfStringEncoding.LatinOneByteEncoding);
                    if (gtsConformance is not null)
                        _document.Info.Elements.SetString("/GTS_PDFXConformance", gtsConformance, PdfStringEncoding.LatinOneByteEncoding);
                }
            }

            // Embedded files go in before any page is painted, so they precede anything a page attaches in /AF. The plan was
            // established (and checked against any earlier call) above; writing it once per document, not once per call, is what the
            // Applied flag is for.
            if (plan.Files.Count > 0 && !_document.Options.EmbeddingApplied)
            {
                plan.Apply(_document, options.CreationDate);
                _document.Options.EmbeddingApplied = true;
            }
        }

        /// <summary>
        /// Each non-default colour conversion mode needs its own destination ICC profile set and parseable: validated once, up front
        /// (fail loudly, don't write a placeholder) rather than discovered deep in a colour guard partway through painting.
        /// </summary>
        private static void ValidateColorOptions(ColorOptions? colorOptions)
        {
            if (colorOptions is { ConversionMode: ColorConversionMode.ConvertToOutputIntent } convertToOutputIntent)
            {
                if (convertToOutputIntent.OutputIntentProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.ConversionMode is ColorConversionMode.ConvertToOutputIntent, " +
                        "but ColorOptions.OutputIntentProfile is not set. Set it to the ICC profile every color " +
                        "should be converted into.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(convertToOutputIntent.OutputIntentProfile, out _))
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.OutputIntentProfile could not be parsed as a valid ICC profile.");
                }
            }
            else if (colorOptions is { ConversionMode: ColorConversionMode.ConvertToProfile } convertToProfile)
            {
                if (convertToProfile.ConvertToProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.ConversionMode is ColorConversionMode.ConvertToProfile, but " +
                        "ColorOptions.ConvertToProfile is not set.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(convertToProfile.ConvertToProfile, out _))
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.ConvertToProfile could not be parsed as a valid ICC profile.");
                }
            }
            else if (colorOptions is { ConversionMode: ColorConversionMode.GrayscaleViaK } grayscaleViaK)
            {
                if (grayscaleViaK.FallbackCmykProfile is not { Length: > 0 })
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.ConversionMode is ColorConversionMode.GrayscaleViaK, but " +
                        "ColorOptions.FallbackCmykProfile is not set - it's the CMYK profile every color is " +
                        "converted through before taking only its K channel.");
                }

                if (!PeachImage.IccColorProfile.TryCreate(grayscaleViaK.FallbackCmykProfile, out var grayscaleProfile) ||
                    grayscaleProfile is null || grayscaleProfile.DataColorSpace != PeachImage.IccColorSpace.Cmyk)
                {
                    throw new InvalidOperationException(
                        "PdfComposerOptions.ColorOptions.FallbackCmykProfile must be a valid CMYK ICC profile when " +
                        "ConversionMode is ColorConversionMode.GrayscaleViaK.");
                }
            }
        }
    }
}
