// Derived from PDFsharp (MIT) - see LICENSE.md

using System.Linq;
using System.Numerics;
using PeachPDF.PDF.Drawing;
using PeachDrawing.Text.Outlines;
using PeachDrawing.Text.Shaping;
using PeachDrawing.Text;
using PeachDrawing.Core;
using PeachPDF.PDF.Internal;
using PeachPDF.PDF;
using PeachPDF.PDF.Advanced;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;

// ReSharper disable RedundantNameQualifier
// ReSharper disable CompareOfFloatsByEqualityOperator

namespace PeachPDF.PDF.Rendering;

internal sealed partial class PdfGraphicsRenderer
{
    // ----- DrawImage ----------------------------------------------------------------------------

    public void DrawImage(PdfRasterImage image, double x, double y, double width, double height)
    {
        const string format = PdfNumberFormats.FourDecimals;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm 100 Tz {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

    /// <summary>
    /// Draws all of <paramref name="image"/> into <paramref name="destRect"/>. <paramref name="srcRect"/>
    /// is NOT honored and cannot be at this level: PDF has no "draw this sub-rectangle of an XObject"
    /// operator, so cropping is a clip plus an off-destination placement of the whole image, which
    /// <c>PdfCanvas.DrawImage</c> composes above. Callers wanting a portion of an image must go
    /// through that; this overload exists so a whole-image draw keeps emitting the operators it always
    /// has. (Silently dropping srcRect here is what once made every <c>border-image</c> slice paint the
    /// entire source squashed into its own ninth of the frame.)
    /// </summary>
    public void DrawImage(PdfRasterImage image, Rect destRect, Rect srcRect)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);
        if (!(image is PdfForm))
        {
            AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do\nQ\n",
                x, y + height, width, height, name);
            
        }
        else
        {
            BeginPage();

            PdfForm form = (PdfForm)image;
            form.Finish();

            PdfFormXObject pdfForm = Owner.FormTable.GetForm(form);

            double cx = width / image.PointWidth;
            double cy = height / image.PointHeight;

            if (cx != 0 && cy != 0)
            {
                AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
                    x, y + height, cx, cy, name);
                
            }
        }
    }

    internal void AppendFormatImage(string format, double x, double y, double width, double height, string name)
    {
        PaintPoint result = WorldToView(new PaintPoint(x, y));
        _content.AppendFormat(CultureInfo.InvariantCulture, format, result.X, result.Y, width, height, name);
    }

    /// <summary>
    /// Draws <paramref name="image"/> (a Form XObject, typically a rendered SVG "content" tile)
    /// at <paramref name="destRect"/> with <paramref name="maskImage"/> (another Form XObject,
    /// sized/positioned identically to <paramref name="image"/> in its own local coordinate
    /// system) attached as a <c>/Luminosity</c> soft mask (white = fully visible, black = fully
    /// transparent) via an ExtGState <c>/SMask</c> - mirroring the pattern <c>PdfShading</c>'s own
    /// alpha-gradient soft masks already use (see <c>BuildAlphaExtGStateIfNeeded</c>), just driven
    /// by an arbitrary rendered Form XObject rather than a hand-built grayscale shading function.
    /// </summary>
    /// <remarks>
    /// The <c>gs</c> that activates the mask is emitted INSIDE the same <c>q ... cm ... Do ... Q</c>
    /// block that places <paramref name="image"/> (mirroring <see cref="DrawImage(PdfRasterImage, Rect, Rect)"/>'s
    /// own Form XObject placement exactly), rather than as a separate "push before / pop after"
    /// pair relying on the ambient CTM. This distinction matters: a Form XObject created via
    /// <c>PdfGraphics.FromForm</c> (as SVG's <c>&lt;mask&gt;</c>/<c>&lt;pattern&gt;</c> "tile"
    /// rendering does) gets its own content Y-flipped relative to ITS OWN (small) height, not the
    /// page's - a coordinate convention that's only self-consistent when the form is placed via an
    /// explicit destRect (as here, and as pattern tiles already do via ordinary <c>DrawImage</c>).
    /// Relying on the CTM already ambient at some arbitrary point in the page's own content stream
    /// (built for page-style, not form-style, coordinates) to position such a form's implicit BBox
    /// - which an earlier, simpler "PushSoftMask/PopSoftMask" implementation of this feature did -
    /// silently misplaces the mask relative to the content in every case except where the two
    /// coordinate conventions happen to coincide (i.e. never, once the page has any margin/scroll/
    /// layout offset), making the masked content evaluate as fully transparent everywhere.
    /// <para>
    /// Separately, both <paramref name="image"/> and <paramref name="maskImage"/> are given their
    /// own <c>/Group &lt;&lt; /S /Transparency ... &gt;&gt;</c> dictionary. The masked CONTENT form
    /// needing this (not just the <c>/G</c> mask target) was found the hard way: MuPDF renders an
    /// active SMask correctly even when the painted content itself isn't a transparency group, but
    /// PDFium (Chrome/Edge, and by extension most other real-world viewers/printers) silently
    /// ignores the SMask entirely in that case - a PDF that "works" only needs checking in one
    /// renderer to look done and still be broken everywhere else. Don't trust a single renderer
    /// (least of all MuPDF, which is unusually forgiving of non-conformant transparency PDFs) for
    /// anything touching soft masks or transparency groups - cross-check with PDFium too (e.g.
    /// Python's <c>pypdfium2</c>) before calling it fixed.
    /// </para>
    /// </remarks>
    internal void DrawImageMasked(PdfForm image, PdfForm maskImage, Rect destRect)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();
        maskImage.Finish();

        // Checked before any group/mask/ExtGState object is built, so a PDF/A-1 rejection never
        // leaves partially-built objects registered in the document's resources/iref table.
        PdfATransparencyGuard.RequireAllowed(Owner, _page, "An SVG <mask>");

        // A /Luminosity SMask's /G target must itself be a transparency group (PDF 32000-1 §11.6.4.3)
        // - PdfForm/PdfFormXObject don't set this up automatically, so it's added here rather than
        // generalizing CreateTile's caller-agnostic form creation for a mask-only requirement.
        var maskPdfForm = maskImage.FormXObject;
        var maskGroup = new PdfDictionary();
        maskGroup.Elements["/S"] = new PdfName("/Transparency");
        maskGroup.Elements["/CS"] = new PdfName("/DeviceGray");
        maskGroup.Elements["/I"] = new PdfBoolean(true);
        maskGroup.Elements["/K"] = new PdfBoolean(false);
        maskPdfForm.Elements["/Group"] = maskGroup;

        // The CONTENT form being masked must itself be a transparency group too, or the active
        // SMask silently has no visible effect in stricter readers (Chrome/Edge/Acrobat, all
        // PDFium- or Acrobat-derived) even though it renders correctly in MuPDF regardless -
        // MuPDF applies an SMask to plain (non-group) painted content leniently; that's MuPDF
        // being forgiving of non-conformant input, not something to rely on. Every real-world PDF
        // writer that composites masked content via a Form XObject marks that content form as an
        // isolated transparency group too, not just the /G mask target.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var softMask = new PdfSoftMask(Owner);
        softMask.Elements[PdfSoftMask.Keys.S] = new PdfName("/Luminosity");
        softMask.Elements.SetReference(PdfSoftMask.Keys.G, maskPdfForm);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements["/SMask"] = softMask;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
        
    }

    /// <summary>
    /// Draws <paramref name="image"/> (a Form XObject, typically a whole box/element subtree
    /// rendered into an offscreen tile via <c>CreateTile</c>) at <paramref name="destRect"/>,
    /// composited as a single flattened result at constant <paramref name="opacity"/> - the PDF
    /// mechanism behind CSS/SVG group <c>opacity</c>. This is deliberately NOT the same as painting
    /// each of the tile's own shapes at reduced alpha individually (the simpler, but double-blending,
    /// approach SVG element opacity uses via <c>ApplyOpacity</c>): marking <paramref name="image"/>
    /// as an <b>isolated transparency group</b> (<c>/Group &lt;&lt; /S /Transparency /I true &gt;&gt;</c>)
    /// makes the PDF viewer flatten the tile's own (possibly overlapping) content into one RGBA
    /// result FIRST, at each shape's own local alpha, and only THEN apply the single <paramref name="opacity"/>
    /// value to that already-flattened result as it's composited onto the real page - so overlapping
    /// content inside the tile no longer double-darkens where it overlaps.
    /// </summary>
    /// <remarks>
    /// See the atomicity/coordinate-convention remarks on <see cref="DrawImageMasked"/> just above -
    /// the same reasoning applies here: the <c>gs</c> activating the alpha must be emitted inside the
    /// same <c>q ... cm ... Do ... Q</c> block that places <paramref name="image"/>, not separately.
    /// </remarks>
    internal void DrawImageWithOpacity(PdfForm image, Rect destRect, double opacity, string pdfBlendModeName = "Normal")
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();

        // Checked before any group/ExtGState object is built, so a PDF/A-1 rejection never leaves
        // partially-built objects registered in the document's resources/iref table. A non-Normal
        // blend mode is itself a transparency-group-requiring construct too (see SetBlendMode), but
        // opacity-less-than-1 already requires the same group, so one check covers both here.
        PdfATransparencyGuard.RequireAllowed(Owner, _page, "CSS/SVG opacity less than 1");

        // Must be an isolated transparency group so the tile's own (possibly overlapping) content is
        // flattened once before the constant alpha below is applied to the flattened result - see
        // the remarks above and on DrawImageMasked for why plain (non-group) content wouldn't work.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        // A single ExtGState carries both /ca+/CA (opacity) and /BM (mix-blend-mode) - the pair is
        // meant to composite as one operation against the destination, not as two separately-scoped
        // effects, so one gs/Do pairing is both simpler and correct.
        var extGState = Owner.ExtGStateTable.GetExtGState(opacity, pdfBlendModeName);
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        // The page is always laid out downwards, so there is only one placement to emit here.
        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/>, composited through
    /// <paramref name="matrix"/>'s channel-independent linear part via an ExtGState <c>/TR</c>
    /// transfer function - the mechanism behind channel-independent CSS <c>filter</c> functions
    /// (<c>brightness()</c>, <c>contrast()</c>, <c>invert()</c>) applied to a whole tile. See
    /// <see cref="ColorMatrix"/>'s remarks for the underlying PDF-spec research: a cross-channel
    /// matrix (<c>grayscale()</c>, <c>sepia()</c>, <c>saturate()</c>, <c>hue-rotate()</c>) cannot be
    /// expressed this way at all, so this throws for one rather than silently mis-rendering it.
    /// </summary>
    /// <remarks>
    /// Dual-rasterization check (this repo's own convention for anything touching graphics-state
    /// constructs - see <c>CLAUDE.md</c>'s Testing conventions) surfaced a real-world renderer gap
    /// worth knowing before debugging "it doesn't look filtered": a well-formed <c>invert()</c>
    /// matrix (a spec-correct <c>/TR</c>, verified structurally in
    /// <c>PdfGraphicsRendererColorMatrixAndMaskTests</c> and by hand against the raw PDF bytes)
    /// rendered correctly in PDFium (the red test rectangle came out cyan, as expected) but rendered
    /// completely UNCHANGED in MuPDF (still red) - MuPDF's rasterizer does not implement ExtGState
    /// <c>/TR</c> at all, silently treating it as absent rather than erroring. This is the mirror
    /// image of this codebase's usual soft-mask finding (MuPDF lenient, PDFium strict): here PDFium
    /// is the one actually honoring a legitimate, spec-conformant construct, and MuPDF is the one
    /// silently dropping it. Do not use a MuPDF-only render to conclude a channel-independent color
    /// matrix "does nothing" - check PDFium (or a real browser/Acrobat) too.
    /// </remarks>
    internal void DrawImageWithColorMatrix(PdfForm image, Rect destRect, ColorMatrix matrix)
    {
        if (!matrix.IsChannelIndependent)
        {
            throw new NotSupportedException(
                "This color matrix mixes color channels (e.g. grayscale()/sepia()/saturate()/hue-rotate() " +
                "or a feColorMatrix with off-diagonal coefficients), which a PDF ExtGState transfer " +
                "function (ISO 32000-1 §8.6.5.3) cannot express - a transfer function is evaluated " +
                "independently per color component and never sees another component's value. See " +
                "ColorMatrix's remarks for the mechanisms that do apply (a raster image's own DeviceN " +
                "tint-transform color space, or rasterizing the affected content) and why neither is a " +
                "drop-in replacement for compositing an already-rendered vector tile.");
        }

        if (Owner.Options.PortableColorFilters)
        {
            DrawImageWithBlendLayers(image, destRect, PdfColorMatrixBlendLayers.Build(matrix));
            return;
        }

        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();

        PdfATransparencyGuard.RequireAllowed(Owner, _page, "CSS/SVG color-matrix filter");

        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var transferFunction = PdfType4Function.BuildChannelIndependentTransferFunction(Owner, matrix);

        var extGState = new PdfExtGState(Owner);
        extGState.TransferFunction = transferFunction;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// The <see cref="PdfGenerateConfig.PortableColorFilters"/> form of <see cref="DrawImageWithColorMatrix"/>:
    /// the same colour math written as blend-mode fills instead of an ExtGState <c>/TR</c>, which MuPDF-based
    /// readers ignore. See <see cref="PdfColorMatrixBlendLayers"/> for how a matrix becomes layers.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A wrapper form (an isolated group) holds the content followed by one opaque full-tile fill per layer, and is
    /// placed through an <c>/Alpha</c> soft mask whose <c>/G</c> is the content form. The mask is what restores the
    /// content's shape - the fills cover the whole tile. Every fill is opaque on purpose: a fill with its own alpha
    /// would make the group's alpha, and so the mask applied on top of it, count the content's alpha twice. A fill
    /// that is not itself masked would otherwise be the one that fattens an antialiased edge (the union of two
    /// partial coverages is larger than either).
    /// </para>
    /// <para>
    /// Exact where the content is opaque, which the dual-renderer check against PDFium and MuPDF confirmed. Where it
    /// is only partly opaque (an <c>rgba()</c> fill, an antialiased edge) a blend mode's backdrop is not opaque, and the
    /// PDF blend formula lets the fill colour in with weight <c>1 - alpha</c>: the colour is off there, and the group
    /// cannot un-premultiply it. That is why this is opt-in rather than the default, which stays the
    /// alpha-independent <c>/TR</c>.
    /// </para>
    /// </remarks>
    private void DrawImageWithBlendLayers(PdfForm image, Rect destRect, IReadOnlyList<PdfBlendLayer> layers)
    {
        BeginPage();

        // Checked before any form/mask/ExtGState object is built, so a PDF/A-1 rejection never leaves
        // partially-built objects registered in the document. Checked for a matrix that changes nothing too,
        // so the option rejects exactly the documents the transfer-function form does.
        PdfATransparencyGuard.RequireAllowed(Owner, _page, "CSS/SVG color-matrix filter");

        if (layers.Count == 0)
        {
            DrawImage(image, destRect.X, destRect.Y, destRect.Width, destRect.Height);
            return;
        }

        const string format = PdfNumberFormats.FourDecimals;

        double width = image.PointWidth;
        double height = image.PointHeight;

        image.Finish();

        var wrapper = new PdfForm(Owner, new Size(width, height));
        var wrapperGraphics = PdfGraphics.FromForm(wrapper);
        wrapperGraphics.DrawImage(image, 0, 0, width, height);
        foreach (var layer in layers)
        {
            var state = wrapperGraphics.Save();
            wrapperGraphics.SetBlendMode(layer.BlendMode);
            wrapperGraphics.DrawRectangle(new SolidBrush(PaintColor.FromArgb(ToByte(layer.R), ToByte(layer.G), ToByte(layer.B))), 0, 0, width, height);
            wrapperGraphics.Restore(state);
        }

        // Finish() disposes the form's graphics, which writes its content stream.
        wrapper.Finish();

        // Both forms must be transparency groups or PDFium ignores the active SMask (see DrawImageMasked).
        wrapper.FormXObject.Elements["/Group"] = NewIsolatedGroup();
        image.FormXObject.Elements["/Group"] = NewIsolatedGroup();

        var softMask = new PdfSoftMask(Owner);
        softMask.Elements[PdfSoftMask.Keys.S] = new PdfName("/Alpha");
        softMask.Elements.SetReference(PdfSoftMask.Keys.G, image.FormXObject);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements["/SMask"] = softMask;
        var gsName = Resources.AddExtGState(extGState);

        string name = Realize(wrapper, destRect.Width, destRect.Height);

        double cx = destRect.Width / wrapper.PointWidth;
        double cy = destRect.Height / wrapper.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            destRect.X, destRect.Y + destRect.Height, cx, cy, name);
    }

    private static int ToByte(double component) => (int)Math.Round(Math.Clamp(component, 0, 1) * 255);

    private static PdfDictionary NewIsolatedGroup()
    {
        var group = new PdfDictionary();
        group.Elements["/S"] = new PdfName("/Transparency");
        group.Elements["/CS"] = new PdfName("/DeviceRGB");
        group.Elements["/I"] = new PdfBoolean(true);
        return group;
    }

    /// <summary>
    /// Draws <paramref name="image"/> at <paramref name="destRect"/> with <paramref name="maskImage"/>
    /// attached as an <c>/Alpha</c>-subtype soft mask, deriving mask values from the mask tile's
    /// computed alpha rather than a luminosity conversion of its color - see
    /// <see cref="Canvas.DrawImageAlphaMasked"/> for the motivating SVG
    /// <c>SourceAlpha</c>/<c>feComposite</c> use case.
    /// </summary>
    /// <remarks>
    /// Unlike <see cref="DrawImageMasked"/>'s <c>/Luminosity</c> mask, the mask form's own transparency
    /// group is NOT given a <c>/CS /DeviceGray</c> entry: ISO 32000-1 §11.6.5.2 only requires (and
    /// only consults) a group colour space when deriving a luminosity value from it - "If the
    /// subtype S is Luminosity, the group attributes dictionary shall contain a CS entry defining
    /// the colour space in which the compositing computation is to be performed" - and the BC
    /// (backdrop color) entry is likewise "consulted only if the subtype S is Luminosity". An Alpha
    /// mask's value is the group's own computed alpha, which has no colour space to speak of, so
    /// forcing DeviceGray here would be asserting a requirement the spec does not impose.
    /// </remarks>
    internal void DrawImageAlphaMasked(PdfForm image, PdfForm maskImage, Rect destRect, bool invert = false)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string name = Realize(image, width, height);

        BeginPage();
        image.Finish();
        maskImage.Finish();

        PdfATransparencyGuard.RequireAllowed(Owner, _page, "An SVG alpha mask (SourceAlpha/feComposite)");

        var maskPdfForm = maskImage.FormXObject;
        var maskGroup = new PdfDictionary();
        maskGroup.Elements["/S"] = new PdfName("/Transparency");
        maskGroup.Elements["/I"] = new PdfBoolean(true);
        maskGroup.Elements["/K"] = new PdfBoolean(false);
        maskPdfForm.Elements["/Group"] = maskGroup;

        // See DrawImageMasked's remarks: the masked CONTENT form must be its own transparency group
        // too, or stricter readers (PDFium/Acrobat) silently ignore the active SMask entirely.
        var contentPdfForm = image.FormXObject;
        var contentGroup = new PdfDictionary();
        contentGroup.Elements["/S"] = new PdfName("/Transparency");
        contentGroup.Elements["/CS"] = new PdfName("/DeviceRGB");
        contentGroup.Elements["/I"] = new PdfBoolean(true);
        contentPdfForm.Elements["/Group"] = contentGroup;

        var softMask = new PdfSoftMask(Owner);
        softMask.Elements[PdfSoftMask.Keys.S] = new PdfName("/Alpha");
        softMask.Elements.SetReference(PdfSoftMask.Keys.G, maskPdfForm);
        if (invert)
            softMask.TransferFunction = PdfType4Function.BuildInvertFunction(Owner);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements["/SMask"] = softMask;
        var gsName = Resources.AddExtGState(extGState);

        double cx = width / image.PointWidth;
        double cy = height / image.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, cx, cy, name);
    }

    /// <summary>
    /// Paints <paramref name="bottom"/> normally at <paramref name="destRect"/>, then
    /// <paramref name="top"/> on top of it at the same rect composited with <paramref name="pdfBlendModeName"/> -
    /// see <see cref="Canvas.DrawImageBlendedOver"/> for the motivating SVG
    /// <c>feBlend</c> use case. <paramref name="pdfBlendModeName"/> crosses from
    /// <c>PaintBlendMode</c> as a plain string (via <c>PaintBlendMode.ToString()</c> at the
    /// <c>PdfCanvas</c> call site) rather than as a typed enum, matching the same crossing
    /// point <see cref="SetBlendMode"/> already uses - The PDF engine sits below
    /// the HTML layers in this codebase's layering, so it cannot reference
    /// <c>PaintBlendMode</c> directly (see <see cref="SetBlendMode"/>).
    /// </summary>
    internal void DrawImageBlendedOver(PdfForm top, PdfForm bottom, Rect destRect, string pdfBlendModeName)
    {
        const string format = PdfNumberFormats.FourDecimals;

        double x = destRect.X;
        double y = destRect.Y;
        double width = destRect.Width;
        double height = destRect.Height;

        string topName = Realize(top, width, height);
        string bottomName = Realize(bottom, width, height);

        BeginPage();
        top.Finish();
        bottom.Finish();

        // A non-Normal /BM is the transparency-group-requiring construct PDF/A-1 forbids (see
        // SetBlendMode's own remarks) - a Normal-mode feBlend is just two ordinary Form XObject
        // placements and needs no such check, matching SetBlendMode's identical carve-out.
        if (pdfBlendModeName is not ("Normal" or "Compatible"))
            PdfATransparencyGuard.RequireAllowed(Owner, _page, $"An SVG feBlend ({pdfBlendModeName})");

        double cx = width / bottom.PointWidth;
        double cy = height / bottom.PointHeight;

        if (cx == 0 || cy == 0)
            return;

        // bottom paints normally; top's own placement carries the blend-mode gs, scoped to just
        // that Do by the surrounding q...Q (same atomicity reasoning as DrawImageMasked/
        // DrawImageWithOpacity above - the gs activating a non-default graphics-state parameter must
        // share the q...Q block of the Do it's meant to affect, not rely on ambient state).
        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm {4} Do Q\n",
            x, y + height, cx, cy, bottomName);

        var extGState = new PdfExtGState(Owner);
        extGState.Elements.SetName(PdfExtGState.Keys.BM, pdfBlendModeName);
        var gsName = Resources.AddExtGState(extGState);

        double tcx = width / top.PointWidth;
        double tcy = height / top.PointHeight;

        if (tcx == 0 || tcy == 0)
            return;

        AppendFormatImage("q {2:" + format + "} 0 0 {3:" + format + "} {0:" + format + "} {1:" + format + "} cm " + gsName + " gs {4} Do Q\n",
            x, y + height, tcx, tcy, topName);
    }

    /// <summary>
    /// Gets the resource name of the specified image within this page or form.
    /// </summary>
    internal string GetImageName(PdfRasterImage image, double width, double height)
    {
        if (_page is not null)
            return _page.GetImageName(image, width, height);
        return _form.GetImageName(image, width, height);
    }

    /// <summary>
    /// Gets the resource name of the specified form within this page or form.
    /// </summary>
    internal string GetFormName(PdfForm form)
    {
        if (_page is not null)
            return _page.GetFormName(form);
        return _form.GetFormName(form);
    }
}
