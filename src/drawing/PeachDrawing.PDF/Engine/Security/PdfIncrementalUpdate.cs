using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Security.Cryptography;
using System.Text;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine.Advanced;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Engine.Security;

/// <summary>
/// Appends a signature field to an existing PDF as an incremental update (ISO 32000-1 §7.5.6): the original bytes stay exactly as they
/// were, which is what keeps an earlier signature valid, and only the objects that change (the catalog, the form, the first page, the
/// arrays that list the fields and annotations) are written again, with the new field and its value, followed by a new cross-reference
/// section that points back at the old one.
/// </summary>
internal static class PdfIncrementalUpdate
{
    static readonly IReadOnlyDictionary<string, string> NoOverrides = new Dictionary<string, string>();

    /// <summary>
    /// The file with a signature field appended whose value is <paramref name="valueDictionary"/> (text that holds the placeholders a seal
    /// fills in).
    /// </summary>
    /// <param name="original">the bytes of the file</param>
    /// <param name="valueDictionary">makes the signature value dictionary as PDF syntax; it is given what encrypts a string of that object (null for an unencrypted file)</param>
    /// <param name="fieldNamePrefix">what an unused field name starts with: the first free "prefix1", "prefix2"... is taken</param>
    /// <param name="visible">the box to draw the signature as, or <see langword="null"/> for an invisible field on the first page</param>
    /// <param name="password">the password of an encrypted file, or <see langword="null"/></param>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="NotSupportedException">the file is encrypted with RC4 or an unknown handler</exception>
    /// <exception cref="InvalidDataException">the file's cross-reference data is damaged, or it has no page to put the field on</exception>
    public static byte[] AppendSignatureField(byte[] original, Func<Func<byte[], byte[]>?, string> valueDictionary, string fieldNamePrefix, VisibleSignature? visible = null, string? password = null)
    {
        var source = PdfSourceDocument.Open(original, password);

        // In an encrypted file every string and stream of an object is stored under that object's own key, so each object written here
        // is encrypted by number; the new objects have generation 0 and a rewritten one keeps its own.
        var decryptor = source.Decryptor;
        Func<byte[], byte[]>? Encrypt(int number) => decryptor is { EncryptsStrings: true }
            ? bytes => decryptor.EncryptString(bytes, number, source.GenerationOf(number))
            : null;

        var rootItem = source.Trailer.GetRaw("Root") as PdfReference ?? throw new InvalidDataException("The PDF has no document catalog.");
        var root = source.Resolve(rootItem) as PdfDictionary ?? throw new InvalidDataException("The PDF's document catalog is unreadable.");
        var pageNumber = visible?.PageNumber ?? 1;
        if (source.Pages.Count == 0)
            throw new InvalidDataException("The PDF has no page to put a signature on.");
        if (pageNumber > source.Pages.Count)
            throw new InvalidOperationException($"PdfSignatureBox.PageNumber is {pageNumber}, but the PDF has {source.Pages.Count} page(s).");
        var page = source.Pages[pageNumber - 1];
        if (page.Reference is not { } pageReference)
            throw new InvalidDataException("The PDF's page is not an indirect object.");

        // The widget's rectangle is in the page's own space; for a visible signature it is found from the box in page-as-shown points.
        string rect = "[0 0 0 0]";
        double boxWidth = 0, boxHeight = 0;
        if (visible is not null)
        {
            if (new PdfSourceNavigation(source).Conformance() is { } level && (level.PdfA != PdfAConformance.None || level.PdfX != PdfXConformance.None))
                throw new InvalidOperationException("A visible signature is drawn in a standard font that is not embedded, which PDF/A and PDF/X do not allow.");
            if (source.IntOf(source.InheritedAttribute(page, "Rotate")) % 360 != 0)
                throw new NotSupportedException("A visible signature cannot be placed on a rotated page.");

            Matrix3x2.Invert(PdfPagePainter.PageToCanvas(source, page), out var toPage);
            var a = Vector2.Transform(new Vector2((float)visible.Bounds.X, (float)visible.Bounds.Y), toPage);
            var b = Vector2.Transform(new Vector2((float)(visible.Bounds.X + visible.Bounds.Width), (float)(visible.Bounds.Y + visible.Bounds.Height)), toPage);
            double x0 = Math.Min(a.X, b.X), y0 = Math.Min(a.Y, b.Y), x1 = Math.Max(a.X, b.X), y1 = Math.Max(a.Y, b.Y);
            boxWidth = x1 - x0;
            boxHeight = y1 - y0;
            rect = string.Create(CultureInfo.InvariantCulture, $"[{x0:0.####} {y0:0.####} {x1:0.####} {y1:0.####}]");
        }

        // A hostile /Size or object number near the limit would wrap the numbers given to the new objects.
        long highest = Math.Max(source.HighestObjectNumber, source.IntOf(source.Trailer.GetRaw("Size")) - 1L);
        if (highest > int.MaxValue - 16)
            throw new InvalidDataException("The PDF's object numbers are too large to add objects to.");
        int next = (int)highest + 1;
        int valueNumber = next++, fieldNumber = next++;
        var objects = new SortedDictionary<int, string>();

        // The form: made if the file has none, otherwise written again with the field listed and the signature flags set.
        var rootOverrides = new Dictionary<string, string>();
        var fieldRef = $"{fieldNumber} 0 R";
        string fieldName;
        if (root.GetRaw("AcroForm") is not { } formItem)
        {
            fieldName = fieldNamePrefix + "1";
            int newFormNumber = next++;
            objects[newFormNumber] = $"<< /Fields [{fieldRef}] /SigFlags 3 >>";
            rootOverrides["/AcroForm"] = $"{newFormNumber} 0 R";
        }
        else
        {
            var form = source.Resolve(formItem) as PdfDictionary ?? throw new InvalidDataException("The PDF's form dictionary is unreadable.");
            var fields = source.Resolve(form.GetRaw("Fields")) as PdfArray;
            fieldName = FreeName(source, fields, fieldNamePrefix);

            var formOverrides = new Dictionary<string, string> { ["/SigFlags"] = (source.IntOf(form.GetRaw("SigFlags")) | 3).ToString(CultureInfo.InvariantCulture) };
            // A dictionary or array that is direct belongs to the indirect object that holds it, and is encrypted under that object's key.
            var formOwner = formItem is PdfReference formOwnerReference ? formOwnerReference.ObjectNumber : rootItem.ObjectNumber;
            if (form.GetRaw("Fields") is PdfReference fieldsReference && fields is not null)
                objects[fieldsReference.ObjectNumber] = Serialize(fields, NoOverrides, extraElement: fieldRef, encrypt: Encrypt(fieldsReference.ObjectNumber));
            else
                formOverrides["/Fields"] = fields is null ? $"[{fieldRef}]" : Serialize(fields, NoOverrides, extraElement: fieldRef, encrypt: Encrypt(formOwner));

            var formText = Serialize(form, formOverrides, encrypt: Encrypt(formOwner));
            if (formItem is PdfReference formReference)
                objects[formReference.ObjectNumber] = formText;
            else
                rootOverrides["/AcroForm"] = formText;
        }

        objects[rootItem.ObjectNumber] = Serialize(root, rootOverrides, encrypt: Encrypt(rootItem.ObjectNumber));

        // The first page: the field's widget is one of its annotations.
        var pageOverrides = new Dictionary<string, string>();
        var annotations = source.Resolve(page.GetRaw("Annots")) as PdfArray;
        if (page.GetRaw("Annots") is PdfReference annotationsReference && annotations is not null)
            objects[annotationsReference.ObjectNumber] = Serialize(annotations, NoOverrides, extraElement: fieldRef, encrypt: Encrypt(annotationsReference.ObjectNumber));
        else
            pageOverrides["/Annots"] = annotations is null ? $"[{fieldRef}]" : Serialize(annotations, NoOverrides, extraElement: fieldRef, encrypt: Encrypt(pageReference.ObjectNumber));
        objects[pageReference.ObjectNumber] = Serialize(page, pageOverrides, encrypt: Encrypt(pageReference.ObjectNumber));

        string appearanceEntry = "";
        if (visible is not null)
        {
            int fontNumber = next++, formNumber = next++;
            // The stream is stored under its own object's key in an encrypted file; the bytes are then binary, which the file is written as Latin-1 for.
            var plain = Encoding.Latin1.GetBytes(PdfSignatureBoxContent.Content(boxWidth, boxHeight, visible.Lines, visible.FontSize));
            var content = decryptor is { EncryptsStreams: true } ? decryptor.EncryptStream(plain, formNumber, 0) : plain;
            objects[fontNumber] = PdfSignatureBoxContent.FontObjectText;
            objects[formNumber] =
                string.Create(CultureInfo.InvariantCulture, $"<< /Type /XObject /Subtype /Form /BBox [0 0 {boxWidth:0.####} {boxHeight:0.####}] ") +
                $"/Resources << /Font << /Helv {fontNumber} 0 R >> >> /Length {content.Length} >>\nstream\n{Encoding.Latin1.GetString(content)}\nendstream";
            appearanceEntry = $" /AP << /N {formNumber} 0 R >>";
        }

        objects[valueNumber] = valueDictionary(Encrypt(valueNumber));
        var nameBytes = Encoding.Latin1.GetBytes(fieldName);
        objects[fieldNumber] =
            $"<< /Type /Annot /Subtype /Widget /FT /Sig /T <{Convert.ToHexString(Encrypt(fieldNumber)?.Invoke(nameBytes) ?? nameBytes)}> /F 132 /Rect {rect}{appearanceEntry} " +
            $"/P {pageReference.ObjectNumber} {pageReference.GenerationNumber} R /V {valueNumber} 0 R >>";

        if (source.WasRebuilt)
            throw new InvalidDataException("The PDF's cross-reference data is damaged, so it cannot be extended; repair the file first.");

        return Append(source, objects, next);
    }

    /// <summary>
    /// The file with validation data appended to its document security store (<c>/DSS</c>, ISO 32000-2 12.8.4.3): one stream per
    /// certificate, OCSP response and CRL that the store does not already hold, and the catalog (and the store) pointing at them. A file
    /// older than PDF 2.0 also gets the extension entry that says it uses the store.
    /// </summary>
    /// <exception cref="PdfPasswordException">the file needs a password that was not given or is wrong</exception>
    /// <exception cref="InvalidDataException">the file's cross-reference data is damaged</exception>
    public static byte[] AppendValidationData(byte[] original, PdfValidationData data, string? password)
    {
        var source = PdfSourceDocument.Open(original, password);
        var decryptor = source.Decryptor;
        Func<byte[], byte[]>? EncryptStrings(int number) => decryptor is { EncryptsStrings: true }
            ? bytes => decryptor.EncryptString(bytes, number, source.GenerationOf(number))
            : null;

        var rootItem = source.Trailer.GetRaw("Root") as PdfReference ?? throw new InvalidDataException("The PDF has no document catalog.");
        var root = source.Resolve(rootItem) as PdfDictionary ?? throw new InvalidDataException("The PDF's document catalog is unreadable.");

        long highest = Math.Max(source.HighestObjectNumber, source.IntOf(source.Trailer.GetRaw("Size")) - 1L);
        if (highest > int.MaxValue - 16)
            throw new InvalidDataException("The PDF's object numbers are too large to add objects to.");
        int next = (int)highest + 1;
        var objects = new SortedDictionary<int, string>();

        // What the store already holds, so that data added twice is stored once.
        var dssBefore = source.DictOf(root, "DSS");
        var overrides = new Dictionary<string, string>();
        var changed = false;
        foreach (var (key, blobs) in new[] { ("/Certs", data.Certificates), ("/OCSPs", data.OcspResponses), ("/CRLs", data.Crls) })
        {
            var references = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            if (dssBefore is not null && source.ArrayOf(dssBefore, key) is { } existing)
            {
                foreach (var element in existing.Elements)
                {
                    references.Add(Serialize(element, NoOverrides));
                    if (source.Resolve(element) is PdfDictionary { Stream: not null } stream)
                        seen.Add(Convert.ToHexString(SHA256.HashData(source.DecodeStream(stream).Data)));
                }
            }

            foreach (var blob in blobs)
            {
                if (!seen.Add(Convert.ToHexString(SHA256.HashData(blob)))) continue;

                // Each entry is a stream; in an encrypted file it is stored under its own object's key.
                int number = next++;
                var stored = decryptor is { EncryptsStreams: true } ? decryptor.EncryptStream(blob, number, 0) : blob;
                objects[number] = $"<< /Length {stored.Length} >>\nstream\n{Encoding.Latin1.GetString(stored)}\nendstream";
                references.Add($"{number} 0 R");
                changed = true;
            }

            if (references.Count > 0)
                overrides[key] = "[" + string.Join(' ', references) + "]";
        }

        if (!changed)
            return (byte[])original.Clone();

        var rootOverrides = new Dictionary<string, string>();
        if (dssBefore is not null && root.GetRaw("DSS") is PdfReference dssReference)
        {
            objects[dssReference.ObjectNumber] = Serialize(dssBefore, overrides, encrypt: EncryptStrings(dssReference.ObjectNumber));
        }
        else if (dssBefore is not null)
        {
            rootOverrides["/DSS"] = Serialize(dssBefore, overrides, encrypt: EncryptStrings(rootItem.ObjectNumber));
        }
        else
        {
            int dssNumber = next++;
            objects[dssNumber] = "<< " + string.Join(' ', overrides.Select(o => $"{o.Key} {o.Value}")) + " >>";
            rootOverrides["/DSS"] = $"{dssNumber} 0 R";
        }

        // Before PDF 2.0 a file that uses the store says so with an extension entry (ESIC, level 1 over PDF 1.7).
        if (HeaderMajorVersion(original) < 2 && !(source.DictOf(root, "Extensions") is { } extensions && extensions.ContainsKeyRaw("ESIC")))
        {
            const string esic = "<< /BaseVersion /1.7 /ExtensionLevel 1 >>";
            var extensionOverrides = new Dictionary<string, string> { ["/ESIC"] = esic };
            if (source.DictOf(root, "Extensions") is { } present && root.GetRaw("Extensions") is PdfReference extensionsReference)
                objects[extensionsReference.ObjectNumber] = Serialize(present, extensionOverrides, encrypt: EncryptStrings(extensionsReference.ObjectNumber));
            else if (source.DictOf(root, "Extensions") is { } direct)
                rootOverrides["/Extensions"] = Serialize(direct, extensionOverrides, encrypt: EncryptStrings(rootItem.ObjectNumber));
            else
                rootOverrides["/Extensions"] = $"<< /ESIC {esic} >>";
        }

        objects[rootItem.ObjectNumber] = Serialize(root, rootOverrides, encrypt: EncryptStrings(rootItem.ObjectNumber));

        if (source.WasRebuilt)
            throw new InvalidDataException("The PDF's cross-reference data is damaged, so it cannot be extended; repair the file first.");

        return Append(source, objects, next);
    }

    /// <summary>The major version in the file's <c>%PDF-x.y</c> header (1 when it cannot be read).</summary>
    static int HeaderMajorVersion(byte[] data)
    {
        var head = Encoding.Latin1.GetString(data, 0, Math.Min(data.Length, 1024));
        var at = head.IndexOf("%PDF-", StringComparison.Ordinal);
        return at >= 0 && at + 5 < head.Length && char.IsAsciiDigit(head[at + 5]) ? head[at + 5] - '0' : 1;
    }

    /// <summary>A signature drawn as a box: the page it goes on, where (points from the top-left of the page as shown), and what it says.</summary>
    internal sealed record VisibleSignature(int PageNumber, Rect Bounds, string[] Lines, double FontSize);

    /// <summary>The first "prefixN" that no field of the form already has.</summary>
    static string FreeName(PdfSourceDocument source, PdfArray? fields, string prefix)
    {
        var taken = new HashSet<string>(StringComparer.Ordinal);
        if (fields is not null)
        {
            foreach (var item in fields.Elements)
            {
                if (source.Resolve(item) is PdfDictionary field && source.Resolve(field.GetRaw("T")) is PdfString name)
                    taken.Add(PdfSourceNavigation.TextOf(name.Value));
            }
        }

        for (int n = 1; ; n++)
        {
            if (!taken.Contains(prefix + n.ToString(CultureInfo.InvariantCulture)))
                return prefix + n.ToString(CultureInfo.InvariantCulture);
        }
    }

    static byte[] Append(PdfSourceDocument source, SortedDictionary<int, string> objects, int next)
    {
        var original = source.Data;
        using var output = new MemoryStream(original.Length + 4096);
        output.Write(original);
        if (original.Length > 0 && original[^1] is not (byte)'\n' and not (byte)'\r')
            output.WriteByte((byte)'\n');

        var positions = new SortedDictionary<int, long>();
        var generations = new Dictionary<int, int>();
        foreach (var (number, body) in objects)
        {
            positions[number] = output.Position;
            generations[number] = source.GenerationOf(number);
            WriteAscii(output, $"{number} {generations[number]} obj\n{body}\nendobj\n");
        }

        var trailer = new StringBuilder();
        var rootItem = (PdfReference)source.Trailer.GetRaw("Root")!;
        trailer.Append(" /Root ").Append(rootItem.ObjectNumber).Append(' ').Append(rootItem.GenerationNumber).Append(" R");
        if (source.Trailer.GetRaw("Info") is PdfReference info)
            trailer.Append(" /Info ").Append(info.ObjectNumber).Append(' ').Append(info.GenerationNumber).Append(" R");
        if (source.Trailer.GetRaw("ID") is { } id)
            trailer.Append(" /ID ").Append(Serialize(id, NoOverrides));

        // Every trailer of an encrypted file names its encryption dictionary, the update's too (its strings are stored as they are).
        if (source.Trailer.GetRaw("Encrypt") is { } encrypt)
            trailer.Append(" /Encrypt ").Append(Serialize(encrypt, NoOverrides));
        trailer.Append(" /Prev ").Append(source.LastStartXref.ToString(CultureInfo.InvariantCulture));

        long xrefPosition = output.Position;
        if (LastSectionIsStream(source))
        {
            // The file already uses cross-reference streams, so the update does too: one uncompressed row (type, offset, generation) per object.
            int streamNumber = next++;
            positions[streamNumber] = xrefPosition;
            generations[streamNumber] = 0;
            var rows = new MemoryStream();
            foreach (var (number, position) in positions)
            {
                rows.WriteByte(1);
                rows.Write([(byte)(position >> 24), (byte)(position >> 16), (byte)(position >> 8), (byte)position]);
                rows.Write([(byte)(generations[number] >> 8), (byte)generations[number]]);
            }

            WriteAscii(output, $"{streamNumber} 0 obj\n<< /Type /XRef /Size {next} /W [1 4 2] /Index [{string.Join(' ', Subsections(positions).Select(s => $"{s.First} {s.Count}"))}]{trailer} /Length {rows.Length} >>\nstream\n");
            output.Write(rows.ToArray());
            WriteAscii(output, "\nendstream\nendobj\n");
        }
        else
        {
            var table = new StringBuilder("xref\n");
            foreach (var (first, count) in Subsections(positions))
            {
                table.Append(first).Append(' ').Append(count).Append('\n');
                for (int number = first; number < first + count; number++)
                    table.Append(positions[number].ToString("D10", CultureInfo.InvariantCulture)).Append(' ').Append(generations[number].ToString("D5", CultureInfo.InvariantCulture)).Append(" n \n");
            }

            table.Append("trailer\n<< /Size ").Append(next).Append(trailer).Append(" >>\n");
            WriteAscii(output, table.ToString());
        }

        WriteAscii(output, $"startxref\n{xrefPosition}\n%%EOF\n");
        return output.ToArray();
    }

    /// <summary>Whether the file's newest cross-reference section is a stream (an object) rather than a classic table.</summary>
    static bool LastSectionIsStream(PdfSourceDocument source)
    {
        var data = source.Data;
        int at = (int)source.LastStartXref;
        while (at < data.Length && data[at] is (byte)' ' or (byte)'\r' or (byte)'\n' or (byte)'\t' or 0)
            at++;
        return !(at + 4 <= data.Length && data.AsSpan(at, 4).SequenceEqual("xref"u8));
    }

    /// <summary>The runs of consecutive object numbers, as (first number, count).</summary>
    static List<(int First, int Count)> Subsections(SortedDictionary<int, long> positions)
    {
        var runs = new List<(int, int)>();
        foreach (var number in positions.Keys)
        {
            if (runs.Count > 0 && runs[^1].Item1 + runs[^1].Item2 == number)
                runs[^1] = (runs[^1].Item1, runs[^1].Item2 + 1);
            else
                runs.Add((number, 1));
        }

        return runs;
    }

    /// <summary>Writes text one byte per character: ASCII syntax, plus the binary bytes of an encrypted stream, which are held as Latin-1.</summary>
    static void WriteAscii(Stream stream, string text) => stream.Write(Encoding.Latin1.GetBytes(text));

    // ---------------------------------------------------------------- writing objects the reader parsed

    /// <summary>
    /// A dictionary or array as PDF syntax. <paramref name="overrides"/> replaces (or adds) the text of a dictionary entry;
    /// <paramref name="extraElement"/> is one more element at the end of an array.
    /// </summary>
    static string Serialize(PdfItem item, IReadOnlyDictionary<string, string> overrides, string? extraElement = null, Func<byte[], byte[]>? encrypt = null)
    {
        var text = new StringBuilder();
        Write(text, item, overrides, extraElement, encrypt);
        return text.ToString();
    }

    static void Write(StringBuilder text, PdfItem? item, IReadOnlyDictionary<string, string> overrides, string? extraElement, Func<byte[], byte[]>? encrypt)
    {
        switch (item)
        {
            case null or PdfNull:
                text.Append("null");
                break;
            case PdfBoolean boolean:
                text.Append(boolean.Value ? "true" : "false");
                break;
            case PdfInteger integer:
                text.Append(integer.Value.ToString(CultureInfo.InvariantCulture));
                break;
            case PdfReal real:
                text.Append(real.Value.ToString("0.#######", CultureInfo.InvariantCulture));
                break;
            case PdfName name:
                WriteName(text, name.Value);
                break;
            case PdfString @string:
                var bytes = Encoding.Latin1.GetBytes(@string.Value);
                text.Append('<').Append(Convert.ToHexString(encrypt?.Invoke(bytes) ?? bytes)).Append('>');
                break;
            case PdfReference reference:
                text.Append(reference.ObjectNumber).Append(' ').Append(reference.GenerationNumber).Append(" R");
                break;
            case PdfArray array:
                text.Append('[');
                bool first = true;
                foreach (var element in array.Elements)
                {
                    if (!first) text.Append(' ');
                    first = false;
                    Write(text, element, NoOverrides, null, encrypt);
                }

                if (extraElement is not null)
                    text.Append(first ? "" : " ").Append(extraElement);
                text.Append(']');
                break;
            case PdfDictionary dictionary:
                if (dictionary.Stream is not null)
                    throw new NotSupportedException("A stream cannot be written again by an incremental update.");
                text.Append("<<");
                foreach (var (key, value) in dictionary)
                {
                    if (overrides.ContainsKey(key)) continue;
                    text.Append(' ');
                    WriteName(text, key);
                    text.Append(' ');
                    Write(text, value, NoOverrides, null, encrypt);
                }

                foreach (var (key, replacement) in overrides)
                {
                    text.Append(' ');
                    WriteName(text, key);
                    text.Append(' ').Append(replacement);
                }

                text.Append(" >>");
                break;
            default:
                throw new NotSupportedException($"A {item.GetType().Name} cannot be written again by an incremental update.");
        }
    }

    static void WriteName(StringBuilder text, string name)
    {
        text.Append('/');
        foreach (var ch in name.TrimStart('/'))
        {
            if (ch > ' ' && ch < 127 && ch is not ('%' or '/' or '<' or '>' or '(' or ')' or '[' or ']' or '{' or '}' or '#'))
                text.Append(ch);
            else
                text.Append('#').Append(((int)ch & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
        }
    }
}
