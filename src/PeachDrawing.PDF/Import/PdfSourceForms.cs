using System;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text.RegularExpressions;
using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// Reads the interactive form fields on a page of a <see cref="PdfSourceDocument"/> as <see cref="PdfFormFieldDefinition"/>s the writer can
/// add to a page: text fields, check boxes, radio buttons and choice fields, each drawn from the widget's own appearance stream.
/// </summary>
internal sealed class PdfSourceForms(PdfSourceDocument doc)
{
    const int ReadOnly = 1 << 0, Required = 1 << 1, NoExport = 1 << 2;
    const int Password = 1 << 13, DoNotScroll = 1 << 23, Comb = 1 << 24;
    const int Radio = 1 << 15, PushButton = 1 << 16;

    static readonly Regex FontSizeOperator = new(@"([0-9]*\.?[0-9]+)\s+Tf", RegexOptions.CultureInvariant);

    /// <summary>The kind of field a widget is, or null for one that is drawn from its appearance instead (push buttons, signatures).</summary>
    public static PdfFormFieldKind? KindOf(PdfSourceDocument doc, PdfDictionary widget)
    {
        int flags = doc.IntOf(Inherited(doc, widget, "Ff"));
        return doc.NameOf(Inherited(doc, widget, "FT")) switch
        {
            "/Tx" => PdfFormFieldKind.Text,
            "/Btn" when (flags & PushButton) != 0 => null,
            "/Btn" => (flags & Radio) != 0 ? PdfFormFieldKind.Radio : PdfFormFieldKind.Checkbox,
            "/Ch" => PdfFormFieldKind.Select,
            _ => null,
        };
    }

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

    string? FullName(PdfDictionary widget)
    {
        var parts = new List<string>();
        var current = widget;
        for (int depth = 0; current is not null && depth < 32; depth++)
        {
            if (doc.Resolve(current.GetRaw("T")) is PdfString name && name.Value.Length > 0) parts.Insert(0, PdfSourceNavigation.TextOf(name.Value));
            current = doc.Resolve(current.GetRaw("Parent")) as PdfDictionary;
        }
        return parts.Count == 0 ? null : string.Join('.', parts);
    }

    string? Text(PdfItem? item) => doc.Resolve(item) switch
    {
        PdfString s => PdfSourceNavigation.TextOf(s.Value),
        PdfName n => n.Value.TrimStart('/'),
        _ => null,
    };

    PdfDictionary? AcroForm => doc.Resolve(doc.Trailer.GetRaw("Root")) is PdfDictionary root ? doc.DictOf(root, "AcroForm") : null;

    /// <summary>The form fields on <paramref name="page"/>, in the order their widgets appear.</summary>
    public IReadOnlyList<PdfFormFieldDefinition> Fields(PdfDictionary page, double pixelsPerPoint, Func<string, string>? rename = null)
    {
        var fields = new List<PdfFormFieldDefinition>();
        if (doc.ArrayOf(page, "Annots") is not { } annots) return fields;
        var toCanvas = PdfPagePainter.PageToCanvas(doc, page);

        foreach (var item in annots.Elements)
        {
            if (doc.Resolve(item) is not PdfDictionary widget || doc.NameOf(widget.GetRaw("Subtype")) != "/Widget") continue;
            if (KindOf(doc, widget) is not { } kind) continue;
            if ((doc.IntOf(widget.GetRaw("F")) & (2 | 32)) != 0) continue;                       // hidden, or not for viewing
            if (doc.ArrayOf(widget, "Rect") is not { Elements.Count: >= 4 } rect) continue;

            double x0 = doc.NumberOf(rect.Elements[0]), y0 = doc.NumberOf(rect.Elements[1]), x1 = doc.NumberOf(rect.Elements[2]), y1 = doc.NumberOf(rect.Elements[3]);
            var a = Vector2.Transform(new Vector2((float)x0, (float)y0), toCanvas);
            var b = Vector2.Transform(new Vector2((float)x1, (float)y1), toCanvas);
            var bounds = new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            var content = new Size(Math.Abs(x1 - x0), Math.Abs(y1 - y0));

            // The upright appearance is carried to the page by the page's rotation (the y-flip of the page matrix is the writer's own).
            Matrix3x2? transform = new Matrix3x2(toCanvas.M11, toCanvas.M12, -toCanvas.M21, -toCanvas.M22, 0, 0);
            if (Math.Abs(transform.Value.M11 - 1) < 1e-6 && Math.Abs(transform.Value.M22 - 1) < 1e-6 && Math.Abs(transform.Value.M12) < 1e-6 && Math.Abs(transform.Value.M21) < 1e-6)
                transform = null;

            int flags = doc.IntOf(Inherited(doc, widget, "Ff"));
            var da = Text(Inherited(doc, widget, "DA")) ?? (AcroForm is { } form ? Text(form.GetRaw("DA")) : null) ?? "";
            double fontSize = FontSizeOperator.Match(da) is { Success: true } m ? double.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) : 0;
            var onState = OnState(widget) ?? "Yes";
            var look = new WidgetAppearance(doc, widget, pixelsPerPoint, fontSize, onState, AcroForm is { } dr ? doc.DictOf(dr, "DR") : null);

            int maxLength = doc.IntOf(Inherited(doc, widget, "MaxLen"));
            var value = Inherited(doc, widget, "V");
            bool on = kind is PdfFormFieldKind.Checkbox or PdfFormFieldKind.Radio
                && (doc.NameOf(widget.GetRaw("AS")) is { } state ? state != "/Off" : doc.NameOf(value) == "/" + onState);

            fields.Add(new PdfFormFieldDefinition
            {
                Kind = kind,
                Name = FullName(widget) is { } fullName ? (rename is null ? fullName : rename(fullName)) : null,
                Value = kind is PdfFormFieldKind.Checkbox or PdfFormFieldKind.Radio ? onState : Text(value),
                IsChecked = on,
                Options = kind == PdfFormFieldKind.Select ? Options(widget, value) : [],
                ReadOnly = (flags & ReadOnly) != 0 || (doc.IntOf(widget.GetRaw("F")) & 64) != 0,
                Required = (flags & Required) != 0,
                NoExport = (flags & NoExport) != 0,
                Password = (flags & Password) != 0,
                DoNotScroll = (flags & DoNotScroll) != 0,
                CombCells = kind == PdfFormFieldKind.Text && (flags & Comb) != 0 && maxLength > 0 ? maxLength : null,
                MaxLength = kind == PdfFormFieldKind.Text && maxLength > 0 ? maxLength : null,
                AutoFontSize = kind == PdfFormFieldKind.Text && fontSize == 0,
                Placeholder = Text(Inherited(doc, widget, "TU")),
                Bounds = bounds,
                ContentSize = content,
                AppearanceTransform = transform,
                Appearance = look,
            });
        }
        return fields;
    }

    /// <summary>The name of a check box or radio button's "on" appearance state: the one state of its normal appearance that is not Off.</summary>
    string? OnState(PdfDictionary widget)
    {
        if (doc.DictOf(widget, "AP") is not { } ap || doc.Resolve(ap.GetRaw("N")) is not PdfDictionary { Stream: null } states) return null;
        foreach (var pair in states.Elements)
        {
            var name = pair.Key.TrimStart('/');
            if (name != "Off") return name;
        }
        return null;
    }

    IReadOnlyList<PdfFormOption> Options(PdfDictionary widget, PdfItem? value)
    {
        var chosen = new HashSet<string>(StringComparer.Ordinal);
        switch (doc.Resolve(value))
        {
            case PdfArray list:
                foreach (var entry in list.Elements)
                    if (Text(entry) is { } text) chosen.Add(text);
                break;
            default:
                if (Text(value) is { } single) chosen.Add(single);
                break;
        }

        var options = new List<PdfFormOption>();
        if (doc.Resolve(Inherited(doc, widget, "Opt")) is PdfArray opt)
        {
            foreach (var entry in opt.Elements)
            {
                string? export, label;
                if (doc.Resolve(entry) is PdfArray pair && pair.Elements.Count >= 2)
                    (export, label) = (Text(pair.Elements[0]), Text(pair.Elements[1]));
                else
                    (export, label) = (Text(entry), Text(entry));
                if (export is null) continue;
                options.Add(new PdfFormOption(export, label ?? export, chosen.Contains(export) || (label is not null && chosen.Contains(label))));
            }
        }
        return options;
    }

    /// <summary>Draws a widget's own appearance streams in place of the look the writer would give the field.</summary>
    sealed class WidgetAppearance(PdfSourceDocument doc, PdfDictionary widget, double pixelsPerPoint, double fontSize, string onState, PdfDictionary? defaultResources) : IPdfFormAppearance
    {
        public double PixelsPerPoint => pixelsPerPoint;

        public double TextFontSizePoints(Size sizePoints, bool autoFontSize, bool isPlaceholder) =>
            fontSize > 0 && !autoFontSize ? fontSize : Math.Clamp(sizePoints.Height * 0.7, 4, 12);

        public void PaintText(Canvas canvas, Size sizePoints, string text, bool autoFontSize, int? combCells, bool isPlaceholder) =>
            Draw(canvas, sizePoints, PdfAppearance.Normal(doc, widget, ""), variableText: true);

        public void PaintCheckbox(Canvas canvas, Size sizePoints, bool isChecked) =>
            Draw(canvas, sizePoints, PdfAppearance.Normal(doc, widget, isChecked ? onState : "Off"), variableText: false);

        public void PaintRadio(Canvas canvas, Size sizePoints, bool isSelected) => PaintCheckbox(canvas, sizePoints, isSelected);

        void Draw(Canvas canvas, Size size, PdfDictionary? form, bool variableText)
        {
            if (form is null) return;
            // The appearance is upright and y-up, fitted to the control's own box; the canvas is y-down at pixels-per-point.
            var toCanvas = new Matrix3x2((float)pixelsPerPoint, 0, 0, (float)-pixelsPerPoint, 0, (float)(size.Height * pixelsPerPoint));
            var region = new Rect(0, 0, Math.Max(1, Math.Ceiling(size.Width * pixelsPerPoint)), Math.Max(1, Math.Ceiling(size.Height * pixelsPerPoint)));
            PdfAppearance.Draw(doc, form, canvas, toCanvas, region, 0, 0, size.Width, size.Height, defaultResources, variableText);
        }
    }
}
