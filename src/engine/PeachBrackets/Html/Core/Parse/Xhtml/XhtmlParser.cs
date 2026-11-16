#nullable enable

using PeachBrackets.Html.Dom;
using PeachPDF;
using PeachBrackets.Html.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml;

namespace PeachBrackets.Html.Parse
{
    /// <summary>
    /// Builds the box tree of an XHTML document with an XML parser, as a browser does for a document served as <c>application/xhtml+xml</c>: the
    /// document must be well-formed (an error stops the parse), names are case-sensitive, an element is empty exactly when the markup says so, a CDATA
    /// section is text, no <c>html</c>, <c>head</c>, <c>body</c> or table section is made up, and the document is never in quirks mode. The tree it
    /// returns is the one <see cref="HtmlParser.ParseDocument(string, out DocumentMode, CssBox?, bool)"/> returns, so the cascade and layout read it
    /// the same way.
    /// </summary>
    internal static class XhtmlParser
    {
        internal const string XhtmlNamespace = "http://www.w3.org/1999/xhtml";
        private const string SvgNamespace = "http://www.w3.org/2000/svg";
        private const string MathNamespace = "http://www.w3.org/1998/Math/MathML";
        private const string XlinkNamespace = "http://www.w3.org/1999/xlink";
        private const string XmlNamespace = "http://www.w3.org/XML/1998/namespace";
        private const string XmlnsNamespace = "http://www.w3.org/2000/xmlns/";

        // The most characters entity references may add: no real document needs a million characters from entities, and a text run that long costs a
        // great deal to shape.
        private const long MaxCharactersFromEntities = 1_000_000;

        /// <summary>
        /// Parses <paramref name="source"/> as an XHTML document.
        /// </summary>
        /// <param name="source">the document</param>
        /// <param name="mode">always <see cref="DocumentMode.NoQuirks"/>: an XML document is never in quirks mode</param>
        /// <param name="root">the root box (null for a new one)</param>
        /// <returns>the synthetic root box, whose child is the <c>html</c> element</returns>
        /// <exception cref="HtmlRenderException">the document is not well-formed XML, or its document element is not <c>html</c></exception>
        public static CssBox ParseDocument(string source, out DocumentMode mode, CssBox? root = null)
        {
            ArgumentNullException.ThrowIfNull(source);

            mode = DocumentMode.NoQuirks;
            root ??= CssBox.CreateBlock();

            if (source.StartsWith('﻿')) source = source[1..];

            var settings = new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Parse,
                XmlResolver = new XhtmlEntityResolver(),
                ValidationType = ValidationType.None,
                IgnoreComments = true,
                IgnoreProcessingInstructions = true,
                MaxCharactersFromEntities = MaxCharactersFromEntities,
                CloseInput = true,
            };

            try
            {
                using var reader = XmlReader.Create(new StringReader(WithExternalSubset(source)), settings);
                Build(reader, root);
            }
            catch (XmlException exception)
            {
                throw new HtmlRenderException(
                    $"The document is not well-formed XML: {exception.Message}", HtmlRenderErrorType.HtmlParsing, exception);
            }

            return root;
        }

        private static void Build(XmlReader reader, CssBox root)
        {
            var current = root;
            var text = new StringBuilder();
            var sawDocumentElement = false;

            void FlushText()
            {
                if (text.Length == 0) return;

                var run = text.ToString();
                text.Clear();

                // Only white space can stand outside the document element, and there is no box for it.
                if (ReferenceEquals(current, root) || current.TryAbsorbForeignText(run)) return;

                CssBox.CreateBox(current).Text = run;
            }

            while (reader.Read())
            {
                switch (reader.NodeType)
                {
                    case XmlNodeType.Text:
                    case XmlNodeType.CDATA:
                    case XmlNodeType.Whitespace:
                    case XmlNodeType.SignificantWhitespace:
                        text.Append(reader.Value);
                        break;

                    case XmlNodeType.Element:
                    {
                        FlushText();

                        var isEmpty = reader.IsEmptyElement;
                        var ns = reader.NamespaceURI;
                        var isHtml = ns.Length == 0 || ns == XhtmlNamespace;

                        // An <svg> or <math> with no namespace of its own (an author who left off the xmlns) is read as inline SVG or MathML, as the HTML
                        // parser does, so what is inside it keeps the case it was written in (<linearGradient>, viewBox): the SVG and MathML builders match
                        // those names exactly. A browser would make it an unknown element; foreignObject takes HTML again.
                        var inForeign = current.IsWithinForeignContent && current.HtmlTag?.Name != "foreignObject";
                        var lowercase = isHtml && !inForeign;

                        if (!sawDocumentElement)
                        {
                            sawDocumentElement = true;
                            if (!(isHtml && string.Equals(reader.LocalName, "html", StringComparison.Ordinal)))
                            {
                                throw new HtmlRenderException(
                                    $"The document element of an XHTML document must be <html> in the XHTML namespace, not <{reader.Name}>.",
                                    HtmlRenderErrorType.HtmlParsing);
                            }
                        }

                        var tag = new HtmlTag(NameOf(reader, ns, lowercase), isEmpty, AttributesOf(reader, lowercase));
                        var box = IsBoxFactoryName(ns, isHtml, reader.LocalName) ? CssBox.CreateBox(tag, current) : new CssBox(current, tag);
                        HtmlParser.EnsureWithinMaxDepth(box);

                        if (!isEmpty) current = box;
                        break;
                    }

                    case XmlNodeType.EndElement:
                        FlushText();
                        current = current.ParentBox ?? root;
                        break;
                }
            }

            FlushText();
        }

        // The tag name a box carries: lowercase for HTML (the engine compares them that way), as written for SVG and MathML, qualified for anything else.
        private static string NameOf(XmlReader reader, string ns, bool lowercase) =>
            lowercase ? reader.LocalName.ToLowerInvariant()
            : ns.Length == 0 || ns == XhtmlNamespace ? reader.LocalName
            : ns is SvgNamespace or MathNamespace ? reader.LocalName
            : reader.Name;

        // CssBox.CreateBox picks a box class from the tag name alone, so a name that only looks like <img> or <input> in another namespace must not use it.
        private static bool IsBoxFactoryName(string ns, bool isHtml, string localName) =>
            isHtml || (ns == SvgNamespace && localName == HtmlConstants.Svg) || (ns == MathNamespace && localName == HtmlConstants.Math);

        private static Dictionary<string, string>? AttributesOf(XmlReader reader, bool lowercase)
        {
            if (!reader.HasAttributes) return null;

            var attributes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            string? xmlLang = null;

            for (var more = reader.MoveToFirstAttribute(); more; more = reader.MoveToNextAttribute())
            {
                var name = reader.NamespaceURI switch
                {
                    "" => reader.LocalName,
                    XlinkNamespace => "xlink:" + reader.LocalName,
                    XmlNamespace => "xml:" + reader.LocalName,
                    XmlnsNamespace => reader.Name,
                    _ => reader.Name,
                };

                // An HTML attribute name is lowercase; an SVG or MathML one keeps its case (viewBox).
                if (lowercase && reader.NamespaceURI.Length == 0) name = name.ToLowerInvariant();

                attributes.TryAdd(name, reader.Value);
                if (name == "xml:lang") xmlLang = reader.Value;
            }

            reader.MoveToElement();

            // The language is read from lang; an XHTML document says it with xml:lang as well or instead.
            if (xmlLang is not null) attributes.TryAdd("lang", xmlLang);

            return attributes;
        }

        /// <summary>
        /// Gives a DOCTYPE with no external subset the one that holds the named character entities, so <c>&amp;nbsp;</c> works in a document
        /// that says <c>&lt;!DOCTYPE html&gt;</c> or names only a public identifier, as it does in a browser. A document with no DOCTYPE, or one
        /// that names its own system identifier, is left as it is (the resolver maps a DTD address to the same list).
        /// </summary>
        internal static string WithExternalSubset(string source)
        {
            var i = 0;

            // The prolog: white space, the XML declaration and other processing instructions, and comments.
            while (i < source.Length)
            {
                if (char.IsWhiteSpace(source[i])) i++;
                else if (At(source, i, "<?")) i = SkipTo(source, i + 2, "?>");
                else if (At(source, i, "<!--")) i = SkipTo(source, i + 4, "-->");
                else break;
            }

            if (!At(source, i, "<!DOCTYPE")) return source;

            i += "<!DOCTYPE".Length;
            i = SkipWhitespace(source, i);

            // The name.
            while (i < source.Length && !char.IsWhiteSpace(source[i]) && source[i] is not ('[' or '>')) i++;
            i = SkipWhitespace(source, i);

            const string external = " SYSTEM \"" + XhtmlEntityResolver.EntitiesUri + "\" ";

            if (i < source.Length && source[i] is '[' or '>')
                return source.Insert(i, external);

            if (At(source, i, "PUBLIC"))
            {
                i = SkipWhitespace(source, i + "PUBLIC".Length);
                var afterPublicId = SkipLiteral(source, i);
                if (afterPublicId < 0) return source;

                var next = SkipWhitespace(source, afterPublicId);
                // A system literal follows: the document names its own.
                if (next < source.Length && source[next] is '"' or '\'') return source;

                return source.Insert(afterPublicId, " \"" + XhtmlEntityResolver.EntitiesUri + "\"");
            }

            return source;
        }

        private static bool At(string source, int index, string text) =>
            string.CompareOrdinal(source, index, text, 0, text.Length) == 0;

        private static int SkipTo(string source, int index, string end)
        {
            var at = source.IndexOf(end, index, StringComparison.Ordinal);
            return at < 0 ? source.Length : at + end.Length;
        }

        private static int SkipWhitespace(string source, int index)
        {
            while (index < source.Length && char.IsWhiteSpace(source[index])) index++;
            return index;
        }

        // The index after a quoted literal that starts at index, or -1 when there is none.
        private static int SkipLiteral(string source, int index)
        {
            if (index >= source.Length || source[index] is not ('"' or '\'')) return -1;

            var close = source.IndexOf(source[index], index + 1);
            return close < 0 ? -1 : close + 1;
        }
    }
}
