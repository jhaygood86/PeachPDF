using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Reads what a viewer navigates by from a <see cref="PdfSourceDocument"/>: the links of a page, the outline, the named destinations,
/// the document information and the attached files. Every position is in the same terms the page is painted in - points from the
/// top-left corner of the page as it is shown, after its rotation.
/// </summary>
internal sealed class PdfSourceNavigation(PdfSourceDocument doc)
{
    const int MaxDepth = 64;

    Dictionary<PdfDictionary, int>? _pageIndexes;
    Dictionary<string, PdfSourceDestination>? _named;

    PdfDictionary? Catalog => doc.Resolve(doc.Trailer.GetRaw("Root")) as PdfDictionary;

    /// <summary>The zero-based index of a page object, or -1.</summary>
    int IndexOfPage(PdfDictionary? page)
    {
        if (page is null) return -1;
        if (_pageIndexes is null)
        {
            _pageIndexes = new Dictionary<PdfDictionary, int>(ReferenceEqualityComparer.Instance as IEqualityComparer<PdfDictionary>);
            for (int i = 0; i < doc.Pages.Count; i++) _pageIndexes[doc.Pages[i]] = i;
        }
        return _pageIndexes.TryGetValue(page, out var index) ? index : -1;
    }

    // ------------------------------------------------------------------ text

    /// <summary>A PDF text string: UTF-16 with a byte order mark, UTF-8 with one, or PDFDocEncoding (taken as Latin-1).</summary>
    public static string TextOf(string raw)
    {
        if (raw.Length >= 2 && raw[0] == 0xFE && raw[1] == 0xFF) return Encoding.BigEndianUnicode.GetString(Encoding.Latin1.GetBytes(raw), 2, raw.Length - 2);
        if (raw.Length >= 2 && raw[0] == 0xFF && raw[1] == 0xFE) return Encoding.Unicode.GetString(Encoding.Latin1.GetBytes(raw), 2, raw.Length - 2);
        if (raw.Length >= 3 && raw[0] == 0xEF && raw[1] == 0xBB && raw[2] == 0xBF) return Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(raw), 3, raw.Length - 3);
        return raw;
    }

    string? TextValue(PdfItem? item) => doc.Resolve(item) is PdfString s ? TextOf(s.Value) : null;

    // ------------------------------------------------------------------ destinations

    /// <summary>Reads a destination (an explicit array, a name looked up in the document's destinations, or a dictionary holding <c>/D</c>).</summary>
    PdfSourceDestination? Destination(PdfItem? item, int depth = 0)
    {
        if (depth > 4) return null;
        switch (doc.Resolve(item))
        {
            case PdfArray array:
                return Explicit(array);
            case PdfDictionary dict:
                return Destination(dict.GetRaw("D"), depth + 1);
            case PdfString s:
                return Named().TryGetValue(TextOf(s.Value), out var byString) ? byString : null;
            case PdfName n:
                return Named().TryGetValue(n.Value.TrimStart('/'), out var byName) ? byName : null;
            default:
                return null;
        }
    }

    PdfSourceDestination? Explicit(PdfArray array)
    {
        if (array.Elements.Count < 2) return null;
        var page = doc.Resolve(array.Elements[0]) as PdfDictionary;
        var index = IndexOfPage(page);
        if (index < 0) return null;

        // The point the view is placed at, where the destination gives one: /XYZ left top, /FitH top, /FitBH top, /FitR l b r t.
        double? x = null, y = null;
        switch (doc.NameOf(array.Elements[1]))
        {
            case "/XYZ" when array.Elements.Count >= 4:
                x = Number(array.Elements[2]); y = Number(array.Elements[3]);
                break;
            case "/FitH" or "/FitBH" when array.Elements.Count >= 3:
                y = Number(array.Elements[2]);
                break;
            case "/FitV" or "/FitBV" when array.Elements.Count >= 3:
                x = Number(array.Elements[2]);
                break;
            case "/FitR" when array.Elements.Count >= 6:
                x = Number(array.Elements[2]); y = Number(array.Elements[5]);
                break;
        }

        double top = 0;
        if (y is { } py)
        {
            var toCanvas = PdfPagePainter.PageToCanvas(doc, page!);
            top = Math.Max(0, Vector2.Transform(new Vector2((float)(x ?? 0), (float)py), toCanvas).Y);
        }
        return new PdfSourceDestination(index, top);
    }

    double? Number(PdfItem item) => doc.Resolve(item) is PdfNumber ? doc.NumberOf(item) : null;

    /// <summary>Every named destination, from the catalog's <c>/Names /Dests</c> tree and the older <c>/Dests</c> dictionary.</summary>
    public IReadOnlyDictionary<string, PdfSourceDestination> NamedDestinations() => Named();

    Dictionary<string, PdfSourceDestination> Named()
    {
        if (_named is not null) return _named;
        _named = new Dictionary<string, PdfSourceDestination>(StringComparer.Ordinal);
        if (Catalog is not { } catalog) return _named;

        if (doc.DictOf(catalog, "Names") is { } names && doc.DictOf(names, "Dests") is { } tree)
            WalkNameTree(tree, (key, value) => AddNamed(key, value), 0);

        if (doc.DictOf(catalog, "Dests") is { } legacy)
        {
            foreach (var pair in legacy.Elements)
                AddNamed(pair.Key.TrimStart('/'), pair.Value);
        }
        return _named;

        void AddNamed(string key, PdfItem value)
        {
            if (_named.ContainsKey(key)) return;
            var resolved = doc.Resolve(value);
            // An explicit destination, or a dictionary whose /D holds it; not a name, which would be an alias.
            var destination = resolved is PdfArray or PdfDictionary ? Destination(resolved, 3) : null;
            if (destination is not null) _named[key] = destination;
        }
    }

    /// <summary>Visits the (key, value) pairs of a name tree, depth-first and in order.</summary>
    void WalkNameTree(PdfDictionary node, Action<string, PdfItem> visit, int depth)
    {
        if (depth > MaxDepth) return;
        if (doc.ArrayOf(node, "Names") is { } pairs)
        {
            for (int i = 0; i + 1 < pairs.Elements.Count; i += 2)
            {
                if (doc.Resolve(pairs.Elements[i]) is PdfString key) visit(TextOf(key.Value), pairs.Elements[i + 1]);
            }
        }
        if (doc.ArrayOf(node, "Kids") is { } kids)
        {
            foreach (var kid in kids.Elements)
            {
                if (doc.Resolve(kid) is PdfDictionary child) WalkNameTree(child, visit, depth + 1);
            }
        }
    }

    // ------------------------------------------------------------------ links

    /// <summary>The link annotations of a page that open a web address or lead to another place in the file.</summary>
    public IReadOnlyList<PdfSourceLink> Links(PdfDictionary page)
    {
        var links = new List<PdfSourceLink>();
        if (doc.ArrayOf(page, "Annots") is not { } annots) return links;
        var toCanvas = PdfPagePainter.PageToCanvas(doc, page);

        foreach (var item in annots.Elements)
        {
            if (doc.Resolve(item) is not PdfDictionary annot || doc.NameOf(annot.GetRaw("Subtype")) != "/Link") continue;
            if (doc.ArrayOf(annot, "Rect") is not { Elements.Count: >= 4 } rect) continue;

            double x0 = doc.NumberOf(rect.Elements[0]), y0 = doc.NumberOf(rect.Elements[1]), x1 = doc.NumberOf(rect.Elements[2]), y1 = doc.NumberOf(rect.Elements[3]);
            var corners = new[] { new Vector2((float)x0, (float)y0), new Vector2((float)x1, (float)y0), new Vector2((float)x1, (float)y1), new Vector2((float)x0, (float)y1) };
            for (int i = 0; i < 4; i++) corners[i] = Vector2.Transform(corners[i], toCanvas);
            float left = Math.Min(Math.Min(corners[0].X, corners[1].X), Math.Min(corners[2].X, corners[3].X));
            float right = Math.Max(Math.Max(corners[0].X, corners[1].X), Math.Max(corners[2].X, corners[3].X));
            float top = Math.Min(Math.Min(corners[0].Y, corners[1].Y), Math.Min(corners[2].Y, corners[3].Y));
            float bottom = Math.Max(Math.Max(corners[0].Y, corners[1].Y), Math.Max(corners[2].Y, corners[3].Y));
            var bounds = new Rect(left, top, right - left, bottom - top);

            string? uri = null;
            PdfSourceDestination? destination = null;
            if (doc.DictOf(annot, "A") is { } action)
            {
                switch (doc.NameOf(action.GetRaw("S")))
                {
                    case "/URI":
                        uri = doc.Resolve(action.GetRaw("URI")) is PdfString u ? u.Value : null;
                        break;
                    case "/GoTo":
                        destination = Destination(action.GetRaw("D"));
                        break;
                }
            }
            else if (annot.ContainsKeyRaw("Dest"))
            {
                destination = Destination(annot.GetRaw("Dest"));
            }

            if (uri is not null || destination is not null) links.Add(new PdfSourceLink(bounds, uri, destination));
        }
        return links;
    }

    // ------------------------------------------------------------------ outline

    /// <summary>The document outline flattened in reading order, each entry with its nesting level; a destination in the file is a page of this file.</summary>
    public IReadOnlyList<PdfBookmark> Outline()
    {
        var entries = new List<PdfBookmark>();
        if (Catalog is { } catalog && doc.DictOf(catalog, "Outlines") is { } root)
            WalkOutline(doc.Resolve(root.GetRaw("First")) as PdfDictionary, 1, entries, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance as IEqualityComparer<PdfDictionary>));
        return entries;
    }

    void WalkOutline(PdfDictionary? item, int level, List<PdfBookmark> entries, HashSet<PdfDictionary> seen)
    {
        for (; item is not null && level <= MaxDepth && seen.Add(item); item = doc.Resolve(item.GetRaw("Next")) as PdfDictionary)
        {
            var title = TextValue(item.GetRaw("Title")) ?? "";
            var entry = new PdfBookmark(level, title, doc.IntOf(item.GetRaw("Count")) > 0);

            if (doc.DictOf(item, "A") is { } action)
            {
                switch (doc.NameOf(action.GetRaw("S")))
                {
                    case "/URI" when doc.Resolve(action.GetRaw("URI")) is PdfString u:
                        entry.Uri = u.Value;
                        break;
                    case "/GoTo" when Destination(action.GetRaw("D")) is { } target:
                        (entry.PageIndex, entry.TopPoints) = (target.PageIndex, target.TopPoints);
                        break;
                }
            }
            else if (item.ContainsKeyRaw("Dest") && Destination(item.GetRaw("Dest")) is { } dest)
            {
                (entry.PageIndex, entry.TopPoints) = (dest.PageIndex, dest.TopPoints);
            }
            entries.Add(entry);

            if (doc.Resolve(item.GetRaw("First")) is PdfDictionary first) WalkOutline(first, level + 1, entries, seen);
        }
    }

    // ------------------------------------------------------------------ information and attachments

    /// <summary>The document information dictionary.</summary>
    public PdfSourceMetadata Metadata()
    {
        var info = doc.Resolve(doc.Trailer.GetRaw("Info")) as PdfDictionary;
        string? Field(string key) => info is null ? null : TextValue(info.GetRaw(key));
        return new PdfSourceMetadata(Field("Title"), Field("Author"), Field("Subject"), Field("Keywords"), Field("Creator"), Field("Producer"),
            Catalog is { } catalog ? TextValue(catalog.GetRaw("Lang")) : null);
    }

    /// <summary>The XMP packet of the catalog's <c>/Metadata</c>, or an empty string.</summary>
    string Xmp()
    {
        if (Catalog is not { } catalog || doc.Resolve(catalog.GetRaw("Metadata")) is not PdfDictionary { Stream: not null } stream) return "";
        try { return Encoding.UTF8.GetString(doc.DecodeStream(stream).Data); }
        catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException) { return ""; }
    }

    static string? XmpValue(string xmp, string ns, string name)
    {
        var match = Regex.Match(xmp, ns + ":" + name + "\\s*(?:=\\s*\"([^\"]*)\"|>\\s*([^<\\s]*)\\s*<)", RegexOptions.CultureInvariant);
        return match.Success ? (match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value) : null;
    }

    /// <summary>The PDF/A and PDF/X levels the file claims, from its XMP packet (and, for PDF/X, the information dictionary).</summary>
    public PdfSourceConformance Conformance()
    {
        var xmp = Xmp();
        var pdfA = PdfAConformance.None;
        if (int.TryParse(XmpValue(xmp, "pdfaid", "part"), out var part))
        {
            var level = XmpValue(xmp, "pdfaid", "conformance")?.ToUpperInvariant();
            pdfA = (part, level) switch
            {
                (1, "A") => PdfAConformance.PdfA1A,
                (1, _) => PdfAConformance.PdfA1B,
                (2, "A") => PdfAConformance.PdfA2A,
                (2, "U") => PdfAConformance.PdfA2U,
                (2, _) => PdfAConformance.PdfA2B,
                (3, "A") => PdfAConformance.PdfA3A,
                (3, "U") => PdfAConformance.PdfA3U,
                (3, _) => PdfAConformance.PdfA3B,
                (4, "E") => PdfAConformance.PdfA4E,
                (4, "F") => PdfAConformance.PdfA4F,
                (4, _) => PdfAConformance.PdfA4,
                _ => PdfAConformance.None,
            };
        }

        var version = XmpValue(xmp, "pdfxid", "GTS_PDFXVersion")
            ?? (doc.Resolve(doc.Trailer.GetRaw("Info")) is PdfDictionary info ? TextValue(info.GetRaw("GTS_PDFXVersion")) : null)
            ?? "";
        var pdfX = version switch
        {
            _ when version.StartsWith("PDF/X-1a", StringComparison.OrdinalIgnoreCase) => PdfXConformance.X1a,
            _ when version.StartsWith("PDF/X-3", StringComparison.OrdinalIgnoreCase) => PdfXConformance.X3,
            _ when version.StartsWith("PDF/X-4", StringComparison.OrdinalIgnoreCase) => PdfXConformance.X4,
            _ when version.StartsWith("PDF/X-6", StringComparison.OrdinalIgnoreCase) => PdfXConformance.X6,
            _ => PdfXConformance.None,
        };
        return new PdfSourceConformance(pdfA, pdfX);
    }

    /// <summary>The first output intent that carries an ICC profile PeachImage can read, or null.</summary>
    public PdfSourceOutputIntent? OutputIntent()
    {
        if (Catalog is not { } catalog || doc.ArrayOf(catalog, "OutputIntents") is not { } intents) return null;
        foreach (var item in intents.Elements)
        {
            if (doc.Resolve(item) is not PdfDictionary intent || doc.DictOf(intent, "DestOutputProfile") is not { Stream: not null } profile) continue;
            byte[] bytes;
            try { bytes = doc.DecodeStream(profile).Data; }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException) { continue; }
            if (!PeachImage.IccColorProfile.TryCreate(bytes, out var parsed) || parsed is null) continue;

            var identifier = TextValue(intent.GetRaw("OutputConditionIdentifier")) ?? TextValue(intent.GetRaw("OutputCondition"));
            return new PdfSourceOutputIntent(identifier, TextValue(intent.GetRaw("Info")), bytes);
        }
        return null;
    }

    /// <summary>The files attached to the document through the catalog's <c>/Names /EmbeddedFiles</c> tree.</summary>
    public IReadOnlyList<PdfEmbeddedFile> Attachments()
    {
        var files = new List<PdfEmbeddedFile>();
        if (Catalog is not { } catalog || doc.DictOf(catalog, "Names") is not { } names || doc.DictOf(names, "EmbeddedFiles") is not { } tree) return files;

        WalkNameTree(tree, (key, value) =>
        {
            if (doc.Resolve(value) is not PdfDictionary spec) return;
            var stream = doc.DictOf(spec, "EF") is { } ef ? (doc.Resolve(ef.GetRaw("UF")) ?? doc.Resolve(ef.GetRaw("F"))) as PdfDictionary : null;
            if (stream is null || stream.Stream is null) return;

            byte[] data;
            try { data = doc.DecodeStream(stream).Data; }
            catch (Exception ex) when (ex is InvalidOperationException or InvalidDataException or NotSupportedException) { return; }

            var file = new PdfEmbeddedFile
            {
                FileName = TextValue(spec.GetRaw("UF")) ?? TextValue(spec.GetRaw("F")) ?? key,
                Description = TextValue(spec.GetRaw("Desc")),
                Data = data,
            };
            if (doc.NameOf(stream.GetRaw("Subtype")) is { } subtype)
                file.MimeType = subtype.TrimStart('/').Replace("#2F", "/", StringComparison.OrdinalIgnoreCase);
            files.Add(file);
        }, 0);
        return files;
    }
}
