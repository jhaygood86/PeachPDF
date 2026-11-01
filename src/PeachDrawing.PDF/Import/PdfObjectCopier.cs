using System;
using System.Collections.Generic;
using System.Text;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.Advanced;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Copies the objects of the pages of an opened PDF into a document being written, as they are: the page's content streams stay
/// the encoded bytes the file held, and its fonts, images, shadings and form XObjects are carried across whole, so nothing has to be
/// drawn again. Everything the pages reach is renumbered into the new document, and an object that several pages share is copied once.
/// </summary>
/// <remarks>
/// <para>
/// Only what belongs to the pages is copied: their content, resources, boxes, rotation, annotations and the form fields those
/// annotations are widgets of. The document-level navigation (outline, named destinations) and information are carried by the
/// caller from <see cref="PdfSourceNavigation"/>, the same as for pages that are drawn again. A link or a go-to action that leads to
/// a page of the file that was not copied is removed, and the structure tree, optional content, and JavaScript are not carried.
/// </para>
/// <para>
/// A copier serves one source file and one destination document. Pages are copied in two steps so a link on an early page can
/// lead to a later one: <see cref="RegisterPage"/> for every page the file contributes, then <see cref="FillPage"/> for each.
/// </para>
/// </remarks>
sealed class PdfObjectCopier
{
    // The page entries that are carried; everything else a page dictionary holds is dropped (the parent link, the structure tree
    // parent key, thumbnails, additional actions, page-piece data and the like).
    static readonly string[] PageKeys =
    [
        "/MediaBox", "/CropBox", "/BleedBox", "/TrimBox", "/ArtBox", "/BoxColorInfo", "/Rotate", "/Resources", "/Group", "/UserUnit",
        "/Trans", "/Dur", "/Tabs", "/PZ", "/Metadata",
    ];

    static readonly string[] InheritedKeys = ["/MediaBox", "/CropBox", "/Rotate", "/Resources"];

    readonly PdfSourceDocument _source;
    readonly PdfDocument _destination;

    // Source objects already copied, by identity, so a shared font is one object in the output.
    readonly Dictionary<PdfItem, PdfReference> _copied = new(ReferenceEqualityComparer.Instance);

    // Pages of the source that the output contains: the source page dictionary to the page that stands for it (the first, if copied twice).
    readonly Dictionary<PdfItem, PdfPage> _pages = new(ReferenceEqualityComparer.Instance);

    // The field names already used by the destination's form, so merged files whose fields share a name do not become one field.
    readonly HashSet<string> _fieldNames;
    readonly HashSet<PdfItem> _fieldRoots = new(ReferenceEqualityComparer.Instance);
    readonly List<PdfDictionary> _newFieldRoots = [];
    readonly HashSet<PdfItem> _widgetPages = new(ReferenceEqualityComparer.Instance);

    public PdfObjectCopier(PdfSourceDocument source, PdfDocument destination, HashSet<string> fieldNames)
    {
        _source = source;
        _destination = destination;
        _fieldNames = fieldNames;
    }

    /// <summary>Says that <paramref name="page"/> of the output stands for <paramref name="sourcePage"/>, for links that lead to it.</summary>
    public void RegisterPage(PdfDictionary sourcePage, PdfPage page) => _pages.TryAdd(sourcePage, page);

    /// <summary>Fills a page of the output with the content of <paramref name="sourcePage"/>.</summary>
    /// <returns>the content streams of the page, in order, as references into the output</returns>
    public List<PdfItem> FillPage(PdfPage page, PdfDictionary sourcePage)
    {
        foreach (var key in PageKeys)
        {
            var value = Array.IndexOf(InheritedKeys, key) >= 0 ? _source.InheritedAttribute(sourcePage, key) : _source.Resolve(sourcePage.GetRaw(key));
            if (value is null or PdfNull)
                continue;

            page.Elements[key] = Clone(value);
        }

        // The page's resources become a dictionary of its own (the fonts, images and the rest stay shared objects): what is drawn on the page
        // afterwards adds to them, and a dictionary several pages share must not grow entries only one of them uses.
        if (page.Elements["/Resources"] is { } resources)
        {
            var own = new PdfDictionary(_destination);
            if ((resources is PdfReference reference ? reference.Value : resources) is PdfDictionary shared)
            {
                foreach (var category in shared)
                {
                    var value = category.Value is PdfReference categoryReference ? categoryReference.Value : category.Value;
                    if (value is PdfDictionary entries)
                    {
                        var copy = new PdfDictionary(_destination);
                        foreach (var entry in entries)
                            copy.Elements[entry.Key] = entry.Value;
                        own.Elements[category.Key] = copy;
                    }
                    else
                    {
                        own.Elements[category.Key] = category.Value;
                    }
                }
            }

            page.Elements["/Resources"] = own;
        }

        page.ResetResources();

        if (_source.ArrayOf(sourcePage, "Annots") is { } annots)
        {
            var copied = new PdfArray(_destination);
            foreach (var entry in annots.Elements)
            {
                if (_source.Resolve(entry) is not PdfDictionary annotation || CloneAnnotation(annotation, page) is not { } clone)
                    continue;

                copied.Elements.Add(clone);
            }

            if (copied.Elements.Count > 0)
                page.Elements["/Annots"] = copied;
        }

        var streams = new List<PdfItem>();
        var contents = _source.Resolve(sourcePage.GetRaw("Contents"));
        if (contents is PdfArray array)
        {
            foreach (var entry in array.Elements)
            {
                if (_source.Resolve(entry) is PdfDictionary { Stream: not null } stream)
                    streams.Add(CloneIndirect(stream));
            }
        }
        else if (contents is PdfDictionary { Stream: not null } single)
        {
            streams.Add(CloneIndirect(single));
        }

        return streams;
    }

    /// <summary>
    /// Adds the form fields the copied widgets belong to to the destination's form, with the form's own defaults taken from the file's.
    /// Call once every page has been filled.
    /// </summary>
    public void FinishForm()
    {
        if (_newFieldRoots.Count == 0)
            return;

        var form = _destination.Catalog.AcroForm;
        foreach (var root in _newFieldRoots)
            form.Fields.Elements.Add(root.RequiredReference);

        if (_source.DictOf(_source.DictOf(_source.Trailer, "Root") ?? _source.NewDictionary(), "AcroForm") is not { } sourceForm)
            return;

        if (!form.Elements.ContainsKey("/DA") && _source.Resolve(sourceForm.GetRaw("DA")) is PdfString da)
            form.Elements["/DA"] = da;
        if (!form.Elements.ContainsKey("/Q") && _source.Resolve(sourceForm.GetRaw("Q")) is PdfInteger q)
            form.Elements["/Q"] = q;
        if (_source.Resolve(sourceForm.GetRaw("NeedAppearances")) is PdfBoolean { Value: true })
            form.Elements.SetBoolean("/NeedAppearances", true);

        // The default resources are shared by the fields' appearances: a font the file names there is added unless the form has one of the name.
        if (_source.DictOf(sourceForm, "DR") is { } resources)
        {
            var target = form.DefaultResources;
            foreach (var category in resources)
            {
                if (_source.Resolve(category.Value) is not PdfDictionary entries)
                    continue;

                if (target.Elements[category.Key] is not PdfDictionary merged)
                {
                    target.Elements[category.Key] = Clone(entries);
                    continue;
                }

                foreach (var entry in entries)
                {
                    if (!merged.Elements.ContainsKey(entry.Key))
                        merged.Elements[entry.Key] = Clone(entry.Value);
                }
            }
        }
    }

    // ---------------------------------------------------------------------------------------------------- annotations

    PdfItem? CloneAnnotation(PdfDictionary annotation, PdfPage page)
    {
        // A pop-up belongs to its parent annotation's window and is made again by a viewer; copying it alone would leave it unattached.
        if (_source.NameOf(annotation.GetRaw("Subtype")) == "/Popup")
            return null;

        // Each copy of a page gets annotations of its own (a page taken twice has two sets of links), but a widget is part of its field, which
        // exists once, so a page taken again has no widgets of its own.
        var isWidget = _source.NameOf(annotation.GetRaw("Subtype")) == "/Widget";
        if (isWidget && !_widgetPages.Add(annotation))
            return null;

        var shell = new PdfDictionary(_destination);
        _destination.Internals.AddObject(shell);

        // The field the widget belongs to lists it among its kids, and that must be this very object.
        if (isWidget)
            _copied[annotation] = shell.RequiredReference;

        var clone = CloneDictionary(annotation, shell);
        clone.Elements["/P"] = page.RequiredReference;
        clone.Elements.Remove("/StructParent");

        // Where it leads: a destination or go-to action naming a page that was not copied has nowhere to go, so it is taken off.
        if (clone.Elements["/Dest"] is PdfArray { Elements.Count: > 0 } dest && IsDangling(dest))
            clone.Elements.Remove("/Dest");
        if (clone.Elements["/A"] is PdfDictionary action)
        {
            if (action.Elements["/D"] is PdfArray { Elements.Count: > 0 } to && IsDangling(to))
                clone.Elements.Remove("/A");
            else if (action.Elements.ContainsKey("/JS") || action.Elements["/S"] is PdfName { Value: "/JavaScript" or "/Launch" or "/ImportData" or "/SubmitForm" })
                clone.Elements.Remove("/A");
        }

        // A widget is a field, or the leaf of one: its form root goes in the form.
        if (isWidget)
            RegisterFieldRoot(annotation, clone);

        return clone.RequiredReference;
    }

    static bool IsDangling(PdfArray destination) => destination.Elements[0] is PdfNull;

    void RegisterFieldRoot(PdfDictionary sourceWidget, PdfDictionary widget)
    {
        var sourceRoot = sourceWidget;
        for (var depth = 0; depth < 32 && _source.Resolve(sourceRoot.GetRaw("Parent")) is PdfDictionary parent; depth++)
            sourceRoot = parent;

        if (!_fieldRoots.Add(sourceRoot))
            return;

        var root = (PdfDictionary)ResolveClone(CloneIndirect(sourceRoot));

        // Two files each with a field "name" must not be one field: the later one is renamed.
        if (root.Elements["/T"] is PdfString { Value: var name })
        {
            var unique = name;
            for (var n = 2; !_fieldNames.Add(unique); n++)
                unique = name + "_" + n;
            if (unique != name)
                root.Elements.SetString("/T", unique, PdfStringEncoding.Unicode);
        }

        _newFieldRoots.Add(root);
    }

    // ---------------------------------------------------------------------------------------------------- copying

    PdfItem ResolveClone(PdfItem reference) => reference is PdfReference r ? r.Value : reference;

    /// <summary>Copies <paramref name="item"/>, which may be a value, a direct object or an indirect reference, into the destination.</summary>
    PdfItem Clone(PdfItem item)
    {
        if (item is PdfReference reference)
        {
            var target = _source.Resolve(reference);
            return target is null or PdfNull ? PdfNull.Instance : Clone(target);
        }

        switch (item)
        {
            case PdfDictionary dictionary:
                if (IsSourcePage(dictionary, out var page))
                    return page is null ? PdfNull.Instance : page.RequiredReference;

                // An indirect object keeps its identity: one copy, however many places name it.
                if (dictionary.Reference is not null || dictionary.Stream is not null)
                    return CloneIndirect(dictionary);

                return CloneDictionary(dictionary, null);
            case PdfArray array:
                if (array.Reference is not null)
                    return CloneIndirect(array);

                return CloneArray(array, null);
            default:
                // Numbers, names, strings, booleans and null are immutable values.
                return item;
        }
    }

    bool IsSourcePage(PdfDictionary dictionary, out PdfPage? page)
    {
        page = null;
        if (_pages.TryGetValue(dictionary, out page))
            return true;

        // A dictionary the page tree names as a page but that is not in the output: its place is a null, so the link to it is dropped.
        return _source.NameOf(dictionary.GetRaw("Type")) == "/Page";
    }

    PdfReference CloneIndirect(PdfItem source) => CloneIndirectCore(source);

    PdfReference CloneIndirectCore(PdfItem sourceObject)
    {
        if (_copied.TryGetValue(sourceObject, out var existing))
            return existing;

        PdfObject shell = sourceObject switch
        {
            PdfDictionary => new PdfDictionary(_destination),
            _ => new PdfArray(_destination),
        };
        _destination.Internals.AddObject(shell);
        var reference = shell.RequiredReference;

        // Registered before its contents are copied, so an object that leads back to itself (a field and its parent) terminates.
        _copied[sourceObject] = reference;

        if (sourceObject is PdfDictionary dictionary)
            CloneDictionary(dictionary, (PdfDictionary)shell);
        else
            CloneArray((PdfArray)sourceObject, (PdfArray)shell);

        return reference;
    }

    PdfDictionary CloneDictionary(PdfDictionary source, PdfDictionary? into)
    {
        var clone = into ?? new PdfDictionary(_destination);
        foreach (var entry in source)
        {
            // Length is set from the bytes below; the page and structure back-references would drag in the rest of the file.
            if (entry.Key == "/Length" && source.Stream is not null)
                continue;

            clone.Elements[entry.Key] = Clone(entry.Value);
        }

        if (source.Stream is not null)
        {
            clone.CreateStream(source.Stream.Value);
        }

        return clone;
    }

    PdfArray CloneArray(PdfArray source, PdfArray? into)
    {
        var clone = into ?? new PdfArray(_destination);
        foreach (var element in source.Elements)
            clone.Elements.Add(Clone(element));
        return clone;
    }
}
