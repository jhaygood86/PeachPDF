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
                Escape(value, text, attribute: true);
                text.Append('"');
            }
        }

        text.Append('>');
        // Only an HTML element can be void: a foreign element (<mspace/>, <path/>) parsed as self-closing still gets its end tag, or the markup
        // read back would nest everything after it.
        if (!box.IsWithinForeignContent && (tag.IsSingle || HtmlUtils.IsSingleTag(tag.Name.ToLowerInvariant())))
            return;

        // A line break straight after <pre>, <textarea> and <listing> is dropped by the parser, so one the text starts with has to be doubled.
        if (box.Boxes.Count > 0 && tag.Name.ToLowerInvariant() is "pre" or "textarea" or "listing"
            && box.Boxes[0] is { HtmlTag: null, Text: { } first } && first.StartsWith('\n'))
            text.Append('\n');

        WriteChildren(box, text);
        text.Append("</").Append(tag.Name).Append('>');
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
