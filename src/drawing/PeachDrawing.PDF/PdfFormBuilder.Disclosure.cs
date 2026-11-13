using PeachDrawing.Core;
using PeachDrawing.PDF.Engine;
using PeachDrawing.PDF.Engine.AcroForms;
using PeachDrawing.PDF.Engine.Annotations;
using System;
using System.Threading.Tasks;

namespace PeachDrawing.PDF;
/// <summary>
/// A region of a page that a reader can open and close: while closed the page shows only what was drawn on the page itself (a heading the
/// reader clicks), and once opened a drawn rectangle covers that heading and what lies beneath it. Nothing reflows: the opened content
/// is laid over the page.
/// </summary>
public sealed class PdfDisclosureDefinition
{
    /// <summary>
    /// The heading's box: what a reader clicks to open the region and, once it is open, to close it. In points from the page's top-left corner
    /// with y down.
    /// </summary>
    public required Rect ToggleBounds { get; init; }

    /// <summary>
    /// Where the opened region is drawn, in points from the page's top-left corner with y down. It should cover <see cref="ToggleBounds"/>, so
    /// that the heading is drawn again, in its opened state, as part of the region.
    /// </summary>
    public required Rect ContentBounds { get; init; }

    /// <summary>Draws the opened region onto a canvas of <see cref="ContentBounds"/>'s size, with its top-left corner at the origin.</summary>
    public required Func<Canvas, ValueTask> PaintContent { get; init; }

    /// <summary>
    /// Whether the region starts open: it is drawn on the page itself, and <see cref="ToggleBounds"/> is its heading, which a reader clicks to
    /// collapse it. <see cref="PaintHeading"/> then draws the heading as it looks collapsed, and <see cref="PaintContent"/> what covers the
    /// page below the heading once it is (<see cref="ContentBounds"/> is that area, below <see cref="ToggleBounds"/>).
    /// </summary>
    public bool InitiallyOpen { get; init; }

    /// <summary>For a region that <see cref="InitiallyOpen"/>, draws its collapsed heading onto a canvas of <see cref="ToggleBounds"/>'s size.</summary>
    public Func<Canvas, ValueTask>? PaintHeading { get; init; }

    /// <summary>The layout units per point <see cref="PaintContent"/> draws in.</summary>
    public double PixelsPerPoint { get; init; } = 1;

    /// <summary>What the controls are called to assistive technology, or <see langword="null"/> for none.</summary>
    public string? AccessibleName { get; init; }
}

public sealed partial class PdfFormBuilder
{
    /// <summary>
    /// Adds a region a reader can open and close without scripting: a hidden rectangle holding <see cref="PdfDisclosureDefinition.PaintContent"/>,
    /// and a transparent button over the heading; the button's <c>Hide</c> action shows the rectangle (and removes the button), and clicking the rectangle hides it again. A reader that
    /// does not run them (a printer, a viewer without interactive forms) shows the closed page.
    /// </summary>
    /// <remarks>
    /// <c>Hide</c> actions are not allowed in a PDF/A or PDF/X document, so callers leave them out of one.
    /// </remarks>
    /// <param name="page">the page the region goes on</param>
    /// <param name="disclosure">what the region is, where it is and how it looks</param>
    /// <returns>a task that completes when the region is drawn</returns>
    public async ValueTask AddDisclosureAsync(PdfComposerPage page, PdfDisclosureDefinition disclosure)
    {
        ArgumentNullException.ThrowIfNull(page);
        ArgumentNullException.ThrowIfNull(disclosure);

        if (disclosure.InitiallyOpen && disclosure.PaintHeading is null)
            throw new ArgumentException("A region that starts open needs PaintHeading.", nameof(disclosure));

        var acroForm = EnsureAcroForm();
        var enginePage = page.Page;
        var id = ++_disclosureCounter;
        var contentName = $"{_generatedNamePrefix}Disclosure{id}Content";
        var openName = $"{_generatedNamePrefix}Disclosure{id}Open";

        PdfRectangle ToPdf(Rect bounds) => new(new Rect(bounds.X, page.HeightPoints - (bounds.Y + bounds.Height), bounds.Width, bounds.Height));

        if (disclosure.InitiallyOpen)
        {
            await AddCollapsibleAsync(acroForm, enginePage, disclosure, ToPdf, contentName, openName, id);
            return;
        }

        // The region: not shown until the button shows it, and read-only so a reader does not let it be edited. It has no value to export.
        var content = disclosure.ContentBounds;
        var contentField = new PdfPushButtonField(Document)
        {
            Rectangle = ToPdf(content),
            PartialFieldName = contentName,
            FieldFlags = PdfPushButtonField.PushButtonFlag | PdfAcroField.NoExportFlag,
            Flags = PdfAnnotationFlags.Hidden | PdfAnnotationFlags.Print,
        };
        Name(contentField, disclosure);
        SetNormalAppearance(contentField, await CreateAppearanceFormAsync(Document, RenderContext, disclosure.PixelsPerPoint, new Size(content.Width, content.Height), disclosure.PaintContent));

        // The button over the heading: a click target with nothing drawn, so the heading underneath shows through.
        var toggle = disclosure.ToggleBounds;
        var openButton = new PdfPushButtonField(Document)
        {
            Rectangle = ToPdf(toggle),
            PartialFieldName = openName,
            FieldFlags = PdfPushButtonField.PushButtonFlag | PdfAcroField.NoExportFlag,
            Flags = PdfAnnotationFlags.Print,
        };
        Name(openButton, disclosure);
        SetNormalAppearance(openButton, CreateAppearanceForm(Document, RenderContext, disclosure.PixelsPerPoint, new Size(toggle.Width, toggle.Height), _ => { }));

        // Opening shows the region and takes the button away; clicking the region (which covers the button) hides it and brings the button back.
        // A Hide action sets one visibility, so each step is an action of its own, chained.
        openButton.Elements.SetObject("/A", Chain(Hide(false, contentName), Hide(true, openName)));
        contentField.Elements.SetObject("/A", Chain(Hide(true, contentName), Hide(false, openName)));

        foreach (var field in new PdfAcroField[] { contentField, openButton })
        {
            enginePage.Annotations.Add(field);
            acroForm.AddField(field);
        }
    }

    private int _disclosureCounter;

    // A region that starts open: a button over its heading collapses it by showing a collapsed heading and the rest of the page as it is
    // collapsed; clicking the collapsed heading brings the open page back. The covering body takes no clicks.
    private async ValueTask AddCollapsibleAsync(
        PdfAcroForm acroForm, PdfPage enginePage, PdfDisclosureDefinition disclosure, Func<Rect, PdfRectangle> toPdf, string bodyName, string collapseName, int id)
    {
        var headingName = $"{_generatedNamePrefix}Disclosure{id}Heading";
        var toggle = disclosure.ToggleBounds;
        var body = disclosure.ContentBounds;

        var bodyField = new PdfPushButtonField(Document)
        {
            Rectangle = toPdf(body),
            PartialFieldName = bodyName,
            FieldFlags = PdfPushButtonField.PushButtonFlag | PdfAcroField.NoExportFlag,
            Flags = PdfAnnotationFlags.Hidden | PdfAnnotationFlags.Print,
        };
        SetNormalAppearance(bodyField, await CreateAppearanceFormAsync(Document, RenderContext, disclosure.PixelsPerPoint, new Size(body.Width, body.Height), disclosure.PaintContent));

        var headingField = new PdfPushButtonField(Document)
        {
            Rectangle = toPdf(toggle),
            PartialFieldName = headingName,
            FieldFlags = PdfPushButtonField.PushButtonFlag | PdfAcroField.NoExportFlag,
            Flags = PdfAnnotationFlags.Hidden | PdfAnnotationFlags.Print,
        };
        Name(headingField, disclosure);
        SetNormalAppearance(headingField, await CreateAppearanceFormAsync(Document, RenderContext, disclosure.PixelsPerPoint, new Size(toggle.Width, toggle.Height), disclosure.PaintHeading!));

        var collapseButton = new PdfPushButtonField(Document)
        {
            Rectangle = toPdf(toggle),
            PartialFieldName = collapseName,
            FieldFlags = PdfPushButtonField.PushButtonFlag | PdfAcroField.NoExportFlag,
            Flags = PdfAnnotationFlags.Print,
        };
        Name(collapseButton, disclosure);
        SetNormalAppearance(collapseButton, CreateAppearanceForm(Document, RenderContext, disclosure.PixelsPerPoint, new Size(toggle.Width, toggle.Height), _ => { }));

        collapseButton.Elements.SetObject("/A", Chain(Hide(false, headingName), Hide(false, bodyName), Hide(true, collapseName)));
        headingField.Elements.SetObject("/A", Chain(Hide(true, headingName), Hide(true, bodyName), Hide(false, collapseName)));

        foreach (var field in new PdfAcroField[] { bodyField, headingField, collapseButton })
        {
            enginePage.Annotations.Add(field);
            acroForm.AddField(field);
        }
    }

    private static async ValueTask<PeachDrawing.PDF.Engine.Advanced.PdfFormXObject> CreateAppearanceFormAsync(
        PdfDocument document, PdfRenderContext renderContext, double pixelsPerPoint, Size sizePt, Func<Canvas, ValueTask> draw)
    {
        var form = new PeachDrawing.PDF.Engine.Drawing.PdfForm(document, sizePt);
        var formGraphics = PeachDrawing.PDF.Engine.Drawing.PdfGraphics.FromForm(form);
        var canvas = new PdfCanvas(renderContext, formGraphics, pixelsPerPoint, releaseGraphics: true);
        try
        {
            await draw(canvas);
        }
        finally
        {
            canvas.Dispose();
        }

        return form.FormXObject;
    }

    private static void Name(PdfAcroField field, PdfDisclosureDefinition disclosure)
    {
        if (disclosure.AccessibleName is { Length: > 0 } name)
            field.AlternateFieldName = name;
    }

    private PdfDictionary Chain(PdfDictionary first, params PdfDictionary[] rest)
    {
        var next = new PdfArray(Document);
        foreach (var action in rest)
            next.Elements.Add(action);
        first.Elements.SetObject("/Next", next);
        return first;
    }

    private PdfDictionary Hide(bool hide, params string[] fieldNames)
    {
        var targets = new PdfArray(Document);
        foreach (var name in fieldNames)
            targets.Elements.Add(new PdfString(name, PdfStringEncoding.WinAnsiEncoding));

        var action = new PdfDictionary(Document);
        action.Elements.SetName("/S", "/Hide");
        action.Elements.SetObject("/T", targets);
        action.Elements.SetBoolean("/H", hide);
        return action;
    }
}
