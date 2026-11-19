using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

internal static partial class PdfAppearanceSynthesizer
{
    const int FlagMultiline = 1 << 12, FlagPassword = 1 << 13, FlagRadio = 1 << 15, FlagPushButton = 1 << 16, FlagCombo = 1 << 17;

    static readonly Regex DefaultAppearanceFont = new(@"/([^\s/\[\]()<>]+)\s+([-+]?[0-9]*\.?[0-9]+)\s+Tf", RegexOptions.CultureInvariant);

    /// <summary>The value of a field attribute, looked up the chain of parent fields.</summary>
    static PdfItem? Inherited(PdfSourceDocument doc, PdfDictionary field, string key)
    {
        var current = field;
        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            if (current.ContainsKeyRaw(key)) return current.GetRaw(key);
            current = doc.Resolve(current.GetRaw("Parent")) as PdfDictionary;
        }

        return null;
    }

    static PdfDictionary? AcroForm(PdfSourceDocument doc) =>
        doc.Resolve(doc.Trailer.GetRaw("Root")) is PdfDictionary root ? doc.DictOf(root, "AcroForm") : null;

    /// <summary>A standard font's dictionary for a name a default appearance commonly uses, or for a base font named outright.</summary>
    static string? StandardBaseFont(string name) => name switch
    {
        "Helv" or "Helvetica" => "Helvetica",
        "HeBo" or "Helvetica-Bold" => "Helvetica-Bold",
        "TiRo" or "Times-Roman" => "Times-Roman",
        "TiBo" or "Times-Bold" => "Times-Bold",
        "Cour" or "Courier" => "Courier",
        "CoBo" or "Courier-Bold" => "Courier-Bold",
        "ZaDb" or "ZapfDingbats" => "ZapfDingbats",
        _ => null,
    };

    /// <summary>What a widget shows with: the font its default appearance names and the size and color operators around it.</summary>
    sealed record TextLook(PdfDictionary FontDictionary, string FontName, double FontSize, string ColorOperators);

    static TextLook ReadLook(PdfSourceDocument doc, PdfDictionary widget)
    {
        var form = AcroForm(doc);
        string da = doc.Resolve(Inherited(doc, widget, "DA")) is PdfString s ? s.Value : form is not null && doc.Resolve(form.GetRaw("DA")) is PdfString f ? f.Value : "";
        string name = "Helv";
        double size = 0;
        if (DefaultAppearanceFont.Match(da) is { Success: true } m)
        {
            name = m.Groups[1].Value;
            size = double.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
        }

        // The color is whatever the default appearance sets besides the font: g, rg and k operators with their operands.
        var colors = new StringBuilder();
        foreach (Match c in Regex.Matches(da, @"((?:[-+]?[0-9]*\.?[0-9]+\s+){1,4})(g|rg|k)\b", RegexOptions.CultureInvariant))
            colors.Append(c.Value).Append(' ');

        PdfDictionary? font = null;
        foreach (var resources in new[] { doc.DictOf(widget, "DR"), Inherited(doc, widget, "DR") is { } dr ? doc.Resolve(dr) as PdfDictionary : null, form is not null ? doc.DictOf(form, "DR") : null })
        {
            if (resources is not null && doc.DictOf(resources, "Font") is { } fonts && doc.Resolve(fonts.GetRaw(name)) is PdfDictionary found)
            {
                font = found;
                break;
            }
        }

        if (font is null)
        {
            var lexer = new PdfLexer(Encoding.Latin1.GetBytes($"<< /Type /Font /Subtype /Type1 /BaseFont /{StandardBaseFont(name) ?? "Helvetica"} /Encoding /WinAnsiEncoding >>"));
            font = (PdfDictionary)doc.ParseItem(lexer, lexer.Next());
        }

        return new TextLook(font, name, size, colors.ToString());
    }

    /// <summary>The text of a PDF string as the characters it stands for (UTF-16 with a byte order mark, or PDFDocEncoding taken as Latin-1).</summary>
    static string Decoded(PdfSourceDocument doc, PdfItem? item) => doc.Resolve(item) switch
    {
        PdfString s => PdfSourceNavigation.TextOf(s.Value),
        PdfName n => n.Value.TrimStart('/'),
        _ => "",
    };

    /// <summary>A PDF hex string of the font codes of <paramref name="text"/>: a character the font's codes cannot hold is shown as a question mark.</summary>
    static string HexString(string text)
    {
        var sb = new StringBuilder("<");
        foreach (char c in text)
            sb.Append(((c < 256 ? c : '?') & 0xFF).ToString("X2", CultureInfo.InvariantCulture));
        return sb.Append('>').ToString();
    }

    static Result? Widget(PdfSourceDocument doc, PdfDictionary widget, RenderContext context)
    {
        if (doc.ArrayOf(widget, "Rect") is not { Elements.Count: >= 4 } rect)
            return null;
        double x0 = Math.Min(doc.NumberOf(rect.Elements[0]), doc.NumberOf(rect.Elements[2])), y0 = Math.Min(doc.NumberOf(rect.Elements[1]), doc.NumberOf(rect.Elements[3]));
        double w = Math.Abs(doc.NumberOf(rect.Elements[2]) - doc.NumberOf(rect.Elements[0])), h = Math.Abs(doc.NumberOf(rect.Elements[3]) - doc.NumberOf(rect.Elements[1]));
        if (!(w > 0) || !(h > 0)) return null;

        int flags = doc.IntOf(Inherited(doc, widget, "Ff"));
        var type = doc.NameOf(Inherited(doc, widget, "FT"));
        var mk = doc.DictOf(widget, "MK");

        // Background and border from /MK (/BG fill, /BC stroke, /BS width and style), under whatever the field shows.
        var content = new StringBuilder($"q 1 0 0 1 {Num(x0)} {Num(y0)} cm\n");
        double border = 0;
        if (mk is not null)
        {
            if (mk.ContainsKeyRaw("BG"))
                content.Append(ColorOperator(doc, mk.GetRaw("BG"), fill: true, "")).Append($"0 0 {Num(w)} {Num(h)} re f\n");
            if (mk.ContainsKeyRaw("BC") && doc.Resolve(mk.GetRaw("BC")) is PdfArray { Elements.Count: > 0 })
            {
                var bs = doc.DictOf(widget, "BS");
                border = bs is not null && bs.ContainsKeyRaw("W") ? doc.NumberOf(bs.GetRaw("W")) : 1;
                var style = bs is not null ? doc.NameOf(bs.GetRaw("S")) : null;
                if (border > 0)
                {
                    content.Append(ColorOperator(doc, mk.GetRaw("BC"), fill: false, "0 0 0 RG ")).Append($"{Num(border)} w ");
                    if (style == "/D") content.Append("[3] 0 d ");
                    content.Append(style == "/U"
                        ? $"0 {Num(border / 2)} m {Num(w)} {Num(border / 2)} l S\n"
                        : $"{Num(border / 2)} {Num(border / 2)} {Num(w - border)} {Num(h - border)} re S\n");
                }
            }
        }

        string? resources = null;
        PdfDictionary? resourceDictionary = null;
        switch (type)
        {
            case "/Btn" when (flags & FlagPushButton) == 0:
                content.Append(Mark(doc, widget, mk, flags, w, h));
                break;
            case "/Tx":
                (var text, resourceDictionary) = TextField(doc, widget, flags, w, h, border, context);
                content.Append(text);
                break;
            case "/Ch":
                (var choice, resourceDictionary) = ChoiceField(doc, widget, flags, w, h, border, context);
                content.Append(choice);
                break;
            default:
                return null;
        }

        content.Append("Q\n");
        var built = Build(doc, content.ToString(), resources);
        return built with { Resources = resourceDictionary ?? built.Resources };
    }

    /// <summary>Resources naming one font as /F0.</summary>
    static PdfDictionary FontResources(PdfSourceDocument doc, PdfDictionary font)
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes("<< /Font << >> >>"));
        var resources = (PdfDictionary)doc.ParseItem(lexer, lexer.Next());
        ((PdfDictionary)resources.GetRaw("Font")!).Elements["/F0"] = font;
        return resources;
    }

    // ---------------------------------------------------------------- check boxes and radio buttons

    /// <summary>The mark of a check box or radio button that is on: the shape its /MK /CA character names, black (or the default appearance's color).</summary>
    static string Mark(PdfSourceDocument doc, PdfDictionary widget, PdfDictionary? mk, int flags, double w, double h)
    {
        var on = doc.NameOf(widget.GetRaw("AS")) is { } state ? state != "/Off" : doc.NameOf(Inherited(doc, widget, "V")) is { } v && v != "/Off";
        if (!on) return "";
        bool radio = (flags & FlagRadio) != 0;
        string style = mk is not null && doc.Resolve(mk.GetRaw("CA")) is PdfString ca && ca.Value.Length > 0 ? ca.Value[..1] : radio ? "l" : "4";

        double cx = w / 2, cy = h / 2, r = Math.Min(w, h) / 2 * 0.6;
        var sb = new StringBuilder("0 0 0 rg 0 0 0 RG\n");
        switch (style)
        {
            case "l":    // circle
                sb.Append(Circle(cx, cy, r)).Append("f\n");
                break;
            case "n":    // square
                sb.Append($"{Num(cx - r)} {Num(cy - r)} {Num(2 * r)} {Num(2 * r)} re f\n");
                break;
            case "u":    // diamond
                sb.Append($"{Num(cx - r)} {Num(cy)} m {Num(cx)} {Num(cy + r)} l {Num(cx + r)} {Num(cy)} l {Num(cx)} {Num(cy - r)} l h f\n");
                break;
            case "8":    // cross
                sb.Append($"1 w {Num(cx - r)} {Num(cy - r)} m {Num(cx + r)} {Num(cy + r)} l S {Num(cx - r)} {Num(cy + r)} m {Num(cx + r)} {Num(cy - r)} l S\n");
                break;
            case "H":    // star
                {
                    var star = new StringBuilder();
                    for (int i = 0; i < 10; i++)
                    {
                        double radius = i % 2 == 0 ? r : r * 0.4, angle = Math.PI / 2 + i * Math.PI / 5;
                        star.Append($"{Num(cx + radius * Math.Cos(angle))} {Num(cy + radius * Math.Sin(angle))} {(i == 0 ? "m" : "l")} ");
                    }

                    sb.Append(star).Append("h f\n");
                    break;
                }

            default:     // check mark
                sb.Append($"{Num(Math.Max(1, r / 4))} w {Num(cx - r)} {Num(cy)} m {Num(cx - r / 3)} {Num(cy - r * 0.75)} l {Num(cx + r)} {Num(cy + r * 0.8)} l S\n");
                break;
        }

        return sb.ToString();
    }

    static string Circle(double cx, double cy, double r)
    {
        const double K = 0.5522847498;
        double k = r * K;
        return $"{Num(cx + r)} {Num(cy)} m {Num(cx + r)} {Num(cy + k)} {Num(cx + k)} {Num(cy + r)} {Num(cx)} {Num(cy + r)} c "
            + $"{Num(cx - k)} {Num(cy + r)} {Num(cx - r)} {Num(cy + k)} {Num(cx - r)} {Num(cy)} c "
            + $"{Num(cx - r)} {Num(cy - k)} {Num(cx - k)} {Num(cy - r)} {Num(cx)} {Num(cy - r)} c "
            + $"{Num(cx + k)} {Num(cy - r)} {Num(cx + r)} {Num(cy - k)} {Num(cx + r)} {Num(cy)} c h ";
    }

    // ---------------------------------------------------------------- text and choice fields

    const double Ascent = 0.905, Descent = -0.212;      // the metrics of the substituted standard fonts

    /// <summary>The width of <paramref name="text"/> in the field's font at <paramref name="size"/>, from the importer's own font.</summary>
    static double Measure(PdfSourceDocument doc, TextLook look, RenderContext context, string text, double size)
    {
        var font = PdfImportedFont.Load(doc, look.FontDictionary, context, out _);
        if (font is null) return text.Length * size * 0.5;
        double total = 0;
        foreach (var g in font.Decode(new string(text.Select(c => (char)(c < 256 ? c : '?')).ToArray())))
            total += g.Width * size;
        return total;
    }

    static (string Content, PdfDictionary Resources) TextField(PdfSourceDocument doc, PdfDictionary widget, int flags, double w, double h, double border, RenderContext context)
    {
        var look = ReadLook(doc, widget);
        var value = Decoded(doc, Inherited(doc, widget, "V"));
        int maxLength = doc.IntOf(Inherited(doc, widget, "MaxLen"));
        if (maxLength > 0 && value.Length > maxLength) value = value[..maxLength];
        if ((flags & FlagPassword) != 0) value = new string('*', value.Length);
        bool multiline = (flags & FlagMultiline) != 0;
        int quadding = doc.IntOf(Inherited(doc, widget, "Q"));

        double inset = Math.Max(border, 1) + 1;
        double size = look.FontSize > 0 ? look.FontSize : multiline ? 12 : Math.Clamp((h - 2 * border) / (Ascent - Descent) * 0.8, 4, 12 * 2);
        var resources = FontResources(doc, look.FontDictionary);
        if (value.Length == 0) return ("", resources);

        double x, baseline;
        var lines = multiline ? value.Replace("\r\n", "\n").Split('\n', '\r') : [value];
        var sb = new StringBuilder($"q {Num(border)} {Num(border)} {Num(w - 2 * border)} {Num(h - 2 * border)} re W n BT {(look.ColorOperators.Length > 0 ? look.ColorOperators : "0 g ")}/F0 {Num(size)} Tf\n");
        double lineHeight = size * (Ascent - Descent);
        baseline = multiline ? h - inset - size * Ascent : (h - lineHeight) / 2 - size * Descent;
        double lastX = 0, lastY = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            double width = Measure(doc, look, context, lines[i], size);
            x = quadding == 1 ? (w - width) / 2 : quadding == 2 ? w - inset - width : inset;
            double y = baseline - i * lineHeight;
            sb.Append($"{Num(x - lastX)} {Num(y - lastY)} Td {HexString(lines[i])} Tj\n");
            (lastX, lastY) = (x, y);
        }

        sb.Append("ET Q\n");
        return (sb.ToString(), resources);
    }

    static (string Content, PdfDictionary Resources) ChoiceField(PdfSourceDocument doc, PdfDictionary widget, int flags, double w, double h, double border, RenderContext context)
    {
        var look = ReadLook(doc, widget);
        var resources = FontResources(doc, look.FontDictionary);
        double size = look.FontSize > 0 ? look.FontSize : 12;
        var options = new List<(string Export, string Display)>();
        var optionList = doc.ArrayOf(widget, "Opt") ?? (Inherited(doc, widget, "Opt") is { } o ? doc.Resolve(o) as PdfArray : null);
        if (optionList is { } opt)
        {
            foreach (var item in opt.Elements)
            {
                if (doc.Resolve(item) is PdfArray { Elements.Count: >= 2 } pair) options.Add((Decoded(doc, pair.Elements[0]), Decoded(doc, pair.Elements[1])));
                else options.Add((Decoded(doc, item), Decoded(doc, item)));
            }
        }

        var selected = new HashSet<string>(StringComparer.Ordinal);
        var v = doc.Resolve(Inherited(doc, widget, "V"));
        if (v is PdfArray values) foreach (var e in values.Elements) selected.Add(Decoded(doc, e));
        else if (v is not null) selected.Add(Decoded(doc, v));

        double inset = Math.Max(border, 1) + 1;
        var sb = new StringBuilder($"q {Num(border)} {Num(border)} {Num(w - 2 * border)} {Num(h - 2 * border)} re W n\n");
        string color = look.ColorOperators.Length > 0 ? look.ColorOperators : "0 g ";
        double lineHeight = size * (Ascent - Descent);

        if ((flags & FlagCombo) != 0)
        {
            // A combo box shows the chosen item's text (or the value itself when it is editable text).
            var shown = options.FirstOrDefault(p => selected.Contains(p.Export)).Display ?? (selected.Count > 0 ? selected.First() : "");
            if (shown.Length > 0)
            {
                double baseline = (h - lineHeight) / 2 - size * Descent;
                sb.Append($"BT {color}/F0 {Num(size)} Tf {Num(inset)} {Num(baseline)} Td {HexString(shown)} Tj ET\n");
            }
        }
        else
        {
            // A list box shows its items from the top; each chosen one is highlighted in PDFium's selection blue with white text.
            int top = Math.Max(0, doc.IntOf(Inherited(doc, widget, "TI")));
            for (int i = top; i < options.Count; i++)
            {
                double y = h - inset - (i - top + 1) * lineHeight;
                if (y + lineHeight < 0) break;
                bool chosen = selected.Contains(options[i].Export) || selected.Contains(options[i].Display);
                if (chosen) sb.Append($"0 0.2 0.5 rg {Num(border)} {Num(y)} {Num(w - 2 * border)} {Num(lineHeight)} re f\n");
                sb.Append($"BT {(chosen ? "1 g " : color)}/F0 {Num(size)} Tf {Num(inset)} {Num(y - size * Descent)} Td {HexString(options[i].Display)} Tj ET\n");
            }
        }

        sb.Append("Q\n");
        return (sb.ToString(), resources);
    }
}
