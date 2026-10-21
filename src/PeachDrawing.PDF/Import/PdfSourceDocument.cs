using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachDrawing.PDF.Import;

/// <summary>A stream's bytes after the general filters, with the image codec (if any) it still carries.</summary>
internal readonly record struct PdfDecodedStream(byte[] Data, string? ImageFilter, PdfDictionary? ImageParameters);

/// <summary>
/// An existing PDF opened for reading. The file is parsed into the engine's own object model
/// (<see cref="PdfDictionary"/>, <see cref="PdfArray"/>, <see cref="PdfReference"/>...), with indirect objects loaded the
/// first time something follows a reference to them. A file whose cross-reference data is missing or damaged is
/// reconstructed by scanning for its objects.
/// </summary>
internal sealed class PdfSourceDocument
{
    enum EntryKind { Free, InFile, InObjectStream }

    readonly record struct XrefEntry(EntryKind Kind, long A, int B);

    readonly byte[] _data;
    readonly PdfDocument _doc;
    readonly Dictionary<int, XrefEntry> _xref = new();
    readonly Dictionary<int, PdfReference> _refs = new();
    readonly Dictionary<int, PdfItem> _loaded = new();
    readonly HashSet<int> _loading = new();
    readonly Dictionary<int, List<(int Number, PdfItem Item)>> _objectStreams = new();
    bool _rebuilt;
    PdfDecryptor? _decryptor;
    int _encryptObjectNumber = -1;

    PdfSourceDocument(byte[] data)
    {
        _data = data;
        _doc = new PdfDocument { _openMode = Engine.IO.PdfDocumentOpenMode.Import };
        _doc._state |= DocumentState.Imported;
        _doc.ImportLoader = LoadInto;
        Trailer = new PdfDictionary(_doc);
    }

    /// <summary>The merged trailer dictionary (newest entries win).</summary>
    public PdfDictionary Trailer { get; }

    /// <summary>The pages in document order.</summary>
    public IReadOnlyList<PdfDictionary> Pages { get; private set; } = [];

    /// <summary>Opens a PDF held in memory.</summary>
    /// <exception cref="InvalidDataException">The data is not a readable PDF.</exception>
    /// <exception cref="NotSupportedException">The file is encrypted.</exception>
    public static PdfSourceDocument Open(byte[] data)
    {
        var doc = new PdfSourceDocument(data);
        const string needsPassword = "The PDF needs a password to open.";
        try { doc.Load(); }
        catch (InvalidDataException) when (doc._decryptor is not null)
        {
            // A revision 4 file cannot be checked against the empty password (that needs RC4), so a wrong one decrypts to noise.
            throw new NotSupportedException(needsPassword);
        }

        // Read the first page's content now: it is what shows a wrong key, as AES output with padding that cannot be right.
        if (doc._decryptor is not null && doc.Pages.Count > 0)
        {
            _ = doc.GetPageContent(doc.Pages[0]);
            if (doc._decryptor.SawInvalidPadding) throw new NotSupportedException(needsPassword);
        }

        return doc;
    }

    void Load()
    {
        bool ok = false;
        try { ok = ReadXrefChain(); } catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) { }
        // Objects are decrypted as they are loaded, so the file key is worked out before any of them is read.
        if (ok) SetUpEncryption();
        if (!ok || !HasUsableRoot()) { Rebuild(); SetUpEncryption(); }
        try { Pages = CollectPages(); }
        catch (InvalidDataException) when (!_rebuilt) { Rebuild(); SetUpEncryption(); Pages = CollectPages(); }
        if (Pages.Count == 0 && !_rebuilt) { Rebuild(); SetUpEncryption(); Pages = CollectPages(); }
    }

    /// <summary>Reads the trailer's <c>/Encrypt</c> entry, if any, and starts decrypting what is loaded from here on (the empty user password).</summary>
    void SetUpEncryption()
    {
        _decryptor = null;
        if (Trailer.GetRaw("Encrypt") is not { } entry) return;
        _encryptObjectNumber = entry is PdfReference reference ? reference.ObjectNumber : -1;
        var encrypt = Resolve(entry) as PdfDictionary ?? throw new InvalidDataException("The PDF's encryption dictionary is unreadable.");
        byte[] id = [];
        if (Resolve(Trailer.GetRaw("ID")) is PdfArray { Elements.Count: > 0 } ids && Resolve(ids.Elements[0]) is PdfString first)
            id = Encoding.Latin1.GetBytes(first.Value);
        _decryptor = PdfDecryptor.Create(this, encrypt, id);

        // Anything read before the key was known (a rebuild scans every object) was read as ciphertext.
        _loaded.Clear();
        _objectStreams.Clear();
        foreach (var r in _refs.Values) r.SetObject(null!);
    }

    bool HasUsableRoot()
    {
        return Resolve(Trailer.GetRaw("Root")) is PdfDictionary root && Resolve(root.GetRaw("Pages")) is PdfDictionary;
    }

    // ---------------------------------------------------------------- cross-reference data

    bool ReadXrefChain()
    {
        int tail = Math.Max(0, _data.Length - 2048);
        int sx = LastIndexOf("startxref", tail);
        if (sx < 0) return false;
        var lx = new PdfLexer(_data, sx + 9);
        var t = lx.Next();
        if (t.Kind != PdfTokenKind.Integer) return false;
        long offset = t.Integer;
        var seen = new HashSet<long>();
        var queue = new Queue<long>();
        queue.Enqueue(offset);
        bool any = false;
        while (queue.Count > 0)
        {
            long off = queue.Dequeue();
            if (off < 0 || off >= _data.Length || !seen.Add(off)) continue;
            var trailer = ReadXrefSection((int)off);
            if (trailer is null) return any;
            any = true;
            foreach (var kv in trailer)
                if (!Trailer.ContainsKeyRaw(kv.Key.TrimStart('/')) && kv.Value is not PdfNull)
                    Trailer.Elements[kv.Key] = kv.Value;
            if (trailer.GetRaw("XRefStm") is PdfInteger xs) queue.Enqueue(xs.Value);
            if (trailer.GetRaw("Prev") is PdfInteger p) queue.Enqueue(p.Value);
            else if (trailer.GetRaw("Prev") is PdfReal pr) queue.Enqueue((long)pr.Value);
        }
        return any;
    }

    /// <summary>Reads one xref table or xref stream at <paramref name="offset"/>; returns its trailer dictionary.</summary>
    PdfDictionary? ReadXrefSection(int offset)
    {
        var lx = new PdfLexer(_data, offset);
        var t = lx.Next();
        if (t.Kind == PdfTokenKind.Keyword && t.Text == "xref")
        {
            while (true)
            {
                var a = lx.Next();
                if (a.Kind == PdfTokenKind.Keyword && a.Text == "trailer") break;
                var b = lx.Next();
                if (a.Kind != PdfTokenKind.Integer || b.Kind != PdfTokenKind.Integer) return null;
                long first = a.Integer;
                for (long i = 0; i < b.Integer; i++)
                {
                    var off = lx.Next();
                    var gen = lx.Next();
                    var kind = lx.Next();
                    if (off.Kind != PdfTokenKind.Integer || gen.Kind != PdfTokenKind.Integer || kind.Kind != PdfTokenKind.Keyword) return null;
                    int num = (int)(first + i);
                    if (_xref.ContainsKey(num)) continue;
                    _xref[num] = kind.Text == "n"
                        ? new XrefEntry(EntryKind.InFile, off.Integer, (int)gen.Integer)
                        : new XrefEntry(EntryKind.Free, 0, 0);
                }
            }
            return ParseItem(lx, lx.Next()) as PdfDictionary;
        }

        // Cross-reference stream: "n g obj << /Type /XRef ... >> stream".
        if (t.Kind != PdfTokenKind.Integer) return null;
        var obj = ParseIndirect(lx, t, out _, out _);
        if (obj is not PdfDictionary dict || dict.Stream is null) return null;
        if (!(dict.GetRaw("Type") is PdfName { Value: "/XRef" })) return null;
        byte[] body = DecodeStream(dict).Data;
        var w = (Resolve(dict.GetRaw("W")) as PdfArray)?.Elements.Select(i => IntOf(i)).ToArray();
        if (w is null || w.Length < 3) return null;
        int size = IntOf(dict.GetRaw("Size"));
        var index = new List<(int, int)>();
        if (Resolve(dict.GetRaw("Index")) is PdfArray ia)
            for (int i = 0; i + 1 < ia.Elements.Count; i += 2) index.Add((IntOf(ia.Elements[i]), IntOf(ia.Elements[i + 1])));
        else index.Add((0, size));
        int pos = 0;
        int rowLen = w[0] + w[1] + w[2];
        foreach (var (start, count) in index)
        {
            for (int i = 0; i < count && pos + rowLen <= body.Length; i++)
            {
                long type = w[0] == 0 ? 1 : ReadBe(body, pos, w[0]);
                long f2 = ReadBe(body, pos + w[0], w[1]);
                long f3 = ReadBe(body, pos + w[0] + w[1], w[2]);
                pos += rowLen;
                int num = start + i;
                if (_xref.ContainsKey(num)) continue;
                _xref[num] = type switch
                {
                    1 => new XrefEntry(EntryKind.InFile, f2, (int)f3),
                    2 => new XrefEntry(EntryKind.InObjectStream, f2, (int)f3),
                    _ => new XrefEntry(EntryKind.Free, 0, 0),
                };
            }
        }
        return dict;
    }

    static long ReadBe(byte[] d, int pos, int n)
    {
        long v = 0;
        for (int i = 0; i < n; i++) v = (v << 8) | d[pos + i];
        return v;
    }

    /// <summary>Reconstructs the object table by scanning the file for "n g obj" and for trailer data.</summary>
    void Rebuild()
    {
        _rebuilt = true;
        _xref.Clear();
        _refs.Clear();
        _loaded.Clear();
        _objectStreams.Clear();
        Trailer.Elements.Clear();
        PdfDictionary? lastTrailer = null;
        var lx = new PdfLexer(_data);
        var xrefStreams = new List<int>();
        int i = 0;
        while (i < _data.Length)
        {
            // Look for the "obj" keyword at line starts; walk backward over two integers.
            int p = IndexOf("obj", i);
            if (p < 0) break;
            i = p + 3;
            if (p > 0 && _data[p - 1] is (byte)'d' || (i < _data.Length && !PdfLexer.IsWhite(_data[i]) && !PdfLexer.IsDelimiter(_data[i]))) continue;
            int q = p - 1;
            while (q >= 0 && PdfLexer.IsWhite(_data[q])) q--;
            int genEnd = q + 1;
            while (q >= 0 && _data[q] >= '0' && _data[q] <= '9') q--;
            int genStart = q + 1;
            if (genStart == genEnd) continue;
            while (q >= 0 && PdfLexer.IsWhite(_data[q])) q--;
            int numEnd = q + 1;
            while (q >= 0 && _data[q] >= '0' && _data[q] <= '9') q--;
            int numStart = q + 1;
            if (numStart == numEnd) continue;
            if (!int.TryParse(Encoding.ASCII.GetString(_data, numStart, numEnd - numStart), out int num)) continue;
            int.TryParse(Encoding.ASCII.GetString(_data, genStart, genEnd - genStart), out int gen);
            _xref[num] = new XrefEntry(EntryKind.InFile, numStart, gen); // later definitions win
        }
        // Trailers: classic "trailer" dictionaries and /Type /XRef streams both carry /Root.
        int t = 0;
        while ((t = IndexOf("trailer", t)) >= 0)
        {
            lx.Position = t + 7;
            if (ParseItem(lx, lx.Next()) is PdfDictionary d) { lastTrailer = Merge(lastTrailer, d); }
            t += 7;
        }
        foreach (var kv in _xref.ToArray())
        {
            if (kv.Value.Kind != EntryKind.InFile) continue;
            try
            {
                var item = LoadObject(kv.Key);
                if (item is PdfDictionary d && d.GetRaw("Type") is PdfName { Value: "/XRef" })
                    lastTrailer = Merge(lastTrailer, d);
                else if (item is PdfDictionary d2 && d2.GetRaw("Type") is PdfName { Value: "/ObjStm" })
                    xrefStreams.Add(kv.Key);
                else if (item is PdfDictionary c && c.GetRaw("Type") is PdfName { Value: "/Catalog" } && lastTrailer?.GetRaw("Root") is null)
                    lastTrailer = Merge(lastTrailer, MakeTrailer(kv.Key));
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) { }
        }
        foreach (int stm in xrefStreams)
        {
            try
            {
                foreach (var (num, _) in GetObjectStream(stm))
                {
                    if (!_xref.TryGetValue(num, out var existing) || existing.Kind != EntryKind.InFile)
                        _xref[num] = new XrefEntry(EntryKind.InObjectStream, stm, 0);
                }
            }
            catch (Exception e) when (e is InvalidDataException or IndexOutOfRangeException or ArgumentException) { }
        }
        if (lastTrailer is not null)
            foreach (var kv in lastTrailer)
                if (kv.Value is not PdfNull) Trailer.Elements[kv.Key] = kv.Value;
        if (Trailer.GetRaw("Root") is null)
            throw new InvalidDataException("The PDF has no document catalog and could not be repaired.");
    }

    /// <summary>A new, empty dictionary owned by this document, for an object that has no place in the file (an inline image).</summary>
    public PdfDictionary NewDictionary() => new(_doc);

    PdfDictionary MakeTrailer(int catalogNumber)
    {
        var d = new PdfDictionary(_doc);
        d.Elements["/Root"] = GetRef(catalogNumber);
        return d;
    }

    PdfDictionary Merge(PdfDictionary? into, PdfDictionary from)
    {
        if (into is null) return from;
        foreach (var kv in from) into.Elements[kv.Key] = kv.Value; // later trailers override earlier ones
        return into;
    }

    // ---------------------------------------------------------------- object access

    PdfReference GetRef(int number, int generation = 0)
    {
        if (!_refs.TryGetValue(number, out var r))
        {
            r = new PdfReference(new PdfObjectId(number, generation), 0) { Document = _doc };
            _refs[number] = r;
        }
        return r;
    }

    /// <summary>Follows an indirect reference (repeatedly) to the object or value it names; null for a missing object.</summary>
    public PdfItem? Resolve(PdfItem? item)
    {
        for (int hops = 0; item is PdfReference r; hops++)
        {
            if (hops > 32) return null;
            item = LoadObject(r.ObjectNumber);
        }
        return item is PdfNull ? null : item;
    }

    void LoadInto(PdfReference r)
    {
        if (LoadObject(r.ObjectNumber) is PdfObject o && o.Reference is null) r.Value = o;
    }

    /// <summary>Loads indirect object <paramref name="number"/>; a missing or free object is the null object.</summary>
    PdfItem LoadObject(int number)
    {
        if (_loaded.TryGetValue(number, out var cached)) return cached;
        if (!_xref.TryGetValue(number, out var entry) || entry.Kind == EntryKind.Free) return PdfNull.Instance;
        if (!_loading.Add(number)) return PdfNull.Instance;
        try
        {
            PdfItem result;
            if (entry.Kind == EntryKind.InFile)
            {
                var lx = new PdfLexer(_data, (int)entry.A);
                var first = lx.Next();
                PdfItem? item = null;
                if (first.Kind == PdfTokenKind.Integer && first.Integer == number)
                    item = ParseIndirect(lx, first, out _, out _);
                if (item is not null && _decryptor is not null && number != _encryptObjectNumber)
                    _decryptor.DecryptObject(item, number, entry.B);
                if (item is null)
                {
                    if (!_rebuilt) { _loading.Remove(number); Rebuild(); return LoadObject(number); }
                    item = PdfNull.Instance;
                }
                result = item;
                if (result is PdfObject po && po.Reference is null) GetRef(number, entry.B).Value = po;
            }
            else
            {
                result = PdfNull.Instance;
                foreach (var (n, it) in GetObjectStream((int)entry.A))
                    if (n == number) { result = it; break; }
                if (result is PdfObject po && po.Reference is null) GetRef(number).Value = po;
            }
            _loaded[number] = result;
            return result;
        }
        finally { _loading.Remove(number); }
    }

    List<(int Number, PdfItem Item)> GetObjectStream(int streamNumber)
    {
        if (_objectStreams.TryGetValue(streamNumber, out var cached)) return cached;
        var list = new List<(int, PdfItem)>();
        _objectStreams[streamNumber] = list; // guards re-entrancy
        if (LoadObject(streamNumber) is PdfDictionary stm && stm.Stream is not null)
        {
            byte[] body = DecodeStream(stm).Data;
            int n = IntOf(stm.GetRaw("N"));
            int first = IntOf(stm.GetRaw("First"));
            var lx = new PdfLexer(body);
            var offsets = new List<(int Num, int Off)>();
            for (int i = 0; i < n; i++)
            {
                var a = lx.Next(); var b = lx.Next();
                if (a.Kind != PdfTokenKind.Integer || b.Kind != PdfTokenKind.Integer) break;
                offsets.Add(((int)a.Integer, (int)b.Integer));
            }
            foreach (var (num, off) in offsets)
            {
                if (first + off >= body.Length) continue;
                var ol = new PdfLexer(body, first + off);
                list.Add((num, ParseItem(ol, ol.Next())));
            }
        }
        return list;
    }

    /// <summary>Parses "n g obj ... endobj" whose first integer token has been read; returns the object (with its stream).</summary>
    PdfItem? ParseIndirect(PdfLexer lx, PdfToken numberToken, out int number, out int generation)
    {
        number = (int)numberToken.Integer;
        var g = lx.Next();
        var kw = lx.Next();
        generation = (int)g.Integer;
        if (g.Kind != PdfTokenKind.Integer || kw.Kind != PdfTokenKind.Keyword || kw.Text != "obj") return null;
        var item = ParseItem(lx, lx.Next());
        if (item is PdfDictionary dict)
        {
            int save = lx.Position;
            var next = lx.Next();
            if (next.Kind == PdfTokenKind.Keyword && next.Text == "stream")
            {
                int p = lx.Position;
                if (p < lx.End && _data[p] == 13) p++;
                if (p < lx.End && _data[p] == 10) p++;
                dict.Stream = new PdfDictionary.PdfStream(ReadStreamBytes(dict, p), dict);
            }
            else lx.Position = save;
        }
        return item;
    }

    byte[] ReadStreamBytes(PdfDictionary dict, int start)
    {
        int length = -1;
        if (dict.GetRaw("Length") is { } lenItem && Resolve(lenItem) is { } l)
            length = IntOf(l);
        if (length >= 0 && start + length <= _data.Length)
        {
            var check = new PdfLexer(_data, start + length);
            var t = check.Next();
            if (t.Kind == PdfTokenKind.Keyword && t.Text == "endstream")
                return _data.AsSpan(start, length).ToArray();
        }
        int end = IndexOf("endstream", start);
        if (end < 0) end = _data.Length;
        // The EOL before endstream is not part of the data.
        if (end > start && _data[end - 1] == 10) end--;
        if (end > start && _data[end - 1] == 13) end--;
        return _data.AsSpan(start, Math.Max(0, end - start)).ToArray();
    }

    // ---------------------------------------------------------------- object syntax

    /// <summary>Parses one object whose first token is <paramref name="t"/>; the "n g R" reference form is recognised.</summary>
    internal PdfItem ParseItem(PdfLexer lx, PdfToken t, int depth = 0)
    {
        if (depth > 200) throw new InvalidDataException("PDF object nesting is too deep.");
        switch (t.Kind)
        {
            case PdfTokenKind.Integer:
            {
                int save = lx.Position;
                var g = lx.Next();
                if (g.Kind == PdfTokenKind.Integer)
                {
                    var r = lx.Next();
                    if (r.Kind == PdfTokenKind.Keyword && r.Text == "R" && t.Integer >= 0)
                        return GetRef((int)t.Integer, (int)g.Integer);
                }
                lx.Position = save;
                return new PdfInteger((int)Math.Clamp(t.Integer, int.MinValue, int.MaxValue));
            }
            case PdfTokenKind.Real: return new PdfReal(t.Real);
            case PdfTokenKind.Name: return new PdfName("/" + t.Text);
            case PdfTokenKind.String: return new PdfString(t.Text, PdfStringEncoding.LatinOneByteEncoding);
            case PdfTokenKind.ArrayStart:
            {
                var arr = new PdfArray(_doc);
                while (true)
                {
                    var n = lx.Next();
                    if (n.Kind is PdfTokenKind.ArrayEnd or PdfTokenKind.Eof) break;
                    if (n.Kind == PdfTokenKind.Keyword && n.Text is "endobj" or "endstream") { lx.Position = (int)n.Start; break; }
                    arr.Elements.Add(ParseItem(lx, n, depth + 1));
                }
                return arr;
            }
            case PdfTokenKind.DictStart:
            {
                var dict = new PdfDictionary(_doc);
                while (true)
                {
                    var k = lx.Next();
                    if (k.Kind is PdfTokenKind.DictEnd or PdfTokenKind.Eof) break;
                    if (k.Kind == PdfTokenKind.Keyword && k.Text is "endobj" or "stream") { lx.Position = (int)k.Start; break; }
                    if (k.Kind != PdfTokenKind.Name) continue; // damaged entry: resynchronise on the next name
                    var vt = lx.Next();
                    if (vt.Kind is PdfTokenKind.DictEnd or PdfTokenKind.Eof) break;
                    var v = ParseItem(lx, vt, depth + 1);
                    if (v is not PdfNull) dict.Elements["/" + k.Text] = v;
                }
                return dict;
            }
            case PdfTokenKind.Keyword:
                return t.Text switch
                {
                    "true" => new PdfBoolean(true),
                    "false" => new PdfBoolean(false),
                    _ => PdfNull.Instance, // "null" and anything unrecognised
                };
            default:
                return PdfNull.Instance;
        }
    }

    // ---------------------------------------------------------------- typed access

    /// <summary>The number at <paramref name="item"/> (resolving a reference), or 0.</summary>
    public int IntOf(PdfItem? item) => Resolve(item) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => (int)r.Value,
        _ => 0,
    };

    /// <summary>The number at <paramref name="item"/> (resolving a reference), or <paramref name="fallback"/>.</summary>
    public double NumberOf(PdfItem? item, double fallback = 0) => Resolve(item) switch
    {
        PdfInteger i => i.Value,
        PdfReal r => r.Value,
        _ => fallback,
    };

    /// <summary>The name (with its slash) at <paramref name="item"/>, or null.</summary>
    public string? NameOf(PdfItem? item) => (Resolve(item) as PdfName)?.Value;

    /// <summary>The dictionary stored under <paramref name="key"/> (no slash) in <paramref name="d"/>, resolving a reference.</summary>
    public PdfDictionary? DictOf(PdfDictionary d, string key) => Resolve(d.GetRaw(key)) as PdfDictionary;

    /// <summary>The array stored under <paramref name="key"/> (no slash) in <paramref name="d"/>, resolving a reference.</summary>
    public PdfArray? ArrayOf(PdfDictionary d, string key) => Resolve(d.GetRaw(key)) as PdfArray;

    // ---------------------------------------------------------------- streams

    /// <summary>
    /// Decodes <paramref name="dict"/>'s stream through its general filters. Decoding stops at the first image codec
    /// (DCT, JPX, CCITT, JBIG2), which is reported so the caller can hand the bytes to an image decoder.
    /// </summary>
    public PdfDecodedStream DecodeStream(PdfDictionary dict)
    {
        byte[] data = dict.Stream?.Value ?? [];
        var filters = new List<string>();
        var parms = new List<PdfDictionary?>();
        var f = Resolve(dict.GetRaw("Filter") ?? dict.GetRaw("F"));
        var dp = Resolve(dict.GetRaw("DecodeParms") ?? dict.GetRaw("DP"));
        if (f is PdfName fn)
        {
            filters.Add(fn.Value.TrimStart('/'));
            parms.Add((dp as PdfDictionary) ?? (dp as PdfArray is { } da && da.Elements.Count > 0 ? Resolve(da.Elements[0]) as PdfDictionary : null));
        }
        else if (f is PdfArray fa)
        {
            for (int i = 0; i < fa.Elements.Count; i++)
            {
                filters.Add((NameOf(fa.Elements[i]) ?? "/").TrimStart('/'));
                parms.Add(dp is PdfArray pa && i < pa.Elements.Count ? Resolve(pa.Elements[i]) as PdfDictionary : (i == 0 ? dp as PdfDictionary : null));
            }
        }
        for (int i = 0; i < filters.Count; i++)
        {
            string name = PdfStreamFilters.Canonical(filters[i]);
            if (!PdfStreamFilters.IsGeneral(name))
                return new PdfDecodedStream(data, name, parms[i]);
            var p = parms[i];
            data = PdfStreamFilters.Decode(name, data,
                p is null ? 1 : OrDefault(p, "Predictor", 1),
                p is null ? 1 : OrDefault(p, "Colors", 1),
                p is null ? 8 : OrDefault(p, "BitsPerComponent", 8),
                p is null ? 1 : OrDefault(p, "Columns", 1),
                p is null ? 1 : OrDefault(p, "EarlyChange", 1));
        }
        return new PdfDecodedStream(data, null, null);
    }

    int OrDefault(PdfDictionary d, string key, int fallback) => d.GetRaw(key) is { } v && Resolve(v) is PdfInteger or PdfReal ? IntOf(v) : fallback;

    // ---------------------------------------------------------------- pages

    List<PdfDictionary> CollectPages()
    {
        var pages = new List<PdfDictionary>();
        var root = Resolve(Trailer.GetRaw("Root")) as PdfDictionary ?? throw new InvalidDataException("The PDF has no document catalog.");
        var top = Resolve(root.GetRaw("Pages")) as PdfDictionary ?? throw new InvalidDataException("The PDF has no page tree.");
        Walk(top, pages, new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance), 0);
        return pages;
    }

    void Walk(PdfDictionary node, List<PdfDictionary> pages, HashSet<PdfDictionary> seen, int depth)
    {
        if (depth > 64 || !seen.Add(node)) return;
        string? type = NameOf(node.GetRaw("Type"));
        if (ArrayOf(node, "Kids") is { } kids && type != "/Page")
        {
            foreach (var k in kids.Elements)
                if (Resolve(k) is PdfDictionary child)
                {
                    if (child.GetRaw("Parent") is null) child.Elements["/Parent"] = GetRefOf(node);
                    Walk(child, pages, seen, depth + 1);
                }
        }
        else pages.Add(node);
    }

    PdfItem GetRefOf(PdfDictionary d) => d.Reference ?? (PdfItem)d;

    /// <summary>The value of an inheritable page attribute (Resources, MediaBox, CropBox, Rotate), or null.</summary>
    public PdfItem? InheritedAttribute(PdfDictionary page, string key)
    {
        var d = page;
        for (int depth = 0; d is not null && depth < 64; depth++)
        {
            if (d.GetRaw(key) is { } v) return Resolve(v);
            d = Resolve(d.GetRaw("Parent")) as PdfDictionary;
        }
        return null;
    }

    /// <summary>The page's content streams, decoded and joined with a newline between them.</summary>
    public byte[] GetPageContent(PdfDictionary page)
    {
        var contents = Resolve(page.GetRaw("Contents"));
        var ms = new MemoryStream();
        void Append(PdfItem? item)
        {
            if (Resolve(item) is PdfDictionary { Stream: not null } s)
            {
                var bytes = DecodeStream(s).Data;
                ms.Write(bytes);
                ms.WriteByte((byte)'\n');
            }
        }
        if (contents is PdfArray arr) foreach (var i in arr.Elements) Append(i);
        else Append(contents);
        return ms.ToArray();
    }

    // ---------------------------------------------------------------- byte search

    int IndexOf(string s, int from)
    {
        var needle = Encoding.ASCII.GetBytes(s);
        int idx = from < 0 ? 0 : from;
        if (idx >= _data.Length) return -1;
        int r = _data.AsSpan(idx).IndexOf(needle);
        return r < 0 ? -1 : r + idx;
    }

    int LastIndexOf(string s, int from)
    {
        var needle = Encoding.ASCII.GetBytes(s);
        int r = _data.AsSpan(from).LastIndexOf(needle);
        return r < 0 ? -1 : r + from;
    }
}

/// <summary>Raw (non-resolving, slash-optional) dictionary access used by the importer.</summary>
internal static class PdfImportDictionaryExtensions
{
    /// <summary>The value stored under <paramref name="key"/>, which is given without its slash; indirect references are not followed.</summary>
    public static PdfItem? GetRaw(this PdfDictionary d, string key) => d.Elements[key.StartsWith('/') ? key : "/" + key];

    /// <summary>Whether <paramref name="key"/> (without its slash) is present.</summary>
    public static bool ContainsKeyRaw(this PdfDictionary d, string key) => d.Elements.ContainsKey(key.StartsWith('/') ? key : "/" + key);
}
