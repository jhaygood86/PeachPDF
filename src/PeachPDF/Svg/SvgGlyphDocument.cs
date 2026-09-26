using PeachDrawing.Text;
using PeachDrawing.Text.Outlines;
using PeachPDF.Html.Adapters;
using PeachPDF.Html.Adapters.Entities;
using PeachPDF.Html.Core;
using PeachPDF.Html.Core.Parse;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace PeachPDF.Svg
{
    /// <summary>
    /// Turns the SVG document a font gives for a glyph (OpenType SVG, the <c>SVG </c> table) into an <see cref="SvgDocument"/> the SVG
    /// renderer can paint: the element that draws the glyph, with the palette colours the document asks for
    /// (<c>var(--color0)</c>) filled in and the text colour standing for <c>context-fill</c>, <c>context-stroke</c> and <c>currentColor</c>.
    /// </summary>
    /// <remarks>
    /// The document comes from a font file and is untrusted: it is parsed without DTDs or an external resolver and with a size cap, no
    /// external resource is fetched (only <c>data:</c> images resolve), and a document that cannot be parsed or built gives
    /// <see langword="null"/>, which the caller answers by drawing the glyph's outline.
    /// </remarks>
    internal static class SvgGlyphDocument
    {
        /// <summary>
        /// The drawing is built on a canvas of this many ems around the glyph origin: one em to its left, two to its right, an em and
        /// a half above it and half an em below, which holds any glyph a font would draw.
        /// </summary>
        internal const double CanvasLeftEms = 1;
        internal const double CanvasTopEms = 1.5;
        internal const double CanvasWidthEms = 3;
        internal const double CanvasHeightEms = 2;

        /// <summary>The deepest a document's elements may nest, and the most the tree may come to once <c>use</c> elements are expanded.</summary>
        internal const int MaxDepth = 48;
        internal const int MaxExpandedElements = 20_000;

        private static readonly Regex PaletteVariable = new(@"--color(\d+)", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex GlyphId = new(@"^glyph\d+$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
        private static readonly Regex ContextPaint = new(@"context-(fill|stroke)", RegexOptions.Compiled | RegexOptions.CultureInvariant);

        /// <summary>Builds the drawing for <paramref name="svg"/>, or returns <see langword="null"/> when it cannot be.</summary>
        /// <param name="svg">The font's document for the glyph.</param>
        /// <param name="palette">The colour of a <c>CPAL</c> entry, or <see langword="null"/> when it has none (the document's own fallback applies).</param>
        /// <param name="entryCount">How many palette entries there are: <c>--color0</c> up to one less.</param>
        /// <param name="foreground">The text colour.</param>
        /// <param name="adapter">The adapter that resolves the document's fonts and images.</param>
        internal static SvgDocument? Build(SvgGlyph svg, Func<int, RColor?> palette, int entryCount, RColor foreground, RAdapter adapter)
        {
            try
            {
                var settings = new XmlReaderSettings
                {
                    DtdProcessing = DtdProcessing.Prohibit,
                    XmlResolver = null,
                    MaxCharactersInDocument = 4 * 1024 * 1024,
                };

                XDocument xdoc;
                using (var reader = XmlReader.Create(new StringReader(svg.Document), settings))
                {
                    xdoc = XDocument.Load(reader);
                }

                var root = xdoc.Root;
                if (root is null)
                {
                    return null;
                }

                if (!KeepOnlyTheGlyph(root, svg) || !IsAffordable(root))
                {
                    return null;
                }

                MakeContextPaintTheTextColour(root);
                MoveVariableAttributesToStyle(root);
                SetTheCanvas(root, svg.UnitsPerEm);
                DefinePaletteColours(root, palette, entryCount);

                // The custom properties (--color0 ...) are on the root's style attribute, so the cascade has to run even when the
                // document has no stylesheet of its own.
                var cssData = SvgCssStyling.BuildStyleData(SvgCssStyling.CollectStyleText(root)) ?? new CssData();
                var valueParser = new CssValueParser(adapter);
                var registered = RegisteredProperty.BuildRegistry(cssData, valueParser);
                CssVarResolver.VarContext? varContext = registered.Count > 0 ? new CssVarResolver.VarContext(registered, valueParser) : null;

                SvgCssStyling.CascadeCustomProperties(root, cssData, "print", registered);
                var sourceNode = new XElementSvgSourceNode(root, root, cssData, "print", varContext);
                return SvgTreeBuilder.Build(sourceNode, adapter, foreground);
            }
            catch (Exception ex) when (ex is XmlException or InvalidOperationException or ArgumentException or FormatException)
            {
                return null;
            }
        }

        /// <summary>
        /// Removes what draws the other glyphs a shared document covers, so only the glyph asked for is drawn, and keeps the definitions
        /// (styles, gradients) the rest shares. A document for a single glyph with no element for it is kept whole.
        /// </summary>
        private static bool KeepOnlyTheGlyph(XElement root, SvgGlyph svg)
        {
            var target = root.DescendantsAndSelf().FirstOrDefault(e => (string?)e.Attribute("id") == svg.ElementId);
            if (target is null)
            {
                return svg.CoversOneGlyph;
            }

            var keep = new HashSet<XElement>(target.AncestorsAndSelf());

            // What the glyph draws through a <use> is kept too, however it is named.
            var byId = root.DescendantsAndSelf().Where(e => e.Attribute("id") is not null).GroupBy(e => (string)e.Attribute("id")!)
                .ToDictionary(g => g.Key, g => g.First());
            var pending = new Stack<XElement>(new[] { target });
            while (pending.Count > 0)
            {
                foreach (var use in pending.Pop().DescendantsAndSelf().Where(e => e.Name.LocalName == "use"))
                {
                    var href = (string?)use.Attribute("href") ?? (string?)use.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href");
                    if (href is { Length: > 1 } && href[0] == '#' && byId.TryGetValue(href[1..], out var referenced))
                    {
                        foreach (var ancestor in referenced.AncestorsAndSelf())
                        {
                            if (keep.Add(ancestor) && ancestor == referenced)
                            {
                                pending.Push(referenced);
                            }
                        }
                    }
                }
            }

            foreach (var other in root.Descendants().Where(e => e.Attribute("id") is { } id && GlyphId.IsMatch(id.Value) && !keep.Contains(e)).ToList())
            {
                other.Remove();
            }

            // A wrapper around the glyph that is not the glyph itself draws nothing else once its siblings are gone; nothing more to prune.
            return true;
        }

        private static readonly HashSet<string> PaintAttributes = new(StringComparer.Ordinal)
        {
            "fill", "stroke", "stop-color", "flood-color", "lighting-color", "color", "fill-opacity", "stroke-opacity", "opacity",
        };

        /// <summary>
        /// A font's document may write <c>fill="var(--color0, red)"</c> as a presentation attribute, which a cascade does not resolve
        /// <c>var()</c> in; the same declaration in the <c>style</c> attribute is what it means.
        /// </summary>
        private static void MoveVariableAttributesToStyle(XElement root)
        {
            foreach (var element in root.DescendantsAndSelf())
            {
                var moved = new StringBuilder();
                foreach (var attribute in element.Attributes().ToList())
                {
                    if (attribute.Name.Namespace == XNamespace.None && PaintAttributes.Contains(attribute.Name.LocalName)
                        && attribute.Value.Contains("var(", StringComparison.Ordinal))
                    {
                        moved.Append(attribute.Name.LocalName).Append(':').Append(attribute.Value).Append(';');
                        attribute.Remove();
                    }
                }

                if (moved.Length > 0)
                {
                    element.SetAttributeValue("style", moved + ((string?)element.Attribute("style") ?? string.Empty));
                }
            }
        }

        /// <summary>The text colour is what <c>context-fill</c> and <c>context-stroke</c> stand for when a glyph is drawn as text.</summary>
        private static void MakeContextPaintTheTextColour(XElement root)
        {
            foreach (var element in root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes().ToList())
                {
                    if (attribute.Name.Namespace == XNamespace.None && (PaintAttributes.Contains(attribute.Name.LocalName) || attribute.Name.LocalName == "style")
                        && attribute.Value.Contains("context-", StringComparison.Ordinal))
                    {
                        attribute.Value = ContextPaint.Replace(attribute.Value, "currentColor");
                    }
                }

                if (element.Name.LocalName == "style" && element.Value.Contains("context-", StringComparison.Ordinal))
                {
                    element.Value = ContextPaint.Replace(element.Value, "currentColor");
                }
            }
        }

        /// <summary>
        /// The drawing is in font units with the glyph origin at (0, 0) and y pointing down; the root's own size and view box are
        /// replaced by a canvas around the origin, and the renderer maps it to the em size the text is drawn at.
        /// </summary>
        private static void SetTheCanvas(XElement root, int unitsPerEm)
        {
            root.SetAttributeValue("viewBox", string.Format(CultureInfo.InvariantCulture, "{0} {1} {2} {3}",
                -CanvasLeftEms * unitsPerEm, -CanvasTopEms * unitsPerEm, CanvasWidthEms * unitsPerEm, CanvasHeightEms * unitsPerEm));
            root.SetAttributeValue("width", (CanvasWidthEms * unitsPerEm).ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("height", (CanvasHeightEms * unitsPerEm).ToString(CultureInfo.InvariantCulture));
            root.SetAttributeValue("preserveAspectRatio", null);
            if (root.Name.LocalName != "svg")
            {
                root.Name = root.Name.Namespace + "svg";
            }
        }

        private static void AddMentioned(string text, SortedSet<int> mentioned, int entryCount)
        {
            if (!text.Contains("--color", StringComparison.Ordinal))
            {
                return;
            }

            foreach (Match match in PaletteVariable.Matches(text))
            {
                if (int.TryParse(match.Groups[1].Value, NumberStyles.None, CultureInfo.InvariantCulture, out int index) && index < entryCount)
                {
                    mentioned.Add(index);
                }
            }
        }

        /// <summary>
        /// Refuses a document a font could use to make the renderer do unbounded work: elements nested too deeply for the recursive builder,
        /// or a tree that <c>use</c> elements expand exponentially.
        /// </summary>
        private static bool IsAffordable(XElement root)
        {
            var sizes = new Dictionary<XElement, long>();
            var byId = new Dictionary<string, XElement>(StringComparer.Ordinal);
            foreach (var element in root.DescendantsAndSelf())
            {
                if ((string?)element.Attribute("id") is { } id)
                {
                    byId.TryAdd(id, element);
                }
            }

            if (Depth(root, 0) > MaxDepth)
            {
                return false;
            }

            return Size(root, byId, sizes, new HashSet<XElement>()) <= MaxExpandedElements;
        }

        private static int Depth(XElement element, int depth)
        {
            int deepest = depth;
            foreach (var child in element.Elements())
            {
                deepest = Math.Max(deepest, Depth(child, depth + 1));
                if (deepest > MaxDepth)
                {
                    return deepest;
                }
            }

            return deepest;
        }

        private static long Size(XElement element, Dictionary<string, XElement> byId, Dictionary<XElement, long> sizes, HashSet<XElement> visiting)
        {
            if (sizes.TryGetValue(element, out long known))
            {
                return known;
            }

            if (!visiting.Add(element))
            {
                return MaxExpandedElements + 1;    // a use that reaches itself
            }

            long size = 1;
            foreach (var child in element.Elements())
            {
                size += Size(child, byId, sizes, visiting);
                if (size > MaxExpandedElements)
                {
                    break;
                }
            }

            if (element.Name.LocalName == "use" && size <= MaxExpandedElements)
            {
                var href = (string?)element.Attribute("href") ?? (string?)element.Attribute(XNamespace.Get("http://www.w3.org/1999/xlink") + "href");
                if (href is { Length: > 1 } && href[0] == '#' && byId.TryGetValue(href[1..], out var target))
                {
                    size += Size(target, byId, sizes, visiting);
                }
            }

            visiting.Remove(element);
            sizes[element] = size;
            return size;
        }

        private static void DefinePaletteColours(XElement root, Func<int, RColor?> palette, int entryCount)
        {
            // Only the entries the document mentions: the cascade copies its custom properties for every element, and a palette can have
            // tens of thousands of entries.
            var mentioned = new SortedSet<int>();
            foreach (var element in root.DescendantsAndSelf())
            {
                foreach (var attribute in element.Attributes())
                {
                    AddMentioned(attribute.Value, mentioned, entryCount);
                }

                if (element.Name.LocalName == "style")
                {
                    AddMentioned(element.Value, mentioned, entryCount);
                }
            }

            var css = new StringBuilder();
            foreach (int i in mentioned)
            {
                if (palette(i) is { } colour)
                {
                    css.Append("--color").Append(i.ToString(CultureInfo.InvariantCulture)).Append(": rgba(")
                        .Append(colour.R.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(colour.G.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append(colour.B.ToString(CultureInfo.InvariantCulture)).Append(',')
                        .Append((colour.A / 255.0).ToString("0.###", CultureInfo.InvariantCulture)).Append(");");
                }
            }

            if (css.Length == 0)
            {
                return;
            }

            var existing = (string?)root.Attribute("style");
            root.SetAttributeValue("style", css + (existing ?? string.Empty));
        }
    }
}
