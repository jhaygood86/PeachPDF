using System;
using System.Collections.Generic;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Which optional-content groups (layers) of a document are visible in its default configuration (ISO 32000-1 8.11): the catalog's
/// <c>/OCProperties /D</c> with its <c>/BaseState</c> and <c>/ON</c> and <c>/OFF</c> lists. A group is a dictionary; a membership dictionary
/// (<c>/OCMD</c>) combines groups by its <c>/P</c> policy or a <c>/VE</c> visibility expression. The configuration's usage-application
/// entries (<c>/AS</c>: view, print, export by the event) are not applied, since a reader shows the document for viewing.
/// </summary>
internal sealed class PdfOptionalContent
{
    readonly PdfSourceDocument _doc;
    readonly bool _baseOn = true;
    readonly HashSet<PdfDictionary> _on = new(ReferenceEqualityComparer.Instance as IEqualityComparer<PdfDictionary>);
    readonly HashSet<PdfDictionary> _off = new(ReferenceEqualityComparer.Instance as IEqualityComparer<PdfDictionary>);

    public PdfOptionalContent(PdfSourceDocument doc)
    {
        _doc = doc;
        if (doc.Resolve(doc.Trailer.GetRaw("Root")) is not PdfDictionary root || doc.DictOf(root, "OCProperties") is not { } props) return;
        if (doc.DictOf(props, "D") is not { } config) return;
        _baseOn = doc.NameOf(config.GetRaw("BaseState")) != "/OFF";
        Collect(doc.ArrayOf(config, "ON"), _on);
        Collect(doc.ArrayOf(config, "OFF"), _off);
    }

    void Collect(PdfArray? array, HashSet<PdfDictionary> into)
    {
        if (array is null) return;
        foreach (var item in array.Elements) if (_doc.Resolve(item) is PdfDictionary group) into.Add(group);
    }

    bool GroupOn(PdfDictionary group) => _off.Contains(group) ? false : _on.Contains(group) || _baseOn;

    /// <summary>Whether what an <c>/OC</c> entry (or a marked-content property) names is shown; anything that is not a group or membership is shown.</summary>
    public bool IsVisible(PdfItem? item, int depth = 0)
    {
        if (depth > 8 || _doc.Resolve(item) is not PdfDictionary d) return true;
        string? type = _doc.NameOf(d.GetRaw("Type"));
        if (type == "/OCMD")
        {
            if (_doc.Resolve(d.GetRaw("VE")) is PdfArray expression) return Evaluate(expression, depth + 1);
            var groups = new List<PdfDictionary>();
            var raw = _doc.Resolve(d.GetRaw("OCGs"));
            if (raw is PdfDictionary single) groups.Add(single);
            else if (raw is PdfArray many) foreach (var e in many.Elements) if (_doc.Resolve(e) is PdfDictionary g) groups.Add(g);
            if (groups.Count == 0) return true;
            bool anyOn = false, allOn = true;
            foreach (var g in groups) { bool on = GroupOn(g); anyOn |= on; allOn &= on; }
            return _doc.NameOf(d.GetRaw("P")) switch
            {
                "/AllOn" => allOn,
                "/AnyOff" => !allOn,
                "/AllOff" => !anyOn,
                _ => anyOn,
            };
        }
        return type != "/OCG" || GroupOn(d);
    }

    /// <summary>A visibility expression: <c>[/And e...]</c>, <c>[/Or e...]</c>, <c>[/Not e]</c>, where an operand is a group or another expression.</summary>
    bool Evaluate(PdfArray expression, int depth)
    {
        if (depth > 8 || expression.Elements.Count == 0) return true;
        string? op = _doc.NameOf(expression.Elements[0]);
        bool Operand(PdfItem item) => _doc.Resolve(item) is PdfArray nested ? Evaluate(nested, depth + 1) : _doc.Resolve(item) is PdfDictionary g ? GroupOn(g) : true;
        switch (op)
        {
            case "/Not": return expression.Elements.Count < 2 || !Operand(expression.Elements[1]);
            case "/And":
                for (int i = 1; i < expression.Elements.Count; i++) if (!Operand(expression.Elements[i])) return false;
                return true;
            case "/Or":
                for (int i = 1; i < expression.Elements.Count; i++) if (Operand(expression.Elements[i])) return true;
                return expression.Elements.Count < 2;
            default: return true;
        }
    }
}
