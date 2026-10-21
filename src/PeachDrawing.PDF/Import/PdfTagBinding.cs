using System;
using System.Collections.Generic;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Carries the logical structure of an imported page into a document being tagged. Before the page is drawn the structure elements that
/// have content on it (and the ones above them) are declared to the recorder in the order of the source's structure tree, with their language,
/// actual text, title and attributes; while it is drawn, a marked-content sequence (<c>/P &lt;&lt;/MCID 3&gt;&gt; BDC</c>) finds its element through the
/// page's entry in the parent tree and is recorded as that element's content.
/// </summary>
internal sealed class PdfTagBinding
{
    /// <summary>Identifies a source structure element within one drawing of a page, so a page drawn twice gets elements of its own.</summary>
    sealed record StructKey(object Stamp, PdfDictionary Element);

    /// <summary>What the recorder is told about one marked-content sequence.</summary>
    internal sealed record Binding(object Key, string Type, string? AltText, IReadOnlyList<(object Key, string Type)> Ancestors);

    static readonly HashSet<string> StandardTypes = new(StringComparer.Ordinal)
    {
        "Document", "DocumentFragment", "Part", "Art", "Sect", "Div", "Aside", "BlockQuote", "Caption", "TOC", "TOCI", "Index", "NonStruct", "Private",
        "Title", "H", "H1", "H2", "H3", "H4", "H5", "H6", "P", "L", "LI", "Lbl", "LBody", "Table", "TR", "TH", "TD", "THead", "TBody", "TFoot",
        "Span", "Quote", "Note", "FENote", "Reference", "BibEntry", "Code", "Link", "Annot", "Ruby", "RB", "RT", "RP", "Warichu", "WT", "WP",
        "Figure", "Formula", "Form", "Em", "Strong", "Sub",
    };

    readonly PdfSourceDocument _doc;
    readonly object _stamp = new();
    Dictionary<int, PdfItem>? _parentTree;

    public PdfTagBinding(PdfSourceDocument doc, PdfTagRecorder recorder)
    {
        _doc = doc;
        Recorder = recorder;
    }

    /// <summary>The recorder the structure is written to.</summary>
    public PdfTagRecorder Recorder { get; }

    PdfDictionary? Root => _doc.Resolve(_doc.Trailer.GetRaw("Root")) is PdfDictionary root ? _doc.DictOf(root, "StructTreeRoot") : null;

    PdfDictionary? RoleMap => Root is { } root ? _doc.DictOf(root, "RoleMap") : null;

    /// <summary>The parent tree: the structure elements of the marked content of each page (and of each form with content of its own).</summary>
    Dictionary<int, PdfItem> ParentTree
    {
        get
        {
            if (_parentTree is not null) return _parentTree;
            _parentTree = [];
            if (Root is { } root && _doc.DictOf(root, "ParentTree") is { } tree) Walk(tree, 0);
            return _parentTree;

            void Walk(PdfDictionary node, int depth)
            {
                if (depth > 32) return;
                if (_doc.ArrayOf(node, "Nums") is { } nums)
                    for (int i = 0; i + 1 < nums.Elements.Count; i += 2)
                        _parentTree![_doc.IntOf(nums.Elements[i])] = nums.Elements[i + 1];
                if (_doc.ArrayOf(node, "Kids") is { } kids)
                    foreach (var kid in kids.Elements)
                        if (_doc.Resolve(kid) is PdfDictionary child) Walk(child, depth + 1);
            }
        }
    }

    // ------------------------------------------------------------------ declaring the page's structure

    /// <summary>
    /// Declares the structure elements that have content on <paramref name="page"/> - the owners of its marked content and of its link
    /// annotations, and every element above them - to the recorder, in the order of the source's structure tree.
    /// </summary>
    public void Declare(PdfDictionary page)
    {
        if (Root is not { } root) return;

        var owners = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        if (page.ContainsKeyRaw("StructParents") && ParentTree.TryGetValue(_doc.IntOf(page.GetRaw("StructParents")), out var pageEntry) && _doc.Resolve(pageEntry) is PdfArray contents)
        {
            // Only the content the page actually marks counts: a parent tree can list elements whose sequences the content no longer holds.
            var used = UsedMarkedContentIds(page);
            for (int mcid = 0; mcid < contents.Elements.Count; mcid++)
                if (used.Contains(mcid) && _doc.Resolve(contents.Elements[mcid]) is PdfDictionary owner) owners.Add(owner);
        }
        if (_doc.ArrayOf(page, "Annots") is { } annots)
            foreach (var item in annots.Elements)
                if (_doc.Resolve(item) is PdfDictionary annotation && OwnerOfAnnotation(annotation) is { } linked) owners.Add(linked);
        if (owners.Count == 0) return;

        // The elements to declare: each owner and its ancestors.
        var wanted = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var owner in owners)
            for (var current = owner; current is not null && current.ContainsKeyRaw("S") && wanted.Add(current); current = _doc.Resolve(current.GetRaw("P")) as PdfDictionary)
            {
            }

        var seen = new HashSet<PdfDictionary>(ReferenceEqualityComparer.Instance);
        foreach (var child in StructChildren(root))
            Visit(child, null, null, false, 0, wanted, owners, seen);
    }

    /// <summary>The marked-content ids the page's own content stream uses, written inline or kept in the resources' property lists.</summary>
    HashSet<int> UsedMarkedContentIds(PdfDictionary page)
    {
        var used = new HashSet<int>();
        var text = System.Text.Encoding.Latin1.GetString(_doc.GetPageContent(page));
        foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(text, @"/MCID\s+(\d+)"))
            used.Add(int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture));
        if (_doc.InheritedAttribute(page, "Resources") is PdfDictionary resources && _doc.DictOf(resources, "Properties") is { } properties)
            foreach (var pair in properties.Elements)
                if (_doc.Resolve(pair.Value) is PdfDictionary property && property.ContainsKeyRaw("MCID")) used.Add(_doc.IntOf(property.GetRaw("MCID")));
        return used;
    }

    void Visit(PdfDictionary node, object? parentKey, string? parentType, bool parentBody, int depth, HashSet<PdfDictionary> wanted, HashSet<PdfDictionary> owners, HashSet<PdfDictionary> seen)
    {
        if (depth > 64 || !seen.Add(node) || !wanted.Contains(node)) return;

        var type = TypeOf(node, RoleMap, grouping: !owners.Contains(node));
        var details = DetailsOf(node);
        object? childParentKey = parentKey;
        string? childType = parentType;
        bool childBody = parentBody;

        if (type == "Document")
        {
            // The target's own Document element stays the root: this one's children go where its own would.
        }
        else if (type == "LBody" && parentType == "LI" && parentKey is not null)
        {
            // The body of a list item is made by the recorder together with the item.
            Recorder.DeclareListItemBody(parentKey);
            childBody = true;
            if (owners.Contains(node)) Recorder.DeclareElement(new StructKey(_stamp, node), "Span", parentKey, details, parentIsListItemBody: true);
        }
        else
        {
            var key = new StructKey(_stamp, node);
            Recorder.DeclareElement(key, type, parentKey, details, parentBody);
            childParentKey = key;
            childType = type;
            childBody = false;
        }

        foreach (var child in StructChildren(node))
            Visit(child, childParentKey, childType, childBody, depth + 1, wanted, owners, seen);
    }

    /// <summary>The structure elements directly under a structure tree root or element, in order.</summary>
    IEnumerable<PdfDictionary> StructChildren(PdfDictionary node)
    {
        var k = _doc.Resolve(node.GetRaw("K"));
        if (k is PdfDictionary single)
        {
            if (single.ContainsKeyRaw("S")) yield return single;
        }
        else if (k is PdfArray array)
        {
            foreach (var item in array.Elements)
                if (_doc.Resolve(item) is PdfDictionary child && child.ContainsKeyRaw("S")) yield return child;
        }
    }

    /// <summary>The language, actual text, title and attributes a source element carries.</summary>
    PdfStructureDetails? DetailsOf(PdfDictionary element)
    {
        string? Text(string key) => _doc.Resolve(element.GetRaw(key)) is PdfString s ? PdfSourceNavigation.TextOf(s.Value) : null;

        var attributes = new List<PdfStructureAttributes>();
        var a = _doc.Resolve(element.GetRaw("A"));
        var sets = new List<PdfItem>();
        if (a is PdfArray list) sets.AddRange(list.Elements);
        else if (a is PdfDictionary) sets.Add(element.GetRaw("A")!);
        foreach (var item in sets)
        {
            if (_doc.Resolve(item) is not PdfDictionary set || _doc.NameOf(set.GetRaw("O"))?.TrimStart('/') is not { } owner) continue;
            var values = new Dictionary<string, PdfStructureAttributeValue>();
            foreach (var pair in set.Elements)
            {
                var name = pair.Key.TrimStart('/');
                if (name == "O" || ValueOf(pair.Value, 0) is not { } value) continue;
                values[name] = value;
            }
            if (values.Count > 0) attributes.Add(new PdfStructureAttributes(owner, values));
        }

        var language = Text("Lang");
        var actual = Text("ActualText");
        var title = Text("T");
        if (language is null && actual is null && title is null && attributes.Count == 0) return null;
        return new PdfStructureDetails { Language = language, ActualText = actual, Title = title, Attributes = attributes };
    }

    PdfStructureAttributeValue? ValueOf(PdfItem item, int depth)
    {
        if (depth > 4) return null;
        switch (_doc.Resolve(item))
        {
            case PdfName n: return PdfStructureAttributeValue.FromName(n.Value.TrimStart('/'));
            case PdfString s: return PdfStructureAttributeValue.FromText(PdfSourceNavigation.TextOf(s.Value));
            case PdfBoolean b: return PdfStructureAttributeValue.FromBoolean(b.Value);
            case PdfNumber: return PdfStructureAttributeValue.FromNumber(_doc.NumberOf(item));
            case PdfArray array:
                var items = new List<PdfStructureAttributeValue>();
                foreach (var element in array.Elements)
                    if (ValueOf(element, depth + 1) is { } value) items.Add(value);
                return PdfStructureAttributeValue.FromList(items);
            default: return null;
        }
    }

    // ------------------------------------------------------------------ finding the element of a marked-content sequence

    /// <summary>The structure the source gave marked content <paramref name="mcid"/> of the stream with parent-tree key <paramref name="structParents"/>, or null.</summary>
    public Binding? Find(int structParents, int mcid)
    {
        if (structParents < 0 || mcid < 0) return null;
        if (!ParentTree.TryGetValue(structParents, out var entry) || _doc.Resolve(entry) is not PdfArray array || mcid >= array.Elements.Count) return null;
        if (_doc.Resolve(array.Elements[mcid]) is not PdfDictionary owner) return null;

        var roleMap = RoleMap;
        var chain = new List<PdfDictionary>();
        for (var current = owner; current is not null && chain.Count < 64; current = _doc.Resolve(current.GetRaw("P")) as PdfDictionary)
        {
            if (!current.ContainsKeyRaw("S")) break;                        // the structure tree root has no structure type
            chain.Add(current);
        }
        chain.Reverse();

        // The document element is the target's own; a list item's body is made by the recorder together with the item, so the source's is skipped.
        var ancestors = new List<(object Key, string Type)>();
        for (int i = 0; i < chain.Count - 1; i++)
        {
            var type = TypeOf(chain[i], roleMap, grouping: true);
            if (type == "Document") continue;
            if (type == "LBody" && ancestors.Count > 0 && ancestors[^1].Type == "LI") continue;
            ancestors.Add((new StructKey(_stamp, chain[i]), type));
        }

        var ownerType = TypeOf(owner, roleMap, grouping: false);
        if (ownerType == "LBody") ownerType = "Span";
        var alt = _doc.Resolve(owner.GetRaw("Alt")) is PdfString s ? PdfSourceNavigation.TextOf(s.Value) : null;
        return new Binding(new StructKey(_stamp, owner), ownerType, alt, ancestors);
    }

    /// <summary>The structure element a link (or other) annotation belongs to, through its <c>/StructParent</c> entry.</summary>
    PdfDictionary? OwnerOfAnnotation(PdfDictionary annotation) =>
        annotation.ContainsKeyRaw("StructParent") && ParentTree.TryGetValue(_doc.IntOf(annotation.GetRaw("StructParent")), out var entry) ? _doc.Resolve(entry) as PdfDictionary : null;

    /// <summary>The recorder key of the structure element an annotation belongs to, when that element was declared; null otherwise.</summary>
    public object? KeyOfAnnotation(PdfDictionary annotation)
    {
        if (OwnerOfAnnotation(annotation) is not { } owner) return null;
        var key = new StructKey(_stamp, owner);
        return Recorder.HasElement(key) ? key : null;
    }

    /// <summary>The standard structure type of an element: its own, or the one its role map leads to; an unknown type becomes a generic one.</summary>
    string TypeOf(PdfDictionary element, PdfDictionary? roleMap, bool grouping)
    {
        var type = (_doc.NameOf(element.GetRaw("S")) ?? "/Span").TrimStart('/');
        for (int hops = 0; hops < 8 && !StandardTypes.Contains(type) && roleMap is not null && _doc.NameOf(roleMap.GetRaw(type)) is { } mapped; hops++)
            type = mapped.TrimStart('/');
        return StandardTypes.Contains(type) ? type : grouping ? "Div" : "Span";
    }
}
