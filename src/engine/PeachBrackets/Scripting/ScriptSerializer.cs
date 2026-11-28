using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Frozen;
using System.Text;

namespace PeachBrackets.ScriptHosting;

/// <summary>Writes the box tree the HTML parser makes back as HTML, so a document a script changed can be parsed again and give the same tree.</summary>
internal static class ScriptSerializer
{
    // Elements whose text is not markup: it is written as it is, not escaped.
    private static readonly FrozenSet<string> RawText = new[] { "script", "style", "xmp", "iframe", "noembed", "noframes", "plaintext", "noscript" }
        .ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>The document's mode as the DOCTYPE that gives it.</summary>
    public static string DocType(DocumentMode mode) => mode switch
    {
        DocumentMode.NoQuirks => "<!DOCTYPE html>",
        DocumentMode.LimitedQuirks => "<!DOCTYPE html PUBLIC \"-//W3C//DTD XHTML 1.0 Transitional//EN\" \"http://www.w3.org/TR/xhtml1/DTD/xhtml1-transitional.dtd\">",
        _ => string.Empty,
    };

    public static string SerializeChildren(CssBox box)
    {
        var text = new StringBuilder();
        WriteChildren(box, text);
        return text.ToString();
    }

    /// <summary>
    /// The children of <paramref name="box"/> as markup in which a media element that is playing somewhere is told where: its <c>src</c> (and that of its
    /// <c>source</c> children) gets a media fragment (<c>#t=seconds</c>) for the position <paramref name="positionOf"/> reports, so a copy of the document
    /// that has its own player shows the picture the live element shows.
    /// </summary>
    public static string SerializeChildren(CssBox box, Func<CssBox, TimeSpan?> positionOf)
    {
        var previous = s_positionOf;
        s_positionOf = positionOf;
        try
        {
            return SerializeChildren(box);
        }
        finally
        {
            s_positionOf = previous;
        }
    }

    /// <summary>
    /// The children of <paramref name="box"/> as markup in which a <c>canvas</c> that stands in for an OffscreenCanvas has the size <paramref name="sizeOf"/> reports, whatever its
    /// <c>width</c> and <c>height</c> attributes have come to say (a copy of the document lays it out at the size of its bitmap).
    /// </summary>
    public static string SerializeChildren(CssBox box, Func<CssBox, (double Width, double Height)?> sizeOf)
    {
        var previous = s_sizeOf;
        s_sizeOf = sizeOf;
        try
        {
            return SerializeChildren(box);
        }
        finally
        {
            s_sizeOf = previous;
        }
    }

    /// <summary>Both: media elements told where their players are, and placeholder canvases given their size.</summary>
    public static string SerializeChildren(CssBox box, Func<CssBox, TimeSpan?> positionOf, Func<CssBox, (double Width, double Height)?> sizeOf)
    {
        var previous = s_sizeOf;
        s_sizeOf = sizeOf;
        try
        {
            return SerializeChildren(box, positionOf);
        }
        finally
        {
            s_sizeOf = previous;
        }
    }

    [ThreadStatic]
    private static Func<CssBox, (double Width, double Height)?>? s_sizeOf;

    [ThreadStatic]
    private static Func<CssBox, TimeSpan?>? s_positionOf;

    // The fragment a media resource of this element is given, or null.
    private static string? MediaFragment(CssBox box, CssBox? parent, string name, string attribute)
    {
        if (s_positionOf is not { } positionOf || !string.Equals(attribute, "src", StringComparison.OrdinalIgnoreCase))
            return null;

        var media = string.Equals(name, "video", StringComparison.OrdinalIgnoreCase) ? box
            : string.Equals(name, "source", StringComparison.OrdinalIgnoreCase) && parent?.HtmlTag is { } owner && string.Equals(owner.Name, "video", StringComparison.OrdinalIgnoreCase) ? parent
            : null;
        return media is not null && positionOf(media) is { } position
            ? "#t=" + position.TotalSeconds.ToString("0.######", System.Globalization.CultureInfo.InvariantCulture)
            : null;
    }

    public static string SerializeNode(CssBox box)
    {
        var text = new StringBuilder();
        Write(box, text, box.ParentBox);
        return text.ToString();
    }

    private static void WriteChildren(CssBox box, StringBuilder text)
    {
        foreach (var child in box.Boxes)
            Write(child, text, box);
    }

    private static void Write(CssBox box, StringBuilder text, CssBox? parent)
    {
        if (box.HtmlTag is not { } tag)
        {
            if (SpecialNode.Of(box) is { } special)
            {
                switch (special.Kind)
                {
                    case ScriptNodeKind.Comment:
                        text.Append("<!--").Append(special.Data).Append("-->");
                        break;
                    case ScriptNodeKind.ProcessingInstruction:
                        text.Append("<?").Append(special.Name).Append(' ').Append(special.Data).Append('>');
                        break;
                    case ScriptNodeKind.DocumentType:
                        text.Append("<!DOCTYPE ").Append(special.Name);
                        if (special.PublicId.Length > 0)
                            text.Append(" PUBLIC \"").Append(special.PublicId).Append("\" \"").Append(special.SystemId).Append('"');
                        else if (special.SystemId.Length > 0)
                            text.Append(" SYSTEM \"").Append(special.SystemId).Append('"');
                        text.Append('>');
                        break;
                }

                return;
            }

            if (box.Text is { } own)
            {
                if (parent?.HtmlTag is { } owner && RawText.Contains(owner.Name))
                    text.Append(own);
                else
                    Escape(own, text, attribute: false);
            }

            return;
        }

        text.Append('<').Append(tag.Name);
        if (tag.Attributes is { } attributes)
        {
            foreach (var (name, value) in attributes)
            {
                text.Append(' ').Append(name).Append("=\"");
                if (s_sizeOf is { } sizeOf && string.Equals(tag.Name, "canvas", StringComparison.OrdinalIgnoreCase) && sizeOf(box) is { } size
                    && (string.Equals(name, "width", StringComparison.OrdinalIgnoreCase) || string.Equals(name, "height", StringComparison.OrdinalIgnoreCase)))
                {
                    text.Append((string.Equals(name, "width", StringComparison.OrdinalIgnoreCase) ? size.Width : size.Height).ToString("0", System.Globalization.CultureInfo.InvariantCulture));
                }
                else if (MediaFragment(box, parent, tag.Name, name) is { } fragment)
                {
                    var hash = value.IndexOf('#', StringComparison.Ordinal);
                    Escape(hash >= 0 ? value[..hash] : value, text, attribute: true);
                    text.Append(fragment);
                }
                else
                {
                    Escape(value, text, attribute: true);
                }

                text.Append('"');
            }
        }

        text.Append('>');

        // Only an HTML void element has no end tag: in SVG and MathML an element that was written <rect/> still ends, or the next sibling would be parsed as its child.
        if (!InForeignContent(box) && (tag.IsSingle || HtmlUtils.IsSingleTag(tag.Name.ToLowerInvariant())))
            return;

        // A line break straight after <pre>, <textarea> and <listing> is dropped by the parser, so one the text starts with has to be doubled.
        if (box.Boxes.Count > 0 && tag.Name.ToLowerInvariant() is "pre" or "textarea" or "listing"
            && box.Boxes[0] is { HtmlTag: null, Text: { } first } && first.StartsWith('\n'))
            text.Append('\n');

        WriteChildren(box, text);
        text.Append("</").Append(tag.Name).Append('>');
    }

    // Whether the element is an svg or math element or inside one (the foreign content of the HTML parser).
    private static bool InForeignContent(CssBox box)
    {
        for (var current = box; current is not null; current = current.ParentBox)
        {
            if (current.HtmlTag is { } tag && (string.Equals(tag.Name, "svg", StringComparison.OrdinalIgnoreCase) || string.Equals(tag.Name, "math", StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static void Escape(string value, StringBuilder text, bool attribute)
    {
        foreach (var c in value)
        {
            switch (c)
            {
                case '&': text.Append("&amp;"); break;
                case ' ': text.Append("&nbsp;"); break;
                case '<' when !attribute: text.Append("&lt;"); break;
                case '>' when !attribute: text.Append("&gt;"); break;
                case '"' when attribute: text.Append("&quot;"); break;
                default: text.Append(c); break;
            }
        }
    }
}
