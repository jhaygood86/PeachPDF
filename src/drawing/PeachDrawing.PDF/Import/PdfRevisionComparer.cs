using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Works out what changed in a document after a signature by reading the file twice: as it was when the signature covered it (the file's
/// bytes up to the end of the signed range, which is a whole file ending at its own <c>%%EOF</c>) and as it is now, and comparing the
/// objects. It is conservative: only the changes a signature, a timestamp, validation data, a filled form or an added annotation make are
/// recognized as such, and any other object that differs makes the answer <see cref="PdfSignatureChanges.Other"/>.
/// </summary>
internal static class PdfRevisionComparer
{
    static readonly HashSet<string> CatalogKeys = new(StringComparer.Ordinal) { "/AcroForm", "/DSS", "/Extensions" };
    static readonly HashSet<string> FormKeys = new(StringComparer.Ordinal) { "/Fields", "/SigFlags" };
    static readonly HashSet<string> PageKeys = new(StringComparer.Ordinal) { "/Annots" };
    static readonly HashSet<string> FilledKeys = new(StringComparer.Ordinal) { "/V", "/AP", "/AS" };

    /// <param name="now">the document as it is</param>
    /// <param name="signedEnd">where the signed range ends: the length of the file as the signer saw it</param>
    public static PdfSignatureChanges Compare(PdfSourceDocument now, long signedEnd)
    {
        if (signedEnd <= 0 || signedEnd > now.Data.Length)
            return PdfSignatureChanges.Unknown;

        PdfSourceDocument before;
        try
        {
            before = PdfSourceDocument.Open(now.Data[..(int)signedEnd], now.Password);
        }
        catch (Exception error) when (error is InvalidDataException or PdfPasswordException or NotSupportedException or ArgumentException or IndexOutOfRangeException)
        {
            return PdfSignatureChanges.Unknown;
        }

        var level = PdfSignatureChanges.None;
        var permitted = new HashSet<int>();
        void Raise(PdfSignatureChanges l) => level = (PdfSignatureChanges)Math.Max((int)level, (int)l);

        try
        {
            if (now.Trailer.GetRaw("Root") is not PdfReference rootReference
                || now.Resolve(rootReference) is not PdfDictionary rootNow
                || before.Resolve(before.Trailer.GetRaw("Root")) is not PdfDictionary rootBefore)
                return PdfSignatureChanges.Unknown;

            // The catalog: only the form, the security store and the extensions may differ, and a difference there is at least a new signature.
            permitted.Add(rootReference.ObjectNumber);
            if (Fingerprint(rootBefore, CatalogKeys) != Fingerprint(rootNow, CatalogKeys)) Raise(PdfSignatureChanges.Other);
            else if (Fingerprint(rootBefore) != Fingerprint(rootNow)) Raise(PdfSignatureChanges.SignaturesAndTimestamps);

            var handled = new HashSet<int>();
            CompareForm(before, now, rootBefore, rootNow, permitted, handled, Raise);
            ComparePages(before, now, permitted, handled, Raise);

            // The security store holds only what was added to it; the objects it reaches are accounted for.
            if (rootNow.GetRaw("DSS") is { } dss)
            {
                foreach (var number in Reach(now, dss))
                {
                    permitted.Add(number);
                    if (Fingerprint(before.ObjectAt(number)) != Fingerprint(now.ObjectAt(number))) Raise(PdfSignatureChanges.SignaturesAndTimestamps);
                }
            }

            // Anything that existed when the signature was made and is not accounted for must be unchanged.
            foreach (var number in before.ObjectNumbers)
            {
                if (permitted.Contains(number)) continue;
                if (Fingerprint(before.ObjectAt(number)) != Fingerprint(now.ObjectAt(number)))
                {
                    Raise(PdfSignatureChanges.Other);
                    break;
                }
            }
        }
        catch (InvalidDataException)
        {
            return PdfSignatureChanges.Unknown;
        }

        // Reading an object can find the file's cross-reference data damaged and rebuild it, which makes the comparison meaningless.
        return before.WasRebuilt || now.WasRebuilt ? PdfSignatureChanges.Unknown : level;
    }

    static void CompareForm(PdfSourceDocument before, PdfSourceDocument now, PdfDictionary rootBefore, PdfDictionary rootNow,
        HashSet<int> permitted, HashSet<int> handled, Action<PdfSignatureChanges> raise)
    {
        var formBefore = before.DictOf(rootBefore, "AcroForm");
        var formNow = now.DictOf(rootNow, "AcroForm");
        if (formNow is null)
        {
            if (formBefore is not null) raise(PdfSignatureChanges.Other);
            return;
        }

        if (rootNow.GetRaw("AcroForm") is PdfReference formReference) permitted.Add(formReference.ObjectNumber);
        if (formBefore is null)
        {
            raise(PdfSignatureChanges.SignaturesAndTimestamps);
        }
        else if (Fingerprint(formBefore, FormKeys) != Fingerprint(formNow, FormKeys))
        {
            raise(PdfSignatureChanges.Other);
        }
        else if (Fingerprint(formBefore) != Fingerprint(formNow))
        {
            raise(PdfSignatureChanges.SignaturesAndTimestamps);
        }

        // The list of fields may only have grown at the end.
        var listBefore = formBefore is null ? null : before.ArrayOf(formBefore, "Fields");
        var listNow = now.ArrayOf(formNow, "Fields");
        if (formNow.GetRaw("Fields") is PdfReference listReference) permitted.Add(listReference.ObjectNumber);
        if (!StartsWith(listNow, listBefore)) raise(PdfSignatureChanges.Other);

        // Each field: new, unchanged, or changed in the way filling it changes it.
        var pending = new Stack<(PdfDictionary Field, string? Type)>();
        if (listNow is not null)
        {
            foreach (var item in listNow.Elements.Reverse())
                if (now.Resolve(item) is PdfDictionary field) pending.Push((field, null));
        }

        for (int budget = 100_000; pending.Count > 0 && budget > 0; budget--)
        {
            var (field, inherited) = pending.Pop();
            var type = now.NameOf(field.GetRaw("FT")) ?? inherited;
            var signature = type == "/Sig";
            var number = field.Reference?.ObjectNumber;
            if (number is { } n)
            {
                handled.Add(n);
                if (before.ObjectAt(n) is not PdfDictionary old)
                {
                    // A field that did not exist: a signature field is what signing adds; any other is a change to the form itself.
                    raise(signature ? PdfSignatureChanges.SignaturesAndTimestamps : PdfSignatureChanges.Other);
                }
                else if (Fingerprint(old) != Fingerprint(field))
                {
                    permitted.Add(n);
                    var changed = ChangedKeys(old, field);
                    if (changed.All(FilledKeys.Contains))
                    {
                        raise(signature ? PdfSignatureChanges.SignaturesAndTimestamps : PdfSignatureChanges.FormFilling);
                        // The appearance a filled field shows may be the same object rewritten.
                        foreach (var reached in Reach(now, field.GetRaw("AP")))
                        {
                            permitted.Add(reached);
                            if (Fingerprint(before.ObjectAt(reached)) != Fingerprint(now.ObjectAt(reached)))
                                raise(signature ? PdfSignatureChanges.SignaturesAndTimestamps : PdfSignatureChanges.FormFilling);
                        }
                    }
                    else
                    {
                        raise(PdfSignatureChanges.Other);
                    }
                }
            }

            if (now.ArrayOf(field, "Kids") is { } kids)
            {
                foreach (var kid in kids.Elements.Reverse())
                    if (now.Resolve(kid) is PdfDictionary child) pending.Push((child, type));
            }
        }
    }

    static void ComparePages(PdfSourceDocument before, PdfSourceDocument now, HashSet<int> permitted, HashSet<int> handled, Action<PdfSignatureChanges> raise)
    {
        foreach (var pageNow in now.Pages)
        {
            if (pageNow.Reference?.ObjectNumber is not { } number || before.ObjectAt(number) is not PdfDictionary pageBefore)
            {
                raise(PdfSignatureChanges.Other);                      // a page that did not exist
                continue;
            }

            if (Fingerprint(pageBefore, PageKeys) != Fingerprint(pageNow, PageKeys))
            {
                raise(PdfSignatureChanges.Other);
                permitted.Add(number);
                continue;
            }

            var annotsBefore = before.ArrayOf(pageBefore, "Annots");
            var annotsNow = now.ArrayOf(pageNow, "Annots");
            if (Fingerprint(annotsBefore) != Fingerprint(annotsNow) || Fingerprint(pageBefore) != Fingerprint(pageNow))
            {
                permitted.Add(number);
                if (pageNow.GetRaw("Annots") is PdfReference listReference) permitted.Add(listReference.ObjectNumber);
            }

            if (!StartsWith(annotsNow, annotsBefore)) raise(PdfSignatureChanges.Annotations);
            var known = annotsBefore?.Elements.Count ?? 0;
            for (int i = 0; annotsNow is not null && i < annotsNow.Elements.Count; i++)
            {
                if (now.Resolve(annotsNow.Elements[i]) is not PdfDictionary annotation) continue;
                var annotationNumber = annotation.Reference?.ObjectNumber;
                if (annotationNumber is { } an && handled.Contains(an)) continue;       // a form field's widget: judged with the fields

                var signature = now.NameOf(annotation.GetRaw("FT")) == "/Sig";
                if (i >= known)
                {
                    raise(signature ? PdfSignatureChanges.SignaturesAndTimestamps : PdfSignatureChanges.Annotations);
                }
                else if (annotationNumber is { } existing && before.ObjectAt(existing) is { } old && Fingerprint(old) != Fingerprint(annotation))
                {
                    permitted.Add(existing);
                    raise(PdfSignatureChanges.Annotations);
                    foreach (var reached in Reach(now, annotation.GetRaw("AP")))
                        permitted.Add(reached);
                }
            }
        }
    }

    /// <summary>Whether <paramref name="after"/> is <paramref name="before"/> with elements added at the end (or the same; a missing list is an empty one).</summary>
    static bool StartsWith(PdfArray? after, PdfArray? before)
    {
        int afterCount = after?.Elements.Count ?? 0, beforeCount = before?.Elements.Count ?? 0;
        if (afterCount < beforeCount) return false;
        for (int i = 0; i < beforeCount; i++)
            if (Fingerprint(after!.Elements[i]) != Fingerprint(before!.Elements[i])) return false;
        return true;
    }

    /// <summary>The keys whose value differs between two dictionaries (present in only one counts as different).</summary>
    static List<string> ChangedKeys(PdfDictionary before, PdfDictionary after) =>
        before.Select(kv => kv.Key).Union(after.Select(kv => kv.Key), StringComparer.Ordinal)
            .Where(key => Fingerprint(before.ContainsKeyRaw(key) ? before.GetRaw(key) : null) != Fingerprint(after.ContainsKeyRaw(key) ? after.GetRaw(key) : null))
            .ToList();

    /// <summary>The numbers of the indirect objects reached from <paramref name="start"/> through dictionaries and arrays (a stream's data is not followed).</summary>
    static IEnumerable<int> Reach(PdfSourceDocument doc, PdfItem? start)
    {
        var found = new HashSet<int>();
        var pending = new Stack<PdfItem>();
        if (start is not null) pending.Push(start);
        for (int budget = 100_000; pending.Count > 0 && budget > 0; budget--)
        {
            var item = pending.Pop();
            if (item is PdfReference reference)
            {
                if (found.Add(reference.ObjectNumber) && doc.ObjectAt(reference.ObjectNumber) is { } target)
                    pending.Push(target);
            }
            else if (item is PdfDictionary dictionary)
            {
                foreach (var kv in dictionary) pending.Push(kv.Value);
            }
            else if (item is PdfArray array)
            {
                foreach (var element in array.Elements) pending.Push(element);
            }
        }

        return found;
    }

    // ---------------------------------------------------------------- fingerprints

    static string Fingerprint(PdfItem? item, ISet<string>? skip = null)
    {
        var text = new StringBuilder();
        Write(text, item, skip, 0);
        return text.ToString();
    }

    /// <summary>A canonical text of an object, equal for equal objects: dictionary keys in order, strings as hexadecimal, a stream by its digest.</summary>
    static void Write(StringBuilder text, PdfItem? item, ISet<string>? skip, int depth)
    {
        if (depth > 64) throw new InvalidDataException("PDF object nesting is too deep.");
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
                text.Append(real.Value.ToString("R", CultureInfo.InvariantCulture));
                break;
            case PdfName name:
                text.Append(name.Value);
                break;
            case PdfString @string:
                text.Append('<').Append(Convert.ToHexString(Encoding.Latin1.GetBytes(@string.Value))).Append('>');
                break;
            case PdfReference reference:
                text.Append(reference.ObjectNumber).Append(' ').Append(reference.GenerationNumber).Append(" R");
                break;
            case PdfArray array:
                text.Append('[');
                foreach (var element in array.Elements)
                {
                    Write(text, element, null, depth + 1);
                    text.Append(' ');
                }

                text.Append(']');
                break;
            case PdfDictionary dictionary:
                text.Append("<<");
                foreach (var kv in dictionary.OrderBy(k => k.Key, StringComparer.Ordinal))
                {
                    if (skip is not null && skip.Contains(kv.Key)) continue;
                    text.Append(kv.Key).Append(' ');
                    Write(text, kv.Value, null, depth + 1);
                    text.Append(' ');
                }

                text.Append(">>");
                if (dictionary.Stream is { } stream)
                    text.Append("stream:").Append(Convert.ToHexString(SHA256.HashData(stream.Value)));
                break;
            default:
                text.Append('?').Append(item.GetType().Name);
                break;
        }
    }
}
