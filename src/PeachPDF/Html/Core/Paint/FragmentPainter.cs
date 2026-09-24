using PeachPDF.CSS;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Fragments;
using PeachPDF.Html.Core.Handlers;
using PeachPDF.Html.Core.Paint.Content;
using PeachPDF.Html.Core.Utils;
using System;
using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.Html.Core.Paint
{
    /// <summary>
    /// The paint phase. Consumes one <see cref="FragmentainerFragment"/> — one page of the immutable
    /// fragment tree layout produced — and draws it to an <see cref="RGraphics"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <b>All geometry comes from fragments.</b> A <see cref="CssBox"/> reached through
    /// <see cref="BoxFragment.Box"/> supplies computed style and nothing else; fragment rectangles are
    /// already fragmentainer-local, so there is no page offset to apply anywhere in here.
    /// </para>
    /// <para>
    /// One painter instance paints one page. Per-page state — which fragments have already been drawn
    /// — is the painter's own, so nothing about painting is recorded on the box tree or the container.
    /// Replaced elements (images, inline SVG, form fields), list markers and repeated table headers
    /// are painted by an <see cref="IFragmentContentPainter"/> selected from the box's type; see
    /// <see cref="FragmentContentPainters"/>. A box whose content the generic paint CAN express has
    /// no painter — an <c>&lt;hr&gt;</c>, whose rule is simply its own border, is the example to
    /// reach for before adding one.
    /// </para>
    /// </remarks>
    internal sealed partial class FragmentPainter(HtmlContainerInt container)
    {
        /// <summary>
        /// Tolerance for treating a clip intersection as empty. A rectangle that merely touches the
        /// clip edge - a box relocated to the next page's content top sits exactly flush against the
        /// previous page's clip bottom - is not actually visible, and floating-point rounding across
        /// the several steps such a box's Y goes through can land either side of exact zero.
        /// </summary>
        private const double VisibilityClipEpsilon = 1e-6;

        /// <summary>
        /// Fragments already painted on this page. A fragment can be reached twice in one page walk —
        /// once nested, once hoisted out for stacking-context ordering — and must only paint the first
        /// time. Reference identity is deliberate: two structurally equal fragments (a rowspan cell
        /// shown in each row it spans) are genuinely separate things to paint.
        /// </summary>
        private readonly HashSet<BoxFragment> _painted = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Lines whose <c>text-overflow: ellipsis</c> truncation has already been painted this page. A
        /// line box is shared by every sibling <see cref="CssBox"/> that contributes words to it (plain
        /// text next to a <c>&lt;b&gt;</c>/<c>&lt;span&gt;</c>/inline image run) - <see cref="PaintWords"/>
        /// is called once per box, not once per line, so without this a second box past the truncation
        /// point would independently rediscover "my own words don't fit" and draw its own extra ellipsis.
        /// Cleared per page implicitly: a new <see cref="FragmentPainter"/> instance paints each page (see
        /// <c>FragmentPaintHarness.PaintPage</c>/production's own per-page construction).
        /// </summary>
        private readonly HashSet<CssLineBox> _linesAlreadyTruncated = new(ReferenceEqualityComparer.Instance);

        /// <summary>
        /// Paints one page: the fragmentainer's whole fragment subtree, clipped to the page's content
        /// window plus the room an outline on this page needs to spill into the page margin.
        /// </summary>
        /// <remarks>
        /// This is the only page-level clip <c>RGraphics.PushClip</c> pushes for page content - and that
        /// is exactly what lets a <c>position: fixed</c> box's own paint reach its spec-correct containing block (the
        /// page box, margins included, per CSS2.1 §10.1): <c>RGraphics.SuspendClipping</c> pops the clip
        /// stack down to exactly one entry, which is always the unconditionally-infinite clip
        /// <see cref="PeachPDF.Adapters.GraphicsAdapter"/>'s constructor pushes before this method ever
        /// runs - not "one level below whatever is on top" - so a single push here is already as far out
        /// as suspension can ever reach; a second, wider clip pushed above it would be popped by the same
        /// loop and change nothing. What DOES need to be single-level is that this push go through
        /// <c>RGraphics.PushClip</c> at all: an earlier version of <see cref="PdfGenerator"/> intersected
        /// the equivalent window directly on the raw <c>XGraphics</c> object, ahead of and invisible to
        /// this abstraction's own clip stack - <c>SuspendClipping</c> could never undo that one, so a fixed
        /// box's own geometry inside the page's margins was silently discarded on every page. See issue
        /// #880.
        /// </remarks>
        internal void Paint(RGraphics g, FragmentainerFragment fragmentainer)
        {
            var pageClip = container.PageClipOverride ?? container.PageBoxRect;
            var outlineReach = MaximumOutlineReach(g, fragmentainer.Root);
            if (outlineReach > 0)
            {
                // The page's ordinary content clip starts at the content-area edge. An outline is
                // explicitly allowed to paint outside the border box and therefore into the page
                // margin; clipping it to the ordinary window reduces a declared multi-point band to
                // an antialiased hairline when the element starts at that edge. Widen only the
                // page-level clip here. Any ancestor/own overflow clips pushed below still constrain
                // the outline normally.
                pageClip = RRect.FromLTRB(
                    pageClip.Left - outlineReach,
                    pageClip.Top - outlineReach,
                    pageClip.Right + outlineReach,
                    pageClip.Bottom + outlineReach);
            }

            g.PushClip(pageClip);

            _pageRoot = fragmentainer.Root;
            PaintFragment(g, fragmentainer.Root);

            g.PopClip();
        }

        private static double MaximumOutlineReach(RGraphics g, BoxFragment fragment)
        {
            var maximum = fragment.Lines.Count > 0
                ? OutlineDrawHandler.OutwardReach(g, fragment.Box)
                : 0;

            foreach (var child in fragment.Children)
                maximum = Math.Max(maximum, MaximumOutlineReach(g, child));

            return maximum;
        }

        /// <summary>
        /// Paints one box fragment — the portion of a box that lives in a single fragmentainer (CSS
        /// Fragmentation Level 3 §2) — establishing the whole-element effects (<c>transform</c>,
        /// <c>clip-path</c>, <c>opacity</c>) around it.
        /// </summary>
        internal void PaintFragment(RGraphics g, BoxFragment fragment)
        {
            var box = fragment.Box;

#if DEBUG
            Console.WriteLine($"paint: {box}");
#endif

            // A backdrop repaint (see FragmentPainter.Backdrop.cs) ends where the element it is repainting for begins.
            if (_stopped)
                return;

            if (ReferenceEquals(fragment, _stopAt))
            {
                _stopped = true;
                return;
            }

            // Another plane of the 3D rendering context being painted: the context composes it, this plane's own painting must not.
            if (_contextMembers is not null && _contextMembers.Contains(fragment))
                return;

            try
            {
                if (box.DerivedStyle.ActualDisplay == Keywords.None || box.Visibility.Value != Visibility.Visible) return;

                // use initial clip to draw blocks with Position = fixed. I.e. ignore page margins
                if (box.Position.Value == PositionMode.Fixed)
                {
                    g.SuspendClipping();
                }

                // A fragment only exists where its box has something in this fragmentainer, so the
                // cheap early-out is now just "is any of it inside the current clip" - the clip being
                // narrower than the page band whenever an ancestor's overflow or a clip-path is active.
                // A fragment that carries only descendants (no decoration rectangles of its own) must
                // always be entered: a hoisted out-of-flow descendant can paint well outside this
                // fragment's own rectangle and is only reached through this call.
                var visible = fragment.Lines.Count == 0 || IsAnyRectVisible(fragment, g.GetClip());

                // A 3D rendering context (transform-style: preserve-3d) is composed as a whole, planes depth-tested against each other.
                var paintedAsContext = visible && !_textOnly && TryPaintContext3D(g, fragment);

                if (visible && !paintedAsContext)
                {
                    // A transform that is not affine once projected onto the element's plane (perspective, or a 3D transform under a
                    // parent's perspective) cannot be a PDF `cm`: the element is painted untransformed into a bitmap and warped.
                    var projective = _textOnly ? null : ResolveProjective(fragment);
                    var transformed = box.IsTransformed && projective is null;

                    if (transformed)
                    {
                        // ActualTransformMatrix is cached treating the box's own top-left as local
                        // (0, 0), so re-anchor the pivot to where the box sits on this page. The whole
                        // (unfragmented) border box is deliberately used rather than this fragment's
                        // own rectangle: per-fragment transform origins are a separate change.
                        g.PushTransform(box.ActualTransformMatrix.RebaseOrigin(fragment.WholeBoxRect.X, fragment.WholeBoxRect.Y));
                    }

                    if (projective is { Affine: { } affine })
                    {
                        // Affine once the parent's perspective is in: a plain transform after all (a plane brought nearer is just larger).
                        g.PushTransform(affine);
                        PaintClippedWithEffects(g, fragment);
                        g.PopTransform();
                    }
                    else if (projective is { } warp)
                    {
                        if (!PaintProjective(g, fragment, warp))
                        {
                            // No raster context (a measure-only pass): the affine linearisation of the transform is the best that is left.
                            g.PushTransform(box.ActualTransformMatrix.RebaseOrigin(fragment.WholeBoxRect.X, fragment.WholeBoxRect.Y));
                            PaintClippedWithEffects(g, fragment);
                            g.PopTransform();
                        }
                    }
                    else
                    {
                        PaintClippedWithEffects(g, fragment);
                    }

                    if (transformed)
                        g.PopTransform();
                }

                // Restore clips
                if (box.Position.Value == PositionMode.Fixed)
                {
                    g.ResumeClipping();
                }
            }
            catch (PeachPDF.PdfSharpCore.Pdf.Advanced.PdfConformanceException)
            {
                // A PDF/A or PDF/X conformance rejection (the document uses a feature/color that the
                // requested conformance level forbids) is a deliberate validation failure, not an
                // unexpected paint error - let it propagate as the InvalidOperationException it is,
                // rather than folding it into a generic HtmlRenderException the way the catch below would.
                throw;
            }
            catch (Exception ex)
            {
                if (box.HtmlContainer is { } container)
                    throw container.RenderError(HtmlRenderErrorType.Paint, "Exception in box paint", ex);
            }
            finally
            {
                // A flatten repaint ends when the box it is repainting for has been painted.
                if (ReferenceEquals(fragment, _stopAfter))
                    _stopped = true;
            }
        }

        /// <summary>
        /// Paints one fragment inside its own clips - <c>clip-path</c>, the legacy <c>clip</c> - and with its group effects (opacity, blend
        /// mode, filter): everything an element does between "its transform is in place" and "its transform is undone".
        /// </summary>
        private void PaintClippedWithEffects(RGraphics g, BoxFragment fragment)
        {
            var box = fragment.Box;

            // clip-path clips the entire element rendering (background, border, content, children).
            // It is established inside the transform push so the clip and the content it clips are
            // transformed together (CSS Masking 1: the clip is in the element's local coordinate
            // system, which any `transform` then maps). The whole (unfragmented) border box is
            // passed for the same reason as the transform pivot above; CssClipPathResolver itself
            // resolves the actual reference box from there, honoring an optional `<geometry-box>`
            // keyword in the value (border-box is only the default when one isn't present).
            var clipped = false;
            if (!_textOnly && box.ClipPath != Keywords.None && !string.IsNullOrEmpty(box.ClipPath))
            {
                if (CssClipPathResolver.TryBuildClipPath(g, box.ClipPath, fragment.WholeBoxRect, box, out var clipGeometry, out _)
                    && clipGeometry is not null)
                {
                    g.PushClip(clipGeometry);
                    clipped = true;
                }
            }

            // Legacy CSS 2.1 `clip` (§11.1.2): "Applies to: absolutely positioned elements" - no
            // independent `overflow` requirement (confirmed against the spec text directly), so
            // this is gated on `position` alone, matching real browsers too. Order relative to the
            // `clip-path` push above is not load-bearing - clip-stack intersection is commutative -
            // this is appended after purely for readability alongside the existing block.
            var legacyClipPushed = 0;
            var isAbsolutelyPositioned = box.Position.Value is PositionMode.Absolute or PositionMode.Fixed;
            if (!_textOnly && isAbsolutelyPositioned && box.Clip != Keywords.Auto && !string.IsNullOrEmpty(box.Clip))
            {
                if (CssClipRectResolver.TryBuildClipRect(box.Clip, fragment.WholeBoxRect, box, out var clipRect))
                {
                    legacyClipPushed = RenderUtils.ClipGraphicsByOverflow(g, clipRect, null);
                }
            }

            // The fast (untiled) path only applies when nothing needs group compositing: full
            // opacity, a normal blend mode, and no filter function that actually changes the
            // result (a filter list of only documented no-ops - grayscale()/hue-rotate()/etc. -
            // takes this path too, same as `filter: blur()` always has).
            var filter = FilterEffectResolver.Resolve(box.ActualFilterFunctions);
            if (_textOnly)
            {
                // The visible content was drawn already; this pass only supplies its text, so no effect applies.
                PaintTagged(g, fragment);
            }
            else if (TryFlatten(g, fragment, filter))
            {
                // Needs transparency the document forbids: painted as an opaque bitmap in its place.
            }
            else if ((filter.RequiresRaster || (g.PrefersRasterGroups && NeedsGroupCompositing(box, filter))) &&
                     PaintRasterized(g, fragment, filter))
            {
                // Rendered to a bitmap, filtered and drawn (blur, grayscale, ...): nothing left to paint.
            }
            else if (box.IsOpaque && box.ActualMixBlendMode == BlendMode.Normal &&
                filter is { OpacityMultiplier: >= 1.0, HasColorMatrix: false })
            {
                PaintTagged(g, fragment);
            }
            else
            {
                PaintWithOpacity(g, fragment, filter);
            }

            for (var i = 0; i < legacyClipPushed; i++)
                g.PopClip();

            if (clipped)
                g.PopClip();

        }

        /// <summary>
        /// Whether the element needs group compositing - opacity, a blend mode or a colour function - which a graphics that
        /// composites bitmaps directly (<see cref="RGraphics.PrefersRasterGroups"/>) does in a tight bitmap of the element.
        /// </summary>
        private static bool NeedsGroupCompositing(CssBox box, FilterEffectResolver.Resolved filter) =>
            !box.IsOpaque || box.ActualMixBlendMode != BlendMode.Normal || filter.OpacityMultiplier < 1.0 || filter.HasColorMatrix;

        /// <summary>
        /// Whether any of <paramref name="fragment"/>'s own decoration rectangles survives
        /// <paramref name="clip"/>. Checked per rectangle rather than against their union, so a
        /// fragment whose rectangles are far apart on the line cannot be kept alive by empty space
        /// between them.
        /// </summary>
        private static bool IsAnyRectVisible(BoxFragment fragment, RRect clip)
        {
            foreach (var line in fragment.Lines)
            {
                if (IsRectVisible(line.Rect, clip)) return true;
            }

            return false;
        }

        /// <summary>
        /// Paints a fragment (and, via <see cref="PaintTagged"/>, its whole subtree) into an offscreen
        /// tile sized to the current page's visible clip, then composites that tile onto
        /// <paramref name="g"/> as a single flattened result at <c>ActualOpacity</c> (further scaled by
        /// <paramref name="filter"/>'s own <c>filter: opacity()</c> contribution), through
        /// <c>ActualMixBlendMode</c>, and - when <paramref name="filter"/> carries a channel-independent
        /// color matrix (<c>brightness()</c>/<c>contrast()</c>/<c>invert()</c>) - recolored first via a
        /// second tile pass. The CSS <c>opacity</c> property is a group effect (it applies once to the
        /// element and everything painted inside it, not to each descendant's own paint calls
        /// independently), and this is what makes overlapping content within the box composite correctly
        /// instead of double-blending where it overlaps.
        /// </summary>
        /// <remarks>
        /// The tile is sized to the whole current page-visible rect (not a tight bounding box of this
        /// box's own content) so that the subtree can keep painting at its normal page coordinates
        /// unmodified - the box's own possible multiple decoration rectangles (line wraps) and any
        /// overflowing/absolutely-positioned descendants all land correctly inside the tile with no
        /// extra translation math, at the cost of a somewhat larger-than-necessary Form XObject. Any
        /// transform already pushed onto <paramref name="g"/> for this same box (see the caller in
        /// <see cref="PaintFragment"/>) is left active and applies to the tile's placement
        /// automatically, since PDF's own <c>cm</c> operator concatenates - no separate
        /// transform-folding is needed here.
        /// </remarks>
        private void PaintWithOpacity(RGraphics g, BoxFragment fragment, FilterEffectResolver.Resolved filter)
        {
            var clip = g.GetClip();
            var tileRect = new RRect(0, 0, clip.Right, clip.Bottom);

            var tile = g.CreateTile(tileRect.Width, tileRect.Height);
            if (tile is not { } t)
            {
                // No page/document context to own a Form XObject in (e.g. a measure-only pass) -
                // opacity/blend-mode/filter have no visual effect there anyway, so just paint directly.
                PaintTagged(g, fragment);
                return;
            }

            t.Graphics.PushClip(clip);
            PaintTagged(t.Graphics, fragment);
            t.Graphics.Dispose();

            var image = t.Image;

            // The color matrix is a separate ExtGState (/TR) from opacity/blend-mode's (/ca, /BM), so a
            // filter list needing both goes through a second tile - correctness first, per this repo's own
            // "don't over-optimize call count in this pass" note; most boxes need at most one of the two.
            if (filter.HasColorMatrix)
            {
                var matrixTile = g.CreateTile(tileRect.Width, tileRect.Height);
                if (matrixTile is { } mt)
                {
                    mt.Graphics.DrawImageWithColorMatrix(image, tileRect, filter.ColorMatrix);
                    mt.Graphics.Dispose();
                    image = mt.Image;
                }
            }

            var opacity = fragment.Box.ActualOpacity * filter.OpacityMultiplier;
            g.DrawImageWithOpacity(image, tileRect, opacity, ToRBlendMode(fragment.Box.ActualMixBlendMode));
        }

        /// <summary>
        /// Maps the CSS-namespace <see cref="BlendMode"/> (the enum-keyword source generator's
        /// <c>enumType</c> codegen hardcodes <c>PeachPDF.CSS</c> as its namespace, so <c>mix-blend-mode</c>
        /// can't bind directly to <see cref="RBlendMode"/> despite the two enums being identical in shape)
        /// onto the <see cref="RBlendMode"/> the PDF-writing layer actually understands. The one call site
        /// that needs this conversion, per CLAUDE.md's guidance to map at paint time rather than duplicate
        /// <c>RBlendMode</c> a second time under a different name.
        /// </summary>
        private static RBlendMode ToRBlendMode(BlendMode mode) => mode switch
        {
            BlendMode.Normal => RBlendMode.Normal,
            BlendMode.Multiply => RBlendMode.Multiply,
            BlendMode.Screen => RBlendMode.Screen,
            BlendMode.Overlay => RBlendMode.Overlay,
            BlendMode.Darken => RBlendMode.Darken,
            BlendMode.Lighten => RBlendMode.Lighten,
            BlendMode.ColorDodge => RBlendMode.ColorDodge,
            BlendMode.ColorBurn => RBlendMode.ColorBurn,
            BlendMode.HardLight => RBlendMode.HardLight,
            BlendMode.SoftLight => RBlendMode.SoftLight,
            BlendMode.Difference => RBlendMode.Difference,
            BlendMode.Exclusion => RBlendMode.Exclusion,
            BlendMode.Hue => RBlendMode.Hue,
            BlendMode.Saturation => RBlendMode.Saturation,
            BlendMode.Color => RBlendMode.Color,
            BlendMode.Luminosity => RBlendMode.Luminosity,
            _ => RBlendMode.Normal,
        };

        /// <summary>
        /// Paints one fragment's content, wrapped in tagged-PDF structure-tree/marked-content
        /// bookkeeping when tagging is enabled (<c>HtmlContainer.StructureTagBuilder</c> is non-null).
        /// This is the single choke point all tagging flows through, mirroring how
        /// <see cref="PaintFragment"/> already wraps the same content for transform/opacity handling.
        /// When tagging is disabled this adds one null check and otherwise behaves exactly as calling
        /// <see cref="PaintContent"/> directly would.
        /// </summary>
        private void PaintTagged(RGraphics g, BoxFragment fragment)
        {
            // Mirrors PaintBoxContent's own early-out: skip classification/tagging entirely for a
            // fragment that has already been painted on this page.
            if (!_painted.Add(fragment))
                return;

            var box = fragment.Box;
            // The bitmap pass creates no structure elements: the overlay pass that follows it tags the same boxes (see
            // PaintSelectableText), and two sets of elements for one box would be wrong.
            var builder = _taggingSuppressed ? null : box.HtmlContainer?.StructureTagBuilder;

            // Outside the structure element this box opens below, so a scope's outlines - which belong
            // to many boxes, most of them already closed - are never drawn into one box's marked content.
            var outerOutlineScope = OpenOutlineScope(fragment);
            var completed = false;

            try
            {
                PaintTaggedContent(g, fragment, builder);
                completed = true;
            }
            finally
            {
                // PaintFragment can swallow a paint error and carry on with the next sibling; the
                // enclosing scope must get its own collector back either way, but a failed paint's
                // outlines are not drawn.
                CloseOutlineScope(g, outerOutlineScope, builder, draw: completed);
            }
        }

        /// <summary>
        /// <see cref="PaintTagged"/>'s structure-tree bookkeeping around one fragment's content.
        /// </summary>
        private void PaintTaggedContent(RGraphics g, BoxFragment fragment, StructureTagBuilder? builder)
        {
            var box = fragment.Box;
            if (builder == null)
            {
                PaintContent(g, fragment);
                return;
            }

            // The display: contents elements this box was lifted out of still have structure elements of
            // their own, and this box's must nest inside them.
            using var phantomAncestors = box.DisplayContentsAncestors is { Count: > 0 } lifted
                ? builder.OpenPhantomAncestors(lifted)
                : null;

            var classification = StructureTagMapper.Classify(box);
            switch (classification.Kind)
            {
                case StructureTagKind.Artifact:
                    using (builder.OpenArtifact(g))
                        PaintContent(g, fragment);
                    break;

                case StructureTagKind.Grouping when classification.StructureType == Keywords.Li:
                    PaintListItem(g, fragment, builder);
                    break;

                case StructureTagKind.Grouping:
                    using (builder.OpenGroupingElement(box, classification.StructureType!))
                        PaintContent(g, fragment);
                    break;

                case StructureTagKind.Content:
                    using (builder.OpenContentElement(g, box, classification.StructureType!, classification.AltText))
                    {
                        // Attach the original MathML source as a PDF 2.0 Associated File on this
                        // formula's own structure element - see StructureTagBuilder.AttachMathMlSource
                        // and the -peachpdf-pdf-tag-type: Formula mapping in docs/html-css-support.md.
                        // Gated on the box actually resolving to a tagged content element (not just
                        // "is a CssBoxMath") - an author who suppresses tagging entirely
                        // (-peachpdf-pdf-tag-type: none, StructureTagKind.None) never reaches this
                        // case at all. An author who retargets the type to something other than
                        // Formula still gets the source attached to whatever element they chose - the
                        // source is still accurate supplementary content for it either way.
                        if (box is CssBoxMath mathBox && mathBox.SerializedSource is { } mathMl)
                            builder.AttachMathMlSource(box, mathMl);

                        PaintContent(g, fragment);
                    }
                    break;

                default:
                    PaintContent(g, fragment);
                    break;
            }
        }

        /// <summary>
        /// Paints a tagged &lt;li&gt;'s marker and body as separate sibling structure elements under
        /// "/LI" - "/Lbl" (the marker, via the synthesized <c>::marker</c> child from
        /// <see cref="CssBox.IsMarkerPseudoElement"/>, defaulting to "Lbl" per the UA stylesheet's
        /// <c>li::marker</c> rule, or suppressed if an author set
        /// <c>li::marker { -peachpdf-pdf-tag-type: none }</c>) and "/LBody" (everything else - not
        /// itself a separate CSS signal, inferred as "whatever isn't the marker" per the design
        /// decision this implements). The struct element for "/Lbl" is opened, and its own content
        /// painted, before "/LBody"'s so the two land in that order under "/LI"'s "/K" (correct
        /// reading order) - the marker's own on-page paint position is unaffected by this call-order
        /// swap, since it's driven entirely by pre-computed layout coordinates, not paint order.
        /// </summary>
        private void PaintListItem(RGraphics g, BoxFragment fragment, StructureTagBuilder builder)
        {
            using (builder.OpenGroupingElement(fragment.Box, Keywords.Li))
            {
                var markerFragment = FindMarkerFragment(fragment);
                if (markerFragment is not null)
                {
                    var markerBox = markerFragment.Box;
                    var markerClassification = StructureTagMapper.Classify(markerBox);
                    if (markerClassification.Kind == StructureTagKind.None)
                    {
                        PaintContent(g, markerFragment);
                    }
                    else
                    {
                        using (builder.OpenContentElement(g, markerBox, markerClassification.StructureType ?? Keywords.Lbl))
                            PaintContent(g, markerFragment);
                    }
                }

                using (builder.OpenListItemBodyElement(fragment.Box))
                    PaintBoxContent(g, fragment, paintMarkers: false);
            }
        }

        /// <summary>
        /// This fragment's <c>::marker</c> child fragment, if the marker appears on this page at all.
        /// </summary>
        private static BoxFragment? FindMarkerFragment(BoxFragment fragment)
        {
            foreach (var child in fragment.Children)
            {
                if (child.Box.IsMarkerPseudoElement) return child;
            }

            return null;
        }

        /// <summary>
        /// Paints a fragment's own content, routed to the box type's
        /// <see cref="IFragmentContentPainter"/> if it has one and to the generic box paint
        /// (<see cref="PaintBoxContent"/>) otherwise. This is the type-dispatch seam that
        /// replaced-element subclasses used to provide by overriding a virtual paint method on the box.
        /// </summary>
        internal void PaintContent(RGraphics g, BoxFragment fragment)
        {
            if (FragmentContentPainters.For(fragment.Box) is { } contentPainter)
            {
                // Replaced content (images, SVG, MathML, form fields) has no text this pass can supply.
                if (_textOnly && contentPainter is not MarkerFragmentPainter)
                    return;

                contentPainter.Paint(this, g, fragment);
            }
            else
                PaintBoxContent(g, fragment);
        }

        /// <summary>
        /// The generic box paint: this fragment's own decoration rectangles (shadows, background,
        /// borders), its words, its text decoration, then its children in CSS 2.1 Appendix E order.
        /// </summary>
        /// <param name="g">the device to draw to</param>
        /// <param name="fragment">the fragment being painted, the source of all geometry</param>
        /// <param name="paintMarkers">
        /// false only on the tagged-PDF &lt;li&gt; path, which paints the marker itself as a separate
        /// sibling structure element ("/Lbl") ahead of the list item's body ("/LBody").
        /// </param>
        internal void PaintBoxContent(RGraphics g, BoxFragment fragment, bool paintMarkers = true)
        {
            var box = fragment.Box;

            if (box.DerivedStyle.ActualDisplay == Keywords.None ||
                (box.DerivedStyle.ActualDisplay == Keywords.TableCell && box.EmptyCells == Keywords.Hide && box.IsSpaceOrEmpty)) return;

            var clipsPushed = RenderUtils.ClipGraphicsByOverflow(g, fragment.OverflowClip, fragment.OverflowClipCurve);
            var overflowClipRecorded = PushOverflowClip(fragment);

            // This fragment's own decoration rectangles - one per line box it spans on this page, or a
            // single whole-border-box rectangle for a block-level box. Already fragmentainer-local.
            var lines = fragment.Lines;
            var clip = g.GetClip();

            // Outline is drawn last - CSS Basic User Interface 4 §3.1: "the outline ... is drawn 'over' a
            // box, i.e., the outline is always on top" of that box's own content. So the per-line
            // geometry this box's outline needs is captured here, alongside border's own (which paints
            // immediately, unlike outline), and handed on once this box's text/decorations/descendants
            // have all painted, below - to be drawn later still, once the enclosing outline scope has
            // painted everything else (see FragmentPainter.Outlines.cs).
            List<OutlineRect>? outlinePaints = null;

            // A text-only pass paints no shadows, backgrounds, borders or outlines.
            for (var i = 0; !_textOnly && i < lines.Count; i++)
            {
                var actualRect = lines[i].Rect;

                if (!IsRectVisible(actualRect, clip)) continue;

                // Where this rectangle's decorations resolve, per box-decoration-break (css-break-3 §6.2).
                // Culling above stays on the rectangle itself: the decoration area may be the whole
                // unbroken box, which is far wider than what is visible here.
                var geometry = BoxDecorationGeometry.For(box, lines[i]);

                // Outset (drop) shadows paint BEFORE the background so they sit behind the box
                // (CSS Backgrounds & Borders 3 §5). box-shadow lives in the same style area
                // AdoptBorderAndBackgroundFrom copies onto a table's grid decoration box (issue #721),
                // so it's suppressed on this box alongside the border stroke itself, not on its own
                // flag - painting it here too would double the shadow (once at this box's own,
                // caption-inclusive rect, once at the decoration box's grid-only one).
                if (box.ActualBackdropFilterFunctions.Count > 0)
                    PaintBackdropFilter(g, fragment, geometry);

                if (!box.SuppressOwnBorderPaint && BoxDecorationGeometry.HasBoxShadow(box))
                    PaintBoxShadows(g, box, geometry, inset: false);

                // filter: drop-shadow() paints at the same point box-shadow's own outset layers do -
                // behind the box's background (CSS Backgrounds & Borders 3 §5's ordering, which this
                // approximation borrows wholesale).
                if (box.ActualFilterFunctions.Count > 0)
                    PaintFilterDropShadows(g, box, geometry);

                // A box whose background was "promoted" to fill the whole page canvas (see
                // HtmlContainerInt.ResolveCanvasBackground / PaintCanvasBackground) already had it
                // painted for every page - skip this box's own normal paint pass so it isn't painted
                // twice.
                if (!box.SuppressOwnBackgroundPaint)
                {
                    // A sliced background is painted over the whole unbroken box, so this clip is what
                    // keeps it out of the space beside this rectangle - which belongs to other content.
                    if (geometry.NeedsClip) g.PushClip(geometry.ClipRect);

                    PaintBackground(g, box, geometry, GetFirstLineStyleForRect(lines[i].Line), fragment);

                    if (geometry.NeedsClip) g.PopClip();
                }

                // Inset shadows paint AFTER (over) the background, clipped to the padding box. See the
                // outset call above for why this is gated on SuppressOwnBorderPaint too.
                if (!box.SuppressOwnBorderPaint && BoxDecorationGeometry.HasBoxShadow(box))
                    PaintBoxShadows(g, box, geometry, inset: true);

                // For multi-page tables, draw the outer bottom border at the page-break Y on
                // intermediate pages (instead of at the rectangle's bottom, which is off-page).
                // The page clip already constrains the side-border top to MarginTop. Keyed purely on
                // PageBreakBottoms rather than also requiring a Table/InlineTable display - only
                // CssLayoutEngineTable ever sets it, on the table box itself and (for a captioned
                // table, see CssBox.TableGridDecorationBox) on its grid decoration box, whose own
                // display is deliberately not "table" (issue #721).
                var rectForBorders = geometry.DecorationRect;
                if (box.PageBreakBottoms != null && box.HtmlContainer != null)
                {
                    // PageBreakBottoms is keyed by pagination slot, which is exactly what the fragment
                    // records - no re-deriving the slot from a scroll offset.
                    if (box.PageBreakBottoms.TryGetValue(fragment.FragmentainerIndex, out var pageBreakBottom))
                    {
                        var pageBreakBottomVisual = pageBreakBottom - fragment.OriginY;
                        if (pageBreakBottomVisual < rectForBorders.Bottom)
                        {
                            rectForBorders = new RRect(
                                rectForBorders.Left,
                                rectForBorders.Top,
                                rectForBorders.Width,
                                pageBreakBottomVisual - rectForBorders.Top);
                        }
                    }
                }

                if (geometry.NeedsClip) g.PushClip(geometry.ClipRect);

                // A captioned table's own border is suppressed - its grid decoration box paints the
                // border instead, at the grid's own rect (CssBox.SuppressOwnBorderPaint, issue #721).
                if (!box.SuppressOwnBorderPaint)
                {
                    // border-image (CSS Backgrounds & Borders 3 §13) replaces the ordinary border-style
                    // stroke entirely once its source resolves to a real, loaded image - falls back to
                    // BordersDrawHandler whenever there is no border-image-source, or its image failed to
                    // load (TryDrawBorderImage returns false either way, same as border-image not being
                    // declared at all).
                    if (!BorderImageDrawHandler.TryDrawBorderImage(g, box, rectForBorders))
                    {
                        BordersDrawHandler.DrawBoxBorders(g, box, rectForBorders,
                            geometry.HasLeftEdge, geometry.HasRightEdge, geometry.HasTopEdge, geometry.HasBottomEdge);
                    }
                }

                if (geometry.NeedsClip) g.PopClip();

                // Unlike backgrounds and borders, an outline spills outside the rectangle it is
                // built from. Building it from the unbroken strip and then clipping it to this line
                // makes the path and clip disagree by exactly that spill: the real outside edge is
                // clipped away while an edge inside the slice can remain visible. Resolve the
                // outline against the slice itself instead. The physical-edge flags below decide
                // which of this rectangle's edges are real ones to expand outward from.
                var outlineRect = geometry.NeedsClip ? geometry.ClipRect : rectForBorders;

                // CSS Basic User Interface 4 §3.1 recommends a fragmented outline be a fully connected
                // shape rather than one left open at every wrap - unlike border/background, which
                // box-decoration-break's `slice` (css-break-3 §6.2, which does not govern outline at
                // all) deliberately keeps those same inline-axis edges open, matching how a wrapped
                // inline actually looks in every UA. Close them for the outline only - geometry's own
                // HasLeftEdge/HasRightEdge above already painted the border/background with the real,
                // open flags, so this doesn't touch that. Closing each rectangle is also what lets the
                // deferred pass below union them: a fragment contributes its whole rectangle to that
                // region, not a three-sided piece of one. Scoped to a horizontal inline axis - which
                // also covers sideways-rl/-lr, whose own line boxes are laid out exactly like
                // horizontal-tb's, see IsVerticalDecorationGeometry - a genuinely vertical
                // (vertical-rl/vertical-lr) box's wrapped inline outline is left exactly as it was:
                // its own inline-axis border/padding isn't even reserved at a line wrap yet either
                // (issue #769), so closing only the outline there would produce a ring that doesn't
                // match its own box's inline extent.
                var closesAtLineWrap = !IsVerticalDecorationGeometry(box);
                (outlinePaints ??= []).Add(new OutlineRect(
                    outlineRect,
                    geometry.HasLeftEdge || closesAtLineWrap,
                    geometry.HasRightEdge || closesAtLineWrap,
                    geometry.HasTopEdge,
                    geometry.HasBottomEdge));
            }

            if (!_textOnly && box.ColumnRuleSegments is { Count: > 0 } && box.ActualColumnRuleWidth > 0)
            {
                PaintColumnRules(g, box, fragment.OriginY, clip);
            }

            PaintWords(g, box, fragment);

            // A block container's own decoration area is one rectangle - its border box - which is the
            // right area for its background and border but never for its text decoration: css-text-decor-3
            // §2.4 propagates a block container's decoration to the anonymous inline box wrapping its
            // in-flow inline content, so the line spans that content, not the box's full width. The
            // per-line rectangles a line-hosted box carries already are that content, so only the
            // one-rectangle (Line: null) case needs the content found for it.
            if (_textOnly)
            {
                // Text decorations (underline, line-through) are drawn shapes, not text.
            }
            else if (lines is [{ Line: null }])
            {
                PaintPropagatedDecoration(g, box, fragment, clip);
            }
            else
            {
                // The box's own per-line rectangles already are the decoration area, so no span has to be
                // collected - but what those rectangles cover still decides where the line breaks: the
                // atomic inlines inside it, which §2.4 does not decorate, and the glyph ink an underline
                // or overline skips. Gathered once for the whole box rather than per line.
                var content = DecorationsWorthCollecting(box)
                    ? DecorationContent.Of(fragment, collectSpans: false)
                    : null;

                for (var i = 0; i < lines.Count; i++)
                {
                    var actualRect = lines[i].Rect;

                    if (IsRectVisible(actualRect, clip))
                    {
                        // Text decoration is drawn over the rectangle itself, never the unbroken box: an
                        // underline belongs to the line it underlines. Only whether to inset it by the box's own
                        // padding and border is a §6.2 question, which is what the edge flags answer.
                        var geometry = BoxDecorationGeometry.For(box, lines[i]);

                        PaintDecoration(g, box, actualRect, geometry.HasLeftEdge, geometry.HasRightEdge,
                            GetFirstLineStyleForRect(lines[i].Line), ownDecorationArea: true,
                            content, lines[i].Line);
                    }
                }
            }

            // Positive z-index layers are Appendix E step 9: over everything else this box paints - its
            // collapsed borders and marker below included - so they are set aside and painted last.
            List<List<StackingOrder.StackingParticipant>>? raisedLayers = null;

            foreach (var layerBoxes in _ownOnly ? [] : StackingOrder.ByLayers(StackingOrder.Flatten(fragment)))
            {
                if (StackingOrder.LayerOf(layerBoxes[0]) > 0)
                    (raisedLayers ??= []).Add(layerBoxes);
                else
                    PaintLayer(g, layerBoxes);
            }

            // CSS 2.1 §17.6.2's resolved borders are drawn once per grid-line run, after every
            // table-internal background and content has painted - which is the whole fix (issue #735):
            // boxes paint in tree order, so a later row's opaque cell background would otherwise erase
            // the border the row above it shares with it. Before the outline pass, because an outline is
            // always on top (CSS UI 4 §3.1).
            if (!_textOnly && !_stopped && box.CollapsedBorderSegments is { Count: > 0 })
            {
                PaintCollapsedTableBorders(g, box, fragment.OriginY, clip);
            }

            // Before this box's own overflow clip is popped: a deferred outline records it for replay.
            if (outlinePaints is not null && !_stopped)
                PaintOrDeferOutline(g, box, outlinePaints);

            PopOverflowClip(overflowClipRecorded);

            for (var i = 0; i < clipsPushed; i++)
                g.PopClip();

            if (paintMarkers && !_stopped)
            {
                var markerFragment = FindMarkerFragment(fragment);
                if (markerFragment != null)
                {
                    PaintContent(g, markerFragment);
                }
            }

            if (!_textOnly && !_stopped)
                PaintContentImage(g, box, fragment);

            if (raisedLayers is null || _stopped) return;

            // The scope this box opened draws its outlines here, under its raised layers - an overlay
            // covers a ring - and over the rest of its content (see FragmentPainter.Outlines.cs).
            if (OwnsOutlineScope(fragment))
                DrawScopeOutlinesSoFar(g, box.HtmlContainer?.StructureTagBuilder);

            var raisedClipsPushed = RenderUtils.ClipGraphicsByOverflow(g, fragment.OverflowClip, fragment.OverflowClipCurve);

            foreach (var layerBoxes in raisedLayers)
                PaintLayer(g, layerBoxes);

            for (var i = 0; i < raisedClipsPushed; i++)
                g.PopClip();
        }

        /// <summary>
        /// Paints one z-index layer of a stacking context's participants, in CSS 2.1 Appendix E's
        /// block / float / inline / positioned order.
        /// </summary>
        private void PaintLayer(RGraphics g, List<StackingOrder.StackingParticipant> layerBoxes)
        {
            // Split paint to handle z-order, per CSS2.1 Appendix E's within-a-stacking-context
            // order: in-flow block-level descendants, then non-positioned floats, then in-flow
            // inline-level descendants (text and inline replaced content), then positioned
            // descendants. Block and inline normal-flow content used to share a single pass here
            // (painted in tree order with no float-relative ordering guarantee at all) - Acid2's own
            // ".eyes" trap (a block, a float, and an inline replaced <object> as siblings, each
            // required to paint in a different layer) depends on inline being its own later pass.
            //
            // A plain (non-inline-itself) box whose entire content is inline - e.g. Acid2's own
            // "#eyes-a" div, a block wrapper around nothing but its resolved inline <object> image -
            // is treated as belonging to the inline pass too, via StackingOrder.ActsAsInline. Its
            // own recursive paint call is what actually paints its inline child (through ITS OWN
            // nested stacking loop), so deferring that whole call to this stacking context's inline
            // pass is what makes the child paint after this context's float pass, matching Appendix
            // E, without needing to hoist the descendant out of its normal DOM position/ancestor at
            // all (unlike the out-of-flow float/absolute/fixed hoisting StackingOrder.Flatten
            // already does - that mechanism moves a box's paint call across ancestor boundaries
            // entirely, which isn't needed or wanted here since "#eyes-a" itself already belongs to
            // this stacking context's own direct children).
            foreach (var p in layerBoxes)
            {
                if (!StackingOrder.ActsAsInline(p.Box) && !p.Box.IsPositioned && !p.Box.IsFloated)
                    PaintStackingParticipant(g, p);
            }

            foreach (var p in layerBoxes)
            {
                // Appendix E step 5 is NON-positioned floats only. A float that is also positioned
                // belongs to step 8 below, in tree order with every other positioned box - painting it
                // here let a later positioned sibling or ancestor's background cover it.
                if (p.Box.IsFloated && !p.Box.IsPositioned)
                    PaintStackingParticipant(g, p);
            }

            foreach (var p in layerBoxes)
            {
                if (StackingOrder.ActsAsInline(p.Box) && !p.Box.IsPositioned && !p.Box.IsFloated)
                    PaintStackingParticipant(g, p);
            }

            foreach (var p in layerBoxes)
            {
                // CSS 2.1 Appendix E step 8 is one tree-order bucket for every positioned
                // descendant at stack level 0. Splitting absolute/fixed/relative into separate
                // passes reorders otherwise-equal siblings by positioning scheme.
                if (p.Box.IsPositioned)
                    PaintStackingParticipant(g, p);
            }
        }

        /// <summary>
        /// Paints one stacking-context participant discovered by <see cref="StackingOrder.Flatten"/>.
        /// A hoisted participant (non-empty <see cref="StackingOrder.StackingParticipant.ClipAncestors"/>)
        /// paints via this fragment's own paint loop rather than the ordinary nested parent-to-child
        /// cascade, so its ancestors' own <c>overflow: hidden</c> clipping - normally picked up "for
        /// free" from still being active on the graphics clip stack during natural nested painting - is
        /// never applied on its own. Re-apply it explicitly here instead, scoped to exactly this
        /// participant's own paint call. A no-op for a direct plain child (empty ClipAncestors).
        /// </summary>
        private void PaintStackingParticipant(RGraphics g, StackingOrder.StackingParticipant participant)
        {
            var fragment = participant.Fragment;
            var pushedClips = RenderUtils.PushAncestorOverflowClips(g, participant.ClipAncestors);

            PaintFragment(g, fragment);

            for (var i = 0; i < pushedClips; i++)
                g.PopClip();
        }

        /// <summary>
        /// Whether <paramref name="rect"/> survives <paramref name="clip"/>. A rect merely touching the
        /// clip edge isn't actually visible - see <see cref="VisibilityClipEpsilon"/>.
        /// </summary>
        private static bool IsRectVisible(RRect rect, RRect clip)
        {
            rect.X -= 2;
            rect.Width += 2;
            clip.Intersect(rect);

            return clip.Width > VisibilityClipEpsilon && clip.Height > VisibilityClipEpsilon;
        }

        /// <summary>
        /// Resolves the <c>::first-line</c> style (if any) that applies to a rectangle painted for
        /// <paramref name="lineBox"/> - true exactly when the line's own owner (the block establishing
        /// that inline formatting context - see <see cref="CssLineBox.OwnerBox"/>) has a resolved
        /// first-line style and this is genuinely that owner's first line. Used to make
        /// <see cref="PaintBackground"/>/<see cref="PaintDecoration"/> first-line-aware without either
        /// method needing to know its own relationship to the block establishing its inline formatting
        /// context - the line box already knows.
        /// </summary>
        private static CssBox? GetFirstLineStyleForRect(CssLineBox? lineBox) =>
            lineBox is not null && lineBox.OwnerBox.ResolvedFirstLineStyle is not null && lineBox == lineBox.OwnerBox.LineBoxes.FirstOrDefault()
                ? lineBox.OwnerBox.ResolvedFirstLineStyle
                : null;
    }
}
