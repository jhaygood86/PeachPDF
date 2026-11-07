using PeachBrackets.Html.Animation;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.Paint;
using PeachBrackets.Html.Parse;
using PeachBrackets.Html.Utils;
using PeachBrackets.Updates;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace PeachBrackets.Html.Updates
{
    /// <summary>
    /// Restyles a laid-out document for a change of the interaction state (<c>:hover</c>, <c>:active</c>, <c>:focus</c> and the like) without
    /// loading it again. The boxes a rule that reads the state could reach are found when the document is cascaded
    /// (<see cref="CssBox.HasStatefulCandidates"/>); those that match different rules under the new state are cascaded again in place, with their
    /// descendants, which inherit from them; and the change is kept only if every property that came out different is one that cannot move or
    /// resize anything (<see cref="PaintOnly"/>). Anything else - or anything unexpected - gives the whole attempt up, and the caller loads the
    /// document again, which discards the tree this left behind: nothing needs undoing.
    /// </summary>
    internal static class InteractionRestyler
    {
        // Properties whose value only ever changes how a box is painted. A change to any other property stays out of the fast path.
        private static readonly HashSet<string> PaintOnly = new(StringComparer.OrdinalIgnoreCase)
        {
            "color", "background-color", "caret-color", "accent-color", "cursor", "opacity", "mix-blend-mode", "visibility", "transform", "translate", "rotate", "scale", "filter", "box-shadow", "text-shadow",
            "text-decoration-line", "text-decoration-style", "text-decoration-color", "text-decoration-thickness", "text-underline-offset",
            "outline-color", "outline-style", "outline-width", "outline-offset",
            "border-top-color", "border-right-color", "border-bottom-color", "border-left-color", "column-rule-color",
            "-webkit-text-stroke-color", "-webkit-text-fill-color",
        };

        /// <summary>Whether a change to <paramref name="property"/> only ever changes how a box is painted, so it can be applied with no layout pass.</summary>
        internal static bool IsPaintOnly(string property) => PaintOnly.Contains(property);

        /// <summary>Whether <paramref name="property"/> is one of the four that make a box's transform: <c>transform</c>, <c>translate</c>, <c>rotate</c> and <c>scale</c>.</summary>
        internal static bool IsTransformProperty(string property) =>
            property.Equals("transform", StringComparison.OrdinalIgnoreCase) || property.Equals("translate", StringComparison.OrdinalIgnoreCase)
            || property.Equals("rotate", StringComparison.OrdinalIgnoreCase) || property.Equals("scale", StringComparison.OrdinalIgnoreCase);

        private static bool IsTransformProperty(HashSet<string> names) =>
            names.Contains("transform") || names.Contains("translate") || names.Contains("rotate") || names.Contains("scale");

        // Properties that change what boxes there are or how the tree is built, which a layout pass over the boxes that exist cannot follow.
        private static readonly HashSet<string> Structural = new(StringComparer.OrdinalIgnoreCase)
        {
            "display", "position", "float", "clear", "content", "contain", "container-type", "container-name", "writing-mode", "direction", "unicode-bidi",
            "will-change", "quotes", "counter-reset", "counter-increment", "counter-set", "all", "visibility",
        };

        /// <summary>
        /// Whether a running animation can write <paramref name="property"/> onto the boxes that exist: a paint-only property is only repainted, and any
        /// other that does not change the shape of the box tree is followed by a layout pass. Custom properties and the properties that build boxes are left out.
        /// </summary>
        internal static bool IsLiveAnimatable(string property) =>
            PaintOnly.Contains(property)
            || (!Structural.Contains(property) && !property.StartsWith("--", StringComparison.Ordinal)
                && !property.StartsWith("animation", StringComparison.OrdinalIgnoreCase) && !property.StartsWith("transition", StringComparison.OrdinalIgnoreCase));

        /// <summary>Whether a change to <paramref name="property"/> reaches the boxes under the one that has it (it is inherited), so they are cascaded again.</summary>
        internal static bool InheritsToDescendants(string property) => CssPropertyRegistry.InheritedProperties.Contains(property);

        // Every property the registry can read back: those with an initial value (the cascade resets all of them) and those the revert snapshots read.
        private static readonly string[] AllPropertyNames =
            CssPropertyRegistry.InitialValues.Keys.Union(CssPropertyRegistry.SnapshotPropertyNames, StringComparer.OrdinalIgnoreCase).ToArray();

        private static Dictionary<string, string?> ReadAll(CssBox box)
        {
            var values = new Dictionary<string, string?>(AllPropertyNames.Length, StringComparer.OrdinalIgnoreCase);
            foreach (var name in AllPropertyNames)
                values[name] = CssUtils.GetPropertyValue(box, name);

            return values;
        }

        // Past this many boxes under the ones to restyle, loading the document again is not far behind.
        private const int MaxBoxes = 6000;

        internal readonly record struct Outcome(bool Applied, string? Reason, List<DirtyRegion> DirtyRegions);

        // What a box paints beyond its rectangle, and how it is moved, in layout units.
        internal readonly record struct PaintExtent(double Bleed, bool HasTransform, Matrix3x2 Matrix)
        {
            internal static PaintExtent Of(CssBox box)
            {
                var bleed = Math.Max(FragmentPainter.ShadowBleed(box), Math.Max(FragmentPainter.OutlineBleed(box), FragmentPainter.TextShadowBleed(box)));
                foreach (var function in box.ActualFilterFunctions)
                {
                    if (function.Name == "blur" && function.Arguments.Count > 0)
                        bleed = Math.Max(bleed, 3 * Math.Max(0, CssValueParser.ParseLength(function.Arguments[0], 0, box)));
                }

                return box.HasTransform ? new PaintExtent(bleed, true, box.ActualTransformMatrix) : new PaintExtent(bleed, false, Matrix3x2.Identity);
            }
        }

        internal readonly record struct ExtentChange(double OldBleed, double NewBleed, bool OldTransform, bool NewTransform, Matrix3x2 OldMatrix, Matrix3x2 NewMatrix);

        private static Outcome Fail(string reason) => new(false, reason, []);

        public static Outcome TryApply(HtmlContainerInt container, InteractionState before, InteractionState after, HashSet<int> changed,
            IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            try
            {
                return Restyle(container, before, after, changed, fragmentainers);
            }
            catch (Exception e) when (e is not OutOfMemoryException)
            {
                // Whatever the tree is now is discarded by the reload the caller falls back to.
                container.Interaction = after;
                return Fail("The restyle failed: " + e.GetType().Name + ": " + e.Message);
            }
        }

        private static Outcome Restyle(HtmlContainerInt container, InteractionState before, InteractionState after, HashSet<int> changed,
            IReadOnlyList<FragmentainerFragment> fragmentainers)
        {
            if (container.Root is not { } root || container.CssData is not { } cssData)
                return Fail("The document has no cascade to repeat.");

            if (fragmentainers.Count == 0)
                return Fail("The document has not been laid out.");

            if (cssData.HasStatefulSpecialPseudoRules)
                return Fail("A rule that reads the state styles a pseudo-element the fast path does not resolve.");

            var mediaType = string.IsNullOrEmpty(container.Media) ? "print" : container.Media;
            var media = MediaQueryContext.FromContainer(container, mediaType);
            var sizes = container.PreviousPassContainerSizes;

            // The boxes that match different rules under the new state.
            var stateful = new List<CssBox>();
            CollectStateful(root, stateful);

            // An inline SVG is a scene graph built once, with its elements' style resolved then and their boxes dropped, so a rule whose subject is an element
            // inside it (`a:hover .icon`, `a:hover + svg .icon`) cannot be restyled in place. When a rule that reads the state could match differently because of the
            // element whose state changed, wherever it sits, leave it to a full load. html and body are always among the changed and say nothing: a rule on them
            // reaches the SVG through the page's own boxes.
            if (HasStatefulSvgRuleReaching(root, changed))
                return Fail("The state changed on an element that a rule reading it styles an inline SVG's elements through, which the fast path does not restyle.");

            var roots = new List<CssBox>();
            try
            {
                foreach (var box in stateful)
                {
                    var origin = box;
                    while (origin.HtmlTag is null && origin.ParentBox is { } parent)
                        origin = parent;

                    var own = origin.HtmlTag is { SourceOrdinal: >= 0 } tag && changed.Contains(tag.SourceOrdinal);

                    // Rules on a box's own state match it differently only when its own state changed.
                    if (!own && !box.HasRelationalStatefulCandidates)
                        continue;

                    switch (cssData.CompareStatefulMatches(media, box, sizes, container, before, after, own))
                    {
                        case 1:
                            roots.Add(box);
                            break;
                        case 2:
                            return Fail("The new state adds or removes a pseudo-element.");
                    }
                }
            }
            finally
            {
                container.Interaction = after;
            }

            if (roots.Count == 0)
                return new Outcome(true, null, []);

            // A root under another root is restyled with it.
            var rootSet = new HashSet<CssBox>(roots);
            roots.RemoveAll(box =>
            {
                for (var ancestor = box.ParentBox; ancestor is not null; ancestor = ancestor.ParentBox)
                {
                    if (rootSet.Contains(ancestor)) return true;
                }

                return false;
            });

            // What is there now, to tell afterwards what changed.
            var boxes = new List<CssBox>();
            foreach (var box in roots)
            {
                if (!CollectSubtree(box, boxes))
                    return Fail("The restyle reaches boxes the fast path leaves to a full load (table repeats, spacers) or too many of them.");

                if (box.IsWithinForeignContent)
                    return Fail("The restyle reaches foreign content.");
            }

            var oldStyles = new Dictionary<CssBox, (ComputedStyle Style, PaintExtent Extent)>(boxes.Count);
            foreach (var box in boxes)
                oldStyles[box] = (box.ComputedStyle, PaintExtent.Of(box));

            foreach (var box in roots)
                DomParser.RecascadeForState(container, box);

            var extents = new Dictionary<CssBox, ExtentChange>();
            var stackingChanged = false;
            foreach (var box in boxes)
            {
                var (oldStyle, oldExtent) = oldStyles[box];
                if (ReferenceEquals(oldStyle, box.ComputedStyle))
                    continue;

                var verdict = Classify(box, oldStyle, out var names, out var transformFlip, out var changes);
                if (verdict is not null)
                    return Fail(verdict);

                if (names.Count == 0)
                    continue;

                if (transformFlip && HasOutOfFlowDescendant(box))
                    return Fail("A transform or filter appears or goes under a box with positioned descendants, which it makes their containing block.");

                InvalidatePaintCaches(box);

                // A property the box's transition-* names does not snap to its new value but runs to it from the old one.
                if (container.AnimationsEnabled && changes.Count > 0 && !PropertyFactoryHasNoTransition(box))
                {
                    container.AnimationClock ??= new PeachBrackets.Html.Animation.AnimationClock(container);
                    container.AnimationClock.BeginTransitions(box, changes);
                    InvalidatePaintCaches(box);
                }

                var newExtent = PaintExtent.Of(box);
                extents[box] = new ExtentChange(oldExtent.Bleed, newExtent.Bleed, oldExtent.HasTransform, newExtent.HasTransform, oldExtent.Matrix, newExtent.Matrix);

                if (names.Contains("opacity") || IsTransformProperty(names) || names.Contains("filter") || names.Contains("mix-blend-mode"))
                    stackingChanged = true;
            }

            if (stackingChanged)
                container.RefreshStackingHoistCandidates();

            // The boxes were restyled in place, not rebuilt: what is kept per box for as long as the style is the same (an inline SVG's matched
            // declarations, see CssBoxSvgSourceNode) is stale now.
            container.StyleGeneration++;

            return new Outcome(true, null, StyleUpdater.DirtyRegionsForExtents(container, extents, fragmentainers));
        }

        private static void CollectStateful(CssBox box, List<CssBox> into)
        {
            if (box.HasStatefulCandidates)
                into.Add(box);

            foreach (var child in box.Boxes)
                CollectStateful(child, into);
        }

        // Whether a rule that reads the state and was a candidate for an element inside an inline SVG could match differently because an element whose state
        // changed (other than html and body) is the one its state compound matches, wherever that element sits relative to the SVG.
        private static bool HasStatefulSvgRuleReaching(CssBox root, HashSet<int> changed)
        {
            var svgs = new List<CssBox>();
            var changedBoxes = new List<CssBox>();
            CollectSvgsAndChanged(root, changed, svgs, changedBoxes);

            foreach (var svg in svgs)
            {
                foreach (var selector in svg.SvgInnerStatefulSelectors!)
                {
                    foreach (var box in changedBoxes)
                    {
                        if (CssData.StateCompoundCouldMatch(selector, box)) return true;
                    }
                }
            }

            return false;
        }

        private static void CollectSvgsAndChanged(CssBox box, HashSet<int> changed, List<CssBox> svgs, List<CssBox> changedBoxes)
        {
            if (box.HtmlTag is { SourceOrdinal: >= 0 } tag && changed.Contains(tag.SourceOrdinal)
                && !tag.Name.Equals("html", StringComparison.OrdinalIgnoreCase) && !tag.Name.Equals("body", StringComparison.OrdinalIgnoreCase))
            {
                changedBoxes.Add(box);
            }

            if (box is CssBoxSvg)
            {
                if (box.HasSvgInnerStatefulCandidates) svgs.Add(box);
                return;
            }

            foreach (var child in box.Boxes)
                CollectSvgsAndChanged(child, changed, svgs, changedBoxes);
        }

        private static bool CollectSubtree(CssBox box, List<CssBox> into)
        {
            if (box is CssProxyBox or CssSpacingBox || into.Count > MaxBoxes)
                return false;

            into.Add(box);
            foreach (var child in box.Boxes)
            {
                if (!CollectSubtree(child, into))
                    return false;
            }

            return true;
        }

        private static bool HasOutOfFlowDescendant(CssBox box)
        {
            foreach (var child in box.Boxes)
            {
                if (child.Position.Value is Css.PositionMode.Absolute or Css.PositionMode.Fixed || HasOutOfFlowDescendant(child))
                    return true;
            }

            return false;
        }

        // Says why the box's new style cannot be kept (null when it can), and which properties differ.
        private static string? Classify(CssBox box, ComputedStyle old, out HashSet<string> names, out bool transformFlip, out List<(string Property, string? Old, string? New)> changes)
        {
            names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            transformFlip = false;
            changes = [];

            var current = box.ComputedStyle;
            if (AreasEqual(old, current))
                return null;

            // Properties the registry cannot read back (layered backgrounds, say) would pass the comparison of names below unseen. So the areas
            // that hold anything but paint must be equal, and in the ones that hold paint beside other things only the fields of the paint-only
            // properties may differ.
            if (!LayoutAreasEqual(old, current))
                return $"A style area that holds more than paint changed on <{box.HtmlTag?.Name ?? "box"}>.";

            // The old style, read back the way the new one is.
            Dictionary<string, string?> oldValues;
            box.AdoptComputedStyle(old);
            try
            {
                oldValues = ReadAll(box);
            }
            finally
            {
                box.AdoptComputedStyle(current);
            }

            var newValues = ReadAll(box);
            foreach (var (name, value) in newValues)
            {
                if (oldValues.TryGetValue(name, out var previous) && string.Equals(previous, value, StringComparison.Ordinal))
                    continue;

                if (!PaintOnly.Contains(name))
                    return $"'{name}' changed on <{box.HtmlTag?.Name ?? "box"}>, which can change layout.";

                // collapse removes a table row or column from the table's layout (CSS Tables 3 §3.3), unlike hidden, which only stops painting it.
                if (IsCollapse(name, previous) || IsCollapse(name, value))
                    return "visibility: collapse changes how a table is laid out.";

                if (name.Equals("background-color", StringComparison.OrdinalIgnoreCase) && box.HtmlTag is { Name: "html" or "body" })
                    return "The background of html and body is the page's canvas background.";

                if ((IsTransformProperty(name) || name.Equals("filter", StringComparison.OrdinalIgnoreCase))
                    && IsNone(previous) != IsNone(value))
                    transformFlip = true;

                names.Add(name);
                changes.Add((name, previous, value));
            }

            return null;
        }

        /// <summary>
        /// Every property whose value differs between <paramref name="old"/> and the style <paramref name="box"/> has now, as (property, old value, new value),
        /// for the transitions a style change starts. Empty when the style did not change. The box ends up holding the style it had.
        /// </summary>
        internal static List<(string Property, string? Old, string? New)> DiffStyles(CssBox box, ComputedStyle old)
        {
            var changes = new List<(string, string?, string?)>();
            var current = box.ComputedStyle;
            if (ReferenceEquals(old, current) || AreasEqual(old, current))
                return changes;

            Dictionary<string, string?> oldValues;
            box.AdoptComputedStyle(old);
            try
            {
                oldValues = ReadAll(box);
            }
            finally
            {
                box.AdoptComputedStyle(current);
            }

            foreach (var (name, now) in ReadAll(box))
            {
                // A property the style does not set reads back as nothing: its value is the initial one.
                var value = now ?? CssDefaults.GetInitialValue(name);
                var previous = oldValues.GetValueOrDefault(name) ?? CssDefaults.GetInitialValue(name);
                if (string.Equals(previous, value, StringComparison.Ordinal))
                    continue;

                changes.Add((name, previous, value));
            }

            // What was derived from the old style while it was read back is not the new style's.
            box.ResetDerivedStyle();
            return changes;
        }

        // No transition runs when nothing names a property, or when every entry lasts no time and waits none (CSS Transitions 1 §3: it starts only when
        // the larger of the duration and zero, plus the delay, is positive - a zero duration with a delay still holds the old value through the delay).
        internal static bool PropertyFactoryHasNoTransition(CssBox box) =>
            AnimationNone(box.TransitionProperty) || (AnimationNone(box.TransitionDuration) && !AnyPositiveDelay(box.TransitionDelay));

        private static bool AnyPositiveDelay(string? list)
        {
            foreach (var entry in AnimationApplier.SplitList(list))
            {
                if (AnimationApplier.LiveSeconds(entry) > 0) return true;
            }

            return false;
        }

        private static bool AnimationNone(string? list) => string.IsNullOrWhiteSpace(list) || list.Trim().Equals("none", StringComparison.OrdinalIgnoreCase) || list.Trim() is "0s" or "0ms";

        internal static bool IsCollapse(string property, string? value) =>
            property.Equals("visibility", StringComparison.OrdinalIgnoreCase) && value is not null && value.Trim().Equals("collapse", StringComparison.OrdinalIgnoreCase);

        private static bool IsNone(string? value) => string.IsNullOrEmpty(value) || value.Equals("none", StringComparison.OrdinalIgnoreCase);

        private static bool LayoutAreasEqual(ComputedStyle a, ComputedStyle b) =>
            a.GeneratedContent.Equals(b.GeneratedContent) && a.BoxModel.Equals(b.BoxModel) && a.Break.Equals(b.Break) && a.Pagination.Equals(b.Pagination)
            && a.DisplayPositioning.Equals(b.DisplayPositioning) && a.Table.Equals(b.Table) && a.Font.Equals(b.Font) && a.List.Equals(b.List)
            && a.Flex.Equals(b.Flex) && a.Grid.Equals(b.Grid) && a.MultiColumn.Equals(b.MultiColumn with { ColumnRuleColor = a.MultiColumn.ColumnRuleColor })
            && a.Background.Equals(b.Background with { BackgroundColor = a.Background.BackgroundColor })
            && a.Border.Equals(b.Border with
            {
                BorderTopColor = a.Border.BorderTopColor,
                BorderRightColor = a.Border.BorderRightColor,
                BorderBottomColor = a.Border.BorderBottomColor,
                BorderLeftColor = a.Border.BorderLeftColor,
                BoxShadow = a.Border.BoxShadow,
                OutlineColor = a.Border.OutlineColor,
                OutlineStyle = a.Border.OutlineStyle,
                OutlineWidth = a.Border.OutlineWidth,
                OutlineOffset = a.Border.OutlineOffset,
            })
            && a.VisualEffects.Equals(b.VisualEffects with
            {
                Opacity = a.VisualEffects.Opacity,
                Transform = a.VisualEffects.Transform,
                Translate = a.VisualEffects.Translate,
                Rotate = a.VisualEffects.Rotate,
                Scale = a.VisualEffects.Scale,
                Filter = a.VisualEffects.Filter,
                MixBlendMode = a.VisualEffects.MixBlendMode,
            });

        private static bool AreasEqual(ComputedStyle a, ComputedStyle b) =>
            a.Border.Equals(b.Border) && a.VisualEffects.Equals(b.VisualEffects) && a.GeneratedContent.Equals(b.GeneratedContent)
            && a.BoxModel.Equals(b.BoxModel) && a.Break.Equals(b.Break) && a.Pagination.Equals(b.Pagination) && a.Background.Equals(b.Background)
            && a.DisplayPositioning.Equals(b.DisplayPositioning) && a.Table.Equals(b.Table) && a.Text.Equals(b.Text) && a.TextDecoration.Equals(b.TextDecoration)
            && a.Font.Equals(b.Font) && a.List.Equals(b.List) && a.Flex.Equals(b.Flex) && a.Grid.Equals(b.Grid) && a.MultiColumn.Equals(b.MultiColumn)
            && CustomPropertiesEqual(a.CustomProperties, b.CustomProperties);

        private static bool CustomPropertiesEqual(Dictionary<string, string>? a, Dictionary<string, string>? b)
        {
            if (ReferenceEquals(a, b)) return true;
            if (a is null || b is null) return (a?.Count ?? 0) == (b?.Count ?? 0);
            if (a.Count != b.Count) return false;

            foreach (var (key, value) in a)
            {
                if (!b.TryGetValue(key, out var other) || other != value)
                    return false;
            }

            return true;
        }

        // The values derived from the style that the setters do not clear, for the properties that may have changed.
        internal static void InvalidatePaintCaches(CssBox box)
        {
            var style = box.DerivedStyle;
            style.InvalidateColor();
            style.InvalidateBackgroundColor();
            style.InvalidateBorderTopColor();
            style.InvalidateBorderRightColor();
            style.InvalidateBorderBottomColor();
            style.InvalidateBorderLeftColor();
            style.InvalidateOutlineColor();
            style.InvalidateOutlineWidth();
            style.InvalidateOutlineOffset();
            style.InvalidateOpacity();
            style.InvalidateTransform();
            style.InvalidateFilter();
            style.InvalidateBackdropFilter();
            style.InvalidateMixBlendMode();
        }
    }
}
