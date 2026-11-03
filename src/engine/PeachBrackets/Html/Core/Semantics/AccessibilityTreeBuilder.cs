using PeachBrackets.Accessibility;
using PeachBrackets.Css;
using PeachBrackets.Html.Annotations;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Fragments;
using PeachBrackets.Html.TextGeometry;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PeachBrackets.Html.Semantics
{
    /// <summary>
    /// Builds the accessibility tree of a laid-out document: it walks the box tree for what each element is (its role, name, state), and the
    /// fragment tree for where each part landed on its pages. Nothing is drawn, and nothing is worked out until the tree is asked for.
    /// </summary>
    internal sealed partial class AccessibilityTreeBuilder
    {
        private readonly HtmlContainerInt _container;
        private readonly Dictionary<CssBox, List<AccessibilityBounds>> _bounds = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<string, CssBox> _byElementId = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<CssBox>> _labelsFor = new(StringComparer.Ordinal);
        private readonly Dictionary<AccessibilityNode, int> _textCounts = new(ReferenceEqualityComparer.Instance);
        private readonly Dictionary<CssBox, AccessibilityNode> _nodeByBox = new(ReferenceEqualityComparer.Instance);
        private readonly HashSet<CssBox> _visitedSources = new(ReferenceEqualityComparer.Instance);
        private int _anonymous;

        private AccessibilityTreeBuilder(HtmlContainerInt container) => _container = container;

        /// <summary>Builds the tree of a document that has been laid out.</summary>
        /// <param name="container">the laid-out document</param>
        /// <param name="title">the document's title, which names the root, or null</param>
        public static AccessibilityTree Build(HtmlContainerInt container, string? title)
        {
            var builder = new AccessibilityTreeBuilder(container);
            return builder.Build(title);
        }

        private AccessibilityTree Build(string? title)
        {
            CollectBounds();

            var root = new AccessibilityNode("root", AccessibilityRole.Document)
            {
                Name = Collapse(title),
                Language = _container.DocumentLanguage,
            };

            if (_container.Root is { } top)
            {
                Index(top);
                var context = new SemanticContext(false, false, false, false, _container.DocumentLanguage, false);
                Visit(top, context, root);
                root.Text = Collapse(string.Join(' ', Texts(root)));
                ResolveStructure(root);
            }

            return new AccessibilityTree(root, _nodeByBox);
        }

        private static IEnumerable<string> Texts(AccessibilityNode node)
        {
            foreach (var child in node.Children)
            {
                if (child.Role == AccessibilityRole.Text)
                    yield return child.Name;
                else if (child.Text.Length > 0)
                    yield return child.Text;
            }
        }

        // ---- where things are --------------------------------------------------------------------------------------------------------

        // The rectangle of every box on every page it is on, in points from the top-left of the sheet: the union of the fragments it has there.
        private void CollectBounds()
        {
            var fragmentainers = _container.FragmentTree?.Fragmentainers;
            if (fragmentainers is null)
                return;

            var ppp = _container.RenderContext.LayoutUnitsPerPoint;
            for (var page = 0; page < fragmentainers.Count; page++)
            {
                var geometry = fragmentainers[page].Geometry;
                var frame = new TextRunCollector.Frame(geometry.ContentLeftPt, geometry.ContentTopPt, _container.MarginLeft, _container.MarginTop, ppp);
                WalkFragments(fragmentainers[page].Root, page, frame, ppp);
            }
        }

        private void WalkFragments(BoxFragment fragment, int page, TextRunCollector.Frame frame, double ppp)
        {
            Add(fragment.Box, page, fragment.Rect, frame, ppp);
            foreach (var word in fragment.Words)
            {
                if (word.Word.OwnerBox is { } owner && (word.Word.Text is { Length: > 0 }))
                    Add(owner, page, word.Rect, frame, ppp);
            }

            foreach (var child in fragment.Children)
                WalkFragments(child, page, frame, ppp);
        }

        private void Add(CssBox box, int page, Rect local, TextRunCollector.Frame frame, double ppp)
        {
            if (local.Width <= 0 && local.Height <= 0)
                return;

            var rect = new Rect(frame.X(local.Left), frame.Y(local.Top), local.Width / ppp, local.Height / ppp);
            if (!_bounds.TryGetValue(box, out var list))
                _bounds[box] = list = [];

            for (var i = 0; i < list.Count; i++)
            {
                if (list[i].PageIndex != page)
                    continue;

                var old = list[i].Bounds;
                var left = Math.Min(old.Left, rect.Left);
                var top = Math.Min(old.Top, rect.Top);
                list[i] = new AccessibilityBounds(page, new Rect(left, top, Math.Max(old.Right, rect.Right) - left, Math.Max(old.Bottom, rect.Bottom) - top));
                return;
            }

            list.Add(new AccessibilityBounds(page, rect));
        }

        // ---- the walk ----------------------------------------------------------------------------------------------------------------

        // Finds the elements that other elements name by id: what a label is for, what aria-labelledby points at.
        private void Index(CssBox box)
        {
            if (box.HtmlTag is { } tag)
            {
                if (tag.TryGetAttribute("id") is { Length: > 0 } id)
                    _byElementId.TryAdd(id, box);

                if (IsTag(tag, "label") && tag.TryGetAttribute("for") is { Length: > 0 } target)
                {
                    if (!_labelsFor.TryGetValue(target, out var labels))
                        _labelsFor[target] = labels = [];

                    labels.Add(box);
                }
            }

            foreach (var child in box.Boxes)
                Index(child is CssProxyBox proxy ? proxy.SourceBox : child);
        }

        private void Visit(CssBox box, SemanticContext context, AccessibilityNode parent)
        {
            // A proxy is visited by the box it was taken out of (see Children).
            if (box is CssProxyBox)
                return;

            if (box.HtmlTag is not { } tag)
            {
                if (!context.SuppressText && HasOwnText(box))
                    AddText(box, parent);

                foreach (var child in Children(box))
                    Visit(child, context, parent);

                return;
            }

            // Left out of the tree with everything in it. An element with display: none still has a box, but nothing of it is shown or read.
            if (IsTrue(tag, "aria-hidden") || tag.HasAttribute("inert") || box.DerivedStyle.ActualDisplay == Keywords.None)
                return;

            var hidden = box.Visibility.Value != Visibility.Visible;
            var focusable = IsFocusable(tag, context);
            var hasAuthorName = HasAuthorName(tag);
            var (role, level) = RoleResolver.Resolve(box, tag, context, focusable, hasAuthorName);
            var inner = Descend(context, tag, role);

            // Something with no role of its own, or one that is only there to look like something, gives its content to what it is in; so does
            // an element that is not shown (its children may still be).
            // An element that says it is a live region keeps its node whatever its role, so a change in it can be told to the reader.
            var keepsLiveRegion = role == AccessibilityRole.Generic && tag.TryGetAttribute("aria-live")?.Trim().ToLowerInvariant() is "polite" or "assertive";
            if (hidden || (role is AccessibilityRole.Generic or AccessibilityRole.Presentation && !keepsLiveRegion))
            {
                foreach (var child in Children(box))
                    Visit(child, inner, parent);

                return;
            }

            var node = new AccessibilityNode(IdOf(tag), role)
            {
                TagName = tag.Name.ToLowerInvariant(),
                ElementId = tag.TryGetAttribute("id") is { Length: > 0 } elementId ? elementId : null,
                Language = inner.Language,
                Level = level,
                IsFocusable = focusable,
                TabIndex = int.TryParse(tag.TryGetAttribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var tabIndex) ? tabIndex : null,
                AccessKey = NonEmpty(tag.TryGetAttribute("accesskey")),
                KeyShortcuts = NonEmpty(tag.TryGetAttribute("aria-keyshortcuts")),
            };

            if (role == AccessibilityRole.Link && tag.TryGetAttribute("href") is { } href)
                node.Uri = DocumentAnnotationCollector.ResolveHref(_container, href);

            if (_bounds.TryGetValue(box, out var bounds))
            {
                foreach (var b in bounds)
                    node.AddBounds(b);
            }

            ApplyStyle(node, box);
            ApplyStates(node, box, tag, role, context);
            ReadStructureAttributes(node, tag);
            ReadRange(node, tag, role);
            node.Name = ComputeName(box, tag, role, out var nameFromTitle);
            node.Description = ComputeDescription(box, tag, nameFromTitle);
            parent.AddChild(node);
            _nodeByBox[box] = node;

            // The text of something named by its content is its name: it has no text nodes below it, only the parts of it that are more than text.
            var below = inner with { SuppressText = RoleResolver.NamesFromContent(role) || IsLeaf(role) };
            foreach (var child in Children(box))
                Visit(child, below, node);

            node.Text = Collapse(string.Join(' ', Texts(node)));
            if (node.Text.Length == 0)
                node.Text = TextOf(box);
        }

        // The boxes of an element in the order a reader meets them. A table header or footer that repeats on every page is taken out of the
        // table and replaced by one proxy for each page it is on; the original is what the table has, once, and HTML puts a header before the
        // rows and a footer after them wherever the source has them.
        private IEnumerable<CssBox> Children(CssBox box)
        {
            if (!box.Boxes.Exists(c => c is CssProxyBox))
                return box.Boxes;

            var headers = new List<CssBox>();
            var footers = new List<CssBox>();
            var others = new List<CssBox>();
            foreach (var child in box.Boxes)
            {
                if (child is not CssProxyBox proxy)
                    others.Add(child);
                else if (_visitedSources.Add(proxy.SourceBox))
                    (proxy.SourceBox.HtmlTag is { } tag && IsTag(tag, "tfoot") ? footers : headers).Add(proxy.SourceBox);
            }

            return [.. headers, .. others, .. footers];
        }

        // What the nodes below an element are told about it.
        private SemanticContext Descend(SemanticContext context, HtmlTag tag, AccessibilityRole role)
        {
            var name = tag.Name.ToLowerInvariant();
            return context with
            {
                InSectioning = context.InSectioning || name is "article" or "aside" or "main" or "nav" or "section",
                InList = context.InList || role == AccessibilityRole.List,
                InGrid = context.InGrid || role is AccessibilityRole.Grid or AccessibilityRole.TreeGrid,
                FieldsetDisabled = context.FieldsetDisabled || (name == "fieldset" && tag.HasAttribute("disabled")),
                Language = NonEmpty(tag.TryGetAttribute("lang")) ?? NonEmpty(tag.TryGetAttribute("xml:lang")) ?? context.Language,
            };
        }

        // Roles whose content is not read as part of the page: the control is the whole of it.
        private static bool IsLeaf(AccessibilityRole role) =>
            role is AccessibilityRole.Image or AccessibilityRole.Math or AccessibilityRole.GraphicsDocument or AccessibilityRole.Separator
                or AccessibilityRole.TextBox or AccessibilityRole.SearchBox or AccessibilityRole.Slider or AccessibilityRole.SpinButton
                or AccessibilityRole.ProgressBar or AccessibilityRole.Meter;

        private string IdOf(HtmlTag tag) => tag.SourceOrdinal >= 0 ? "e" + tag.SourceOrdinal.ToString(CultureInfo.InvariantCulture) : "n" + (++_anonymous).ToString(CultureInfo.InvariantCulture);

        private static bool HasOwnText(CssBox box)
        {
            if (box.Visibility.Value != Visibility.Visible)
                return false;

            foreach (var word in box.Words)
            {
                if (!word.IsImage && !word.IsLineBreak && !string.IsNullOrWhiteSpace(word.Text))
                    return true;
            }

            return false;
        }

        // What a text attribute of the node's text is: the font, its size, weight and slant, and the colours.
        private void ApplyStyle(AccessibilityNode node, CssBox box)
        {
            var font = box.ActualFont;
            node.FontFamily = font.Typeface?.FamilyName;
            node.FontSize = font.Size;
            // The weight of an anonymous text box is stepped again from its parent's when the keyword is relative (bolder inside <b> comes out as 900), so the
            // element's own is used.
            var weighted = box.HtmlTag is null && box.ParentBox is { } element ? element : box;
            node.FontWeight = (int)Math.Clamp(Math.Round(weighted.ActualNumericWeight), 1, 1000);
            node.IsItalic = font.Typeface?.IsItalic == true || ((int)font.SyntheticStyle & 2) != 0;
            // A decoration set on an element is drawn on the text inside it, so it is looked for on the way up.
            for (var decorated = box; decorated is not null; decorated = decorated.ParentBox)
            {
                var line = decorated.TextDecorationLine;
                if (string.IsNullOrEmpty(line) || line == Keywords.None)
                    continue;

                node.Underline |= line.Contains("underline", StringComparison.Ordinal);
                node.Strikethrough |= line.Contains("line-through", StringComparison.Ordinal);
            }

            node.ForegroundColor = Argb(box.ActualColor);
            node.BackgroundColor = box.ActualBackgroundColor is { A: > 0 } background ? Argb(background) : null;
        }

        private static int Argb(PeachDrawing.Core.PaintColor color) => (color.A << 24) | (color.R << 16) | (color.G << 8) | color.B;

        private void AddText(CssBox box, AccessibilityNode parent)
        {
            var text = Collapse(OwnText(box));
            if (text.Length == 0)
                return;

            _textCounts.TryGetValue(parent, out var index);
            _textCounts[parent] = index + 1;
            var node = new AccessibilityNode(parent.Id + ".t" + index.ToString(CultureInfo.InvariantCulture), AccessibilityRole.Text)
            {
                Name = text,
                Text = text,
                Language = parent.Language,
            };

            ApplyStyle(node, box);

            if (_bounds.TryGetValue(box, out var bounds))
            {
                foreach (var b in bounds)
                    node.AddBounds(b);
            }

            parent.AddChild(node);
        }

        // ---- helpers -----------------------------------------------------------------------------------------------------------------

        private static bool IsTag(HtmlTag tag, string name) => tag.Name.Equals(name, StringComparison.OrdinalIgnoreCase);

        private static bool IsTrue(HtmlTag tag, string attribute) =>
            string.Equals(tag.TryGetAttribute(attribute)?.Trim(), "true", StringComparison.OrdinalIgnoreCase);

        private static string? NonEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

        private static string Collapse(string? text) => string.IsNullOrEmpty(text) ? string.Empty : WhiteSpace().Replace(text, " ").Trim();

        [GeneratedRegex(@"\s+")]
        private static partial Regex WhiteSpace();

        // Whether the author gave the element a name of its own: what turns a section or a form into a landmark.
        private static bool HasAuthorName(HtmlTag tag) =>
            NonEmpty(tag.TryGetAttribute("aria-label")) is not null || NonEmpty(tag.TryGetAttribute("aria-labelledby")) is not null
            || NonEmpty(tag.TryGetAttribute("title")) is not null;

        private static bool IsFocusable(HtmlTag tag, in SemanticContext context)
        {
            if (int.TryParse(tag.TryGetAttribute("tabindex"), NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                return true;

            var name = tag.Name.ToLowerInvariant();
            var disabled = tag.HasAttribute("disabled") || context.FieldsetDisabled;
            return name switch
            {
                "a" or "area" => tag.HasAttribute("href"),
                "button" or "select" or "textarea" => !disabled,
                "input" => !disabled && !string.Equals(tag.TryGetAttribute("type"), "hidden", StringComparison.OrdinalIgnoreCase),
                "summary" => true,
                _ => tag.HasAttribute("contenteditable") && !string.Equals(tag.TryGetAttribute("contenteditable"), "false", StringComparison.OrdinalIgnoreCase),
            };
        }
    }
}
