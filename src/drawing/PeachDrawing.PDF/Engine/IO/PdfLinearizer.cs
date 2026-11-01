using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Engine.Security;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace PeachDrawing.PDF.Engine.IO;

/// <summary>
/// Writes a document in the linearized layout of ISO 32000-1 Annex F ("fast web view"): the objects a reader needs for the first page come
/// first, then the other pages in order, then what the pages share, then the rest, with the two hint tables a viewer uses to fetch a
/// page by byte range. The file is an ordinary classic cross-reference file otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Layout, in file order (the part numbers are the Annex's): the header; the linearization parameter dictionary (2); the first-page
/// cross-reference section and trailer (3); the catalog (4); the primary hint stream (5); the first page's objects (6); each other page's own
/// objects (7); the objects shared between pages or with the document (8); everything else (9); the main cross-reference section. Objects are
/// renumbered while writing so the first-page section (parts 2-6) has the highest numbers, which is what gives it a cross-reference table of its
/// own; the numbers are put back afterwards, so a document can be saved again.
/// </para>
/// <para>
/// Every object is written to memory once, after it has its new number (an encrypted string depends on it), so every length is known before any
/// offset is. The hint stream's size depends only on the lengths it records, never on the offsets, which is how the layout is settled in one pass.
/// Hint offsets follow the Annex's convention that an offset past the hint stream is given as if the hint stream were not there.
/// </para>
/// <para>
/// The hint tables have the shape qpdf writes: every object of the first-page section is an entry of the shared-object table (so another page can
/// name one it uses), the first page names no shared objects of its own, and a page's content is described as the whole page.
/// </para>
/// </remarks>
internal static class PdfLinearizer
{
    // An integer of the width the trailer's /Prev is written with, replaced by the real value once the layout is known.
    const int PrevPlaceholder = int.MaxValue;
    const string PrevPlaceholderText = "2147483647";

    sealed class Sections
    {
        public required PdfReference Catalog;

        /// <summary>Part 4 beyond the catalog: what a viewer needs to open the document at all (its form, viewer preferences, open action), and whatever those use.</summary>
        public required List<PdfReference> OpenDocument;
        public required List<PdfReference> FirstPage;
        public required List<List<PdfReference>> OtherPages;
        public required List<PdfReference> Shared;
        public required List<PdfReference> Rest;

        /// <summary>How many objects at the end of <see cref="FirstPage"/> are the outline hierarchy, which has a hint table of its own.</summary>
        public int OutlineObjects;
    }

    public static void Write(PdfDocument document, PdfWriter output, PdfAes256SecurityHandler? security)
    {
        var pageCount = document.Pages.Count;
        var pageObjects = Enumerable.Range(0, pageCount).Select(i => document.Pages[i].RequiredReference).ToList();
        var pageSet = new HashSet<PdfReference>(pageObjects);
        var root = document.Catalog.RequiredReference;
        var all = document._irefTable.AllReferences.Where(r => r.Value is not null).ToList();

        // What each page reaches, and what the document itself (outlines, form, names...) reaches; neither follows a link to another page.
        var reach = pageObjects.Select(p => Reach(p, pageSet, skipKey: null)).ToList();
        var documentReach = Reach(root, pageSet, skipKey: "/Pages");
        var sections = Classify(all, root, pageObjects, reach, documentReach, OutlinesShownAtOpen(root, pageSet), OpenDocumentObjects(root, pageSet));

        var originalIds = all.ToDictionary(r => r, r => r.ObjectId);
        try
        {
            WriteFile(document, output, security, sections, pageObjects, reach);
        }
        finally
        {
            foreach (var (iref, id) in originalIds)
                iref.ObjectId = id;
        }
    }

    // ---------------------------------------------------------------------------------------------------- sections

    static HashSet<PdfReference> Reach(PdfReference start, HashSet<PdfReference> pageSet, string? skipKey)
    {
        var seen = new HashSet<PdfReference> { start };
        var stack = new Stack<PdfItem>();
        stack.Push(start.Value);
        while (stack.Count > 0)
        {
            switch (stack.Pop())
            {
                case PdfReference reference:
                    // A link to another page is not part of this page.
                    if (reference.Value is null || (pageSet.Contains(reference) && !ReferenceEquals(reference, start)))
                        break;
                    if (seen.Add(reference))
                        stack.Push(reference.Value);
                    break;
                case PdfDictionary dictionary:
                {
                    var isPage = dictionary.Reference is { } own && pageSet.Contains(own);
                    foreach (var entry in dictionary)
                    {
                        if ((entry.Key == "/Parent" && isPage) || entry.Key == skipKey)
                            continue;
                        stack.Push(entry.Value);
                    }

                    break;
                }
                case PdfArray array:
                    foreach (var element in array.Elements)
                        stack.Push(element);
                    break;
            }
        }

        return seen;
    }

    /// <summary>
    /// The outline hierarchy when the catalog asks for it to be shown on opening (<c>/PageMode /UseOutlines</c>): the Annex puts it with the first
    /// page, since a viewer needs it to draw the first screen.
    /// </summary>
    static HashSet<PdfReference> OutlinesShownAtOpen(PdfReference root, HashSet<PdfReference> pageSet)
    {
        if (root.Value is PdfDictionary catalog
            && catalog.Elements["/PageMode"] is PdfName { Value: "/UseOutlines" }
            && catalog.Elements["/Outlines"] is PdfReference { Value: not null } outlines)
            return Reach(outlines, pageSet, skipKey: null);

        return [];
    }

    /// <summary>
    /// What the catalog entries a viewer reads when it opens the file reach: the interactive form, viewer preferences, threads and open action. The Annex
    /// puts them right after the catalog, ahead of the first page, even when a page uses them too.
    /// </summary>
    static HashSet<PdfReference> OpenDocumentObjects(PdfReference root, HashSet<PdfReference> pageSet)
    {
        var open = new HashSet<PdfReference>();
        if (root.Value is not PdfDictionary catalog)
            return open;

        foreach (var key in new[] { "/ViewerPreferences", "/Threads", "/OpenAction", "/AcroForm" })
        {
            if (catalog.Elements[key] is not { } value)
                continue;

            if (value is PdfReference { Value: not null } reference)
            {
                open.UnionWith(Reach(reference, pageSet, skipKey: null));
            }
            else if (value is PdfDictionary direct)
            {
                // A direct dictionary has no object of its own, so what it names is reached one by one.
                foreach (var entry in direct)
                {
                    if (entry.Value is PdfReference { Value: not null } inner)
                        open.UnionWith(Reach(inner, pageSet, skipKey: null));
                }
            }
        }

        return open;
    }

    static Sections Classify(List<PdfReference> all, PdfReference root, List<PdfReference> pageObjects, List<HashSet<PdfReference>> reach,
        HashSet<PdfReference> documentReach, HashSet<PdfReference> withFirstPage, HashSet<PdfReference> openDocument)
    {
        var users = new Dictionary<PdfReference, List<int>>();
        for (var i = 0; i < reach.Count; i++)
        {
            foreach (var r in reach[i])
            {
                if (!users.TryGetValue(r, out var list))
                    users[r] = list = [];
                list.Add(i);
            }
        }

        var firstPage = new List<PdfReference>();
        var otherPages = Enumerable.Range(1, Math.Max(0, reach.Count - 1)).Select(_ => new List<PdfReference>()).ToList();
        var shared = new List<PdfReference>();
        var rest = new List<PdfReference>();
        var outlines = new List<PdfReference>();
        var open = new List<PdfReference>();

        foreach (var r in all.OrderBy(r => r.ObjectNumber))
        {
            if (ReferenceEquals(r, root))
                continue;

            if (openDocument.Contains(r))
                open.Add(r);
            else if (withFirstPage.Contains(r))
                outlines.Add(r);
            else if (!users.TryGetValue(r, out var pages))
                rest.Add(r);
            else if (pages.Contains(0))
                firstPage.Add(r);
            else if (pages.Count == 1 && !documentReach.Contains(r))
                otherPages[pages[0] - 1].Add(r);
            else
                shared.Add(r);
        }

        // A page's own dictionary leads its section.
        firstPage.Remove(pageObjects[0]);
        firstPage.Insert(0, pageObjects[0]);
        for (var i = 1; i < pageObjects.Count; i++)
        {
            otherPages[i - 1].Remove(pageObjects[i]);
            otherPages[i - 1].Insert(0, pageObjects[i]);
        }

        // The outline hierarchy ends the first-page section, contiguous, so one hint entry can describe it.
        firstPage.AddRange(outlines);
        return new Sections { Catalog = root, OpenDocument = open, FirstPage = firstPage, OtherPages = otherPages, Shared = shared, Rest = rest, OutlineObjects = outlines.Count };
    }

    // ---------------------------------------------------------------------------------------------------- the file

    static void WriteFile(PdfDocument document, PdfWriter output, PdfAes256SecurityHandler? security, Sections sections,
        List<PdfReference> pageObjects, List<HashSet<PdfReference>> reach)
    {
        var pageCount = pageObjects.Count;

        // The later parts take the low numbers, in file order; the first-page section follows with the parameter dictionary first.
        var later = sections.OtherPages.SelectMany(p => p).Concat(sections.Shared).Concat(sections.Rest).ToList();
        var number = 1;
        foreach (var r in later)
            r.ObjectId = new PdfObjectId(number++, 0);

        var firstSectionStart = number;
        var linearizationNumber = number++;
        sections.Catalog.ObjectId = new PdfObjectId(number++, 0);
        foreach (var r in sections.OpenDocument)
            r.ObjectId = new PdfObjectId(number++, 0);
        var hintNumber = number++;
        foreach (var r in sections.FirstPage)
            r.ObjectId = new PdfObjectId(number++, 0);
        var size = number;

        var bytes = new Dictionary<PdfReference, byte[]>();
        foreach (var r in sections.FirstPage.Concat(later).Concat(sections.OpenDocument).Append(sections.Catalog))
            bytes[r] = Serialize(r, security);

        var header = Scratch(w => w.WriteFileHeader(document));

        // Sizes first, with offsets of zero: nothing here depends on the value of an offset, only on its width.
        var zeros = new Offsets();
        var linearization0 = LinearizationDictionary(linearizationNumber, zeros, pageCount);
        var firstXref0 = FirstPageXref(document, size, firstSectionStart, new long[size - firstSectionStart], 0);
        var hint0 = HintObject(hintNumber, BuildHint(sections, pageObjects, reach, bytes, 0, 0, 0, 0), security);

        long position = header.Length;
        var offsets = new Offsets();
        var linearizationOffset = position;
        position += linearization0.Length;
        var firstXrefOffset = position;
        position += firstXref0.Length;
        var catalogOffset = position;
        position += bytes[sections.Catalog].Length;
        var openOffsets = new List<long>();
        foreach (var r in sections.OpenDocument)
        {
            openOffsets.Add(position);
            position += bytes[r].Length;
        }

        var hintOffset = position;
        var hintLength = hint0.Length;
        position += hintLength;

        var objectOffsets = new Dictionary<PdfReference, long>();
        objectOffsets[sections.Catalog] = catalogOffset;
        foreach (var r in sections.FirstPage)
        {
            objectOffsets[r] = position;
            position += bytes[r].Length;
        }

        var endOfFirstPage = position;
        foreach (var r in later)
        {
            objectOffsets[r] = position;
            position += bytes[r].Length;
        }

        var mainXrefOffset = position;
        var main0 = MainXref(document, firstSectionStart, new long[firstSectionStart], firstXrefOffset);
        var fileLength = position + main0.Length;

        // The hint tables give offsets past the hint stream as if it were not in the file.
        long Adjusted(long offset) => offset >= hintOffset ? offset - hintLength : offset;

        offsets.Length = fileLength;
        offsets.HintOffset = hintOffset;
        offsets.HintLength = hintLength;
        offsets.FirstPageObject = sections.FirstPage[0].ObjectNumber;
        offsets.EndOfFirstPage = endOfFirstPage;
        offsets.MainXrefFirstEntry = mainXrefOffset + 5 + $"0 {firstSectionStart}".Length;

        var firstPageLocation = Adjusted(objectOffsets[sections.FirstPage[0]]);
        var firstSharedNumber = sections.Shared.Count > 0 ? sections.Shared[0].ObjectNumber : 0;
        var firstSharedLocation = sections.Shared.Count > 0 ? Adjusted(objectOffsets[sections.Shared[0]]) : 0;
        var outlineLocation = sections.OutlineObjects > 0 ? Adjusted(objectOffsets[sections.FirstPage[^sections.OutlineObjects]]) : 0;
        var hint = HintObject(hintNumber, BuildHint(sections, pageObjects, reach, bytes, firstPageLocation, firstSharedNumber, firstSharedLocation, outlineLocation), security);
        var linearization = LinearizationDictionary(linearizationNumber, offsets, pageCount);

        var firstEntries = new long[size - firstSectionStart];
        firstEntries[0] = linearizationOffset;
        firstEntries[1] = catalogOffset;
        for (var i = 0; i < openOffsets.Count; i++)
            firstEntries[2 + i] = openOffsets[i];
        firstEntries[2 + openOffsets.Count] = hintOffset;
        for (var i = 0; i < sections.FirstPage.Count; i++)
            firstEntries[3 + openOffsets.Count + i] = objectOffsets[sections.FirstPage[i]];
        var firstXref = FirstPageXref(document, size, firstSectionStart, firstEntries, mainXrefOffset);

        var mainEntries = new long[firstSectionStart];
        foreach (var r in later)
            mainEntries[r.ObjectNumber] = objectOffsets[r];
        var main = MainXref(document, firstSectionStart, mainEntries, firstXrefOffset);

        if (linearization.Length != linearization0.Length || firstXref.Length != firstXref0.Length || hint.Length != hint0.Length || main.Length != main0.Length)
            throw new InvalidOperationException(string.Create(CultureInfo.InvariantCulture,
                $"The linearized layout changed size between its two passes (parameters {linearization0.Length}/{linearization.Length}, first xref {firstXref0.Length}/{firstXref.Length}, hint {hint0.Length}/{hint.Length}, main xref {main0.Length}/{main.Length})."));

        var stream = output.Stream;
        stream.Write(header);
        stream.Write(linearization);
        stream.Write(firstXref);
        stream.Write(bytes[sections.Catalog]);
        foreach (var r in sections.OpenDocument)
            stream.Write(bytes[r]);
        stream.Write(hint);
        foreach (var r in sections.FirstPage)
            stream.Write(bytes[r]);
        foreach (var r in later)
            stream.Write(bytes[r]);
        stream.Write(main);
    }

    sealed class Offsets
    {
        public long Length, HintOffset, HintLength, FirstPageObject, EndOfFirstPage, MainXrefFirstEntry;
    }

    static byte[] Serialize(PdfReference iref, PdfAes256SecurityHandler? security)
    {
        using var stream = new MemoryStream();
        var writer = new PdfWriter(stream) { Encryption = security };
        iref.Value.WriteObject(writer);
        return stream.ToArray();
    }

    static byte[] Scratch(Action<PdfWriter> write)
    {
        using var stream = new MemoryStream();
        write(new PdfWriter(stream));
        return stream.ToArray();
    }

    static string D10(long value) => value.ToString("D10", CultureInfo.InvariantCulture);

    static byte[] Ascii(string text) => Encoding.Latin1.GetBytes(text);

    static byte[] LinearizationDictionary(int number, Offsets o, int pageCount) => Ascii(string.Create(CultureInfo.InvariantCulture,
        $"{number} 0 obj\n<< /Linearized 1 /L {D10(o.Length)} /H [ {D10(o.HintOffset)} {D10(o.HintLength)} ] /O {D10(o.FirstPageObject)} /E {D10(o.EndOfFirstPage)} /N {D10(pageCount)} /T {D10(o.MainXrefFirstEntry)} >>\nendobj\n"));

    static string XrefEntry(long offset) => string.Create(CultureInfo.InvariantCulture, $"{offset:D10} 00000 n \n");

    /// <summary>The first-page cross-reference section and its trailer, which names the main section by offset and is the one the file's last <c>startxref</c> finds.</summary>
    static byte[] FirstPageXref(PdfDocument document, int size, int first, long[] offsets, long mainXrefOffset)
    {
        var table = new StringBuilder();
        table.Append("xref\n").Append(first.ToString(CultureInfo.InvariantCulture)).Append(' ').Append(offsets.Length.ToString(CultureInfo.InvariantCulture)).Append('\n');
        foreach (var offset in offsets)
            table.Append(XrefEntry(offset));
        table.Append("trailer\n");

        var trailer = document._trailer;
        trailer.Elements.SetInteger("/Size", size);
        trailer.Elements["/Prev"] = new PdfInteger(PrevPlaceholder);
        byte[] dictionary;
        try
        {
            dictionary = Scratch(w => trailer.WriteObject(w));
        }
        finally
        {
            trailer.Elements.Remove("/Prev");
        }

        var text = Encoding.Latin1.GetString(dictionary).Replace(PrevPlaceholderText, D10(mainXrefOffset), StringComparison.Ordinal);
        return Ascii(table + text + "startxref\n0\n%%EOF\n");
    }

    /// <summary>The main cross-reference section, covering the objects before the first-page section, and the last <c>startxref</c> of the file.</summary>
    static byte[] MainXref(PdfDocument document, int count, long[] offsets, long firstXrefOffset)
    {
        var table = new StringBuilder();
        table.Append("xref\n0 ").Append(count.ToString(CultureInfo.InvariantCulture)).Append('\n');
        table.Append("0000000000 65535 f \n");
        for (var i = 1; i < count; i++)
            table.Append(XrefEntry(offsets[i]));
        table.Append("trailer\n");

        var trailer = new PdfDictionary(document);
        trailer.Elements.SetInteger("/Size", count);
        if (document._trailer.Elements["/ID"] is { } id)
            trailer.Elements["/ID"] = id;
        var dictionary = Encoding.Latin1.GetString(Scratch(w => trailer.WriteObject(w)));
        return Ascii(table + dictionary + string.Create(CultureInfo.InvariantCulture, $"startxref\n{firstXrefOffset}\n%%EOF\n"));
    }

    // ---------------------------------------------------------------------------------------------------- hint tables

    sealed class BitWriter
    {
        readonly List<byte> _bytes = [];
        int _used;

        public int Length => _bytes.Count;

        public void Write(long value, int bits)
        {
            for (var shift = bits - 1; shift >= 0; shift--)
            {
                if (_used == 0)
                    _bytes.Add(0);
                if (((value >> shift) & 1) != 0)
                    _bytes[^1] |= (byte)(0x80 >> _used);
                _used = (_used + 1) % 8;
            }
        }

        public void Align() => _used = 0;

        public byte[] ToArray() => _bytes.ToArray();
    }

    /// <summary>The primary hint stream's contents: the page offset hint table, then the shared object hint table; the result's first element is the table's length.</summary>
    static (byte[] Data, int SharedTableOffset, int? OutlineTableOffset) BuildHint(Sections sections, List<PdfReference> pageObjects, List<HashSet<PdfReference>> reach,
        Dictionary<PdfReference, byte[]> bytes, long firstPageLocation, long firstSharedNumber, long firstSharedLocation, long outlineLocation)
    {
        var pageCount = pageObjects.Count;
        long Length(IEnumerable<PdfReference> objects) => objects.Sum(r => (long)bytes[r].Length);

        // The shared-object entries: every object of the first-page section, then the shared ones.
        var entries = sections.FirstPage.Concat(sections.Shared).ToList();
        var entryIndex = new Dictionary<PdfReference, int>();
        for (var i = 0; i < entries.Count; i++)
            entryIndex[entries[i]] = i;

        var objectCounts = new long[pageCount];
        var pageLengths = new long[pageCount];
        var sharedIds = new List<int>[pageCount];
        objectCounts[0] = sections.FirstPage.Count;
        pageLengths[0] = Length(sections.FirstPage);
        sharedIds[0] = [];
        for (var i = 1; i < pageCount; i++)
        {
            var own = sections.OtherPages[i - 1];
            objectCounts[i] = own.Count;
            pageLengths[i] = Length(own);
            // In the order of the objects' numbers, which is the order a checker walks the page's objects in.
            sharedIds[i] = reach[i].Where(entryIndex.ContainsKey).OrderBy(r => r.ObjectNumber).Select(r => entryIndex[r]).ToList();
        }

        long leastObjects = objectCounts.Min(), leastLength = pageLengths.Min();
        var maxShared = sharedIds.Max(l => l.Count);
        var bitsObjects = Bits(objectCounts.Max() - leastObjects);
        var bitsLength = Bits(pageLengths.Max() - leastLength);
        var bitsSharedCount = Bits(maxShared);
        var bitsSharedId = Bits(Math.Max(0, entries.Count - 1));

        var w = new BitWriter();
        w.Write(leastObjects, 32);
        w.Write(firstPageLocation, 32);
        w.Write(bitsObjects, 16);
        w.Write(leastLength, 32);
        w.Write(bitsLength, 16);
        w.Write(0, 32);              // least content stream offset: a content stream is described as starting with its page
        w.Write(0, 16);
        w.Write(leastLength, 32);    // least content stream length: and as being as long as the page
        w.Write(bitsLength, 16);
        w.Write(bitsSharedCount, 16);
        w.Write(bitsSharedId, 16);
        w.Write(0, 16);              // bits for the numerator of a shared object's fractional position (none are given)
        w.Write(4, 16);              // and its denominator

        foreach (var count in objectCounts)
            w.Write(count - leastObjects, bitsObjects);
        w.Align();
        foreach (var length in pageLengths)
            w.Write(length - leastLength, bitsLength);
        w.Align();
        foreach (var ids in sharedIds)
            w.Write(ids.Count, bitsSharedCount);
        w.Align();
        foreach (var ids in sharedIds)
        {
            foreach (var id in ids)
                w.Write(id, bitsSharedId);
        }

        w.Align();
        w.Align();                   // the numerators of the shared references' positions: none, as their width is zero
        w.Align();                   // the content stream offsets: none, as their width is zero
        foreach (var length in pageLengths)
            w.Write(length - leastLength, bitsLength);
        w.Align();

        var sharedTableOffset = w.Length;

        var entryLengths = entries.Select(r => (long)bytes[r].Length).ToList();
        var leastEntry = entryLengths.Count == 0 ? 0 : entryLengths.Min();
        var bitsEntry = entryLengths.Count == 0 ? 0 : Bits(entryLengths.Max() - leastEntry);
        w.Write(firstSharedNumber, 32);
        w.Write(firstSharedLocation, 32);
        w.Write(sections.FirstPage.Count, 32);
        w.Write(entries.Count, 32);
        w.Write(0, 16);              // bits for the number of objects in a group: every entry is one object
        w.Write(leastEntry, 32);
        w.Write(bitsEntry, 16);
        foreach (var length in entryLengths)
            w.Write(length - leastEntry, bitsEntry);
        w.Align();
        foreach (var _ in entryLengths)
            w.Write(0, 1);           // no MD5 signatures
        w.Align();

        // The outline hint table: the object number, location and count of the outline hierarchy and its length in bytes.
        int? outlineTableOffset = null;
        if (sections.OutlineObjects > 0)
        {
            var outlineObjects = sections.FirstPage.Skip(sections.FirstPage.Count - sections.OutlineObjects).ToList();
            outlineTableOffset = w.Length;
            w.Write(outlineObjects[0].ObjectNumber, 32);
            w.Write(outlineLocation, 32);
            w.Write(outlineObjects.Count, 32);
            w.Write(Length(outlineObjects), 32);
            w.Align();
        }

        return (w.ToArray(), sharedTableOffset, outlineTableOffset);
    }

    static byte[] HintObject(int number, (byte[] Data, int SharedTableOffset, int? OutlineTableOffset) hint, PdfAes256SecurityHandler? security)
    {
        var data = security is null ? hint.Data : security.Encrypt(hint.Data);
        using var stream = new MemoryStream();
        var head = Ascii(string.Create(CultureInfo.InvariantCulture, $"{number} 0 obj\n<< /S {hint.SharedTableOffset}{(hint.OutlineTableOffset is { } outline ? $" /O {outline}" : "")} /Length {data.Length} >>\nstream\n"));
        stream.Write(head);
        stream.Write(data);
        stream.Write(Ascii("\nendstream\nendobj\n"));
        return stream.ToArray();
    }

    static int Bits(long value)
    {
        var bits = 0;
        for (; value > 0; value >>= 1)
            bits++;
        return bits;
    }
}
