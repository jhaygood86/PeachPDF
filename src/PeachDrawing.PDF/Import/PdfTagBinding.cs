using System;
using System.Collections.Generic;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Carries the logical structure of an imported page into a document being tagged: for a marked-content sequence of the page's content
/// (<c>/P &lt;&lt;/MCID 3&gt;&gt; BDC</c>) it finds the structure element the source gave it - through the page's entry in the structure
/// tree's parent tree - and the elements above it, so the recorder can open them around the content as it is drawn.
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

    /// <summary>The structure the source gave marked content <paramref name="mcid"/> of the stream with parent-tree key <paramref name="structParents"/>, or null.</summary>
    public Binding? Find(int structParents, int mcid)
    {
        if (structParents < 0 || mcid < 0) return null;
        if (!ParentTree.TryGetValue(structParents, out var entry) || _doc.Resolve(entry) is not PdfArray array || mcid >= array.Elements.Count) return null;
        if (_doc.Resolve(array.Elements[mcid]) is not PdfDictionary owner) return null;

        var roleMap = Root is { } root ? _doc.DictOf(root, "RoleMap") : null;
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

    /// <summary>The standard structure type of an element: its own, or the one its role map leads to; an unknown type becomes a generic one.</summary>
    string TypeOf(PdfDictionary element, PdfDictionary? roleMap, bool grouping)
    {
        var type = (_doc.NameOf(element.GetRaw("S")) ?? "/Span").TrimStart('/');
        for (int hops = 0; hops < 8 && !StandardTypes.Contains(type) && roleMap is not null && _doc.NameOf(roleMap.GetRaw(type)) is { } mapped; hops++)
            type = mapped.TrimStart('/');
        return StandardTypes.Contains(type) ? type : grouping ? "Div" : "Span";
    }
}
