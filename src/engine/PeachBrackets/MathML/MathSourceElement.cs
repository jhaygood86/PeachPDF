using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using PeachDrawing.Core;
using PeachBrackets.Html;
using PeachBrackets.Html.Dom;
using PeachBrackets.Html.Utils;

namespace PeachBrackets.MathML
{
    /// <summary>
    /// One node of an inline <c>&lt;math&gt;</c>'s markup, held without a <see cref="CssBox"/>: an element (<see cref="Tag"/> set) or
    /// a run of text (<see cref="Tag"/> null). <see cref="MathTreeBuilder"/> only ever reads the names, attributes, text and
    /// colour of these, and a box per MathML element - with its own computed style - was about a third of the allocation of an
    /// equation-heavy document. <see cref="CssBoxMath"/> keeps the tree while the box is being built and settles it at the cascade:
    /// when no style rule, <c>style</c> attribute or <c>color</c> attribute can reach any element in it, every element's colour is
    /// the <c>&lt;math&gt;</c> box's own (<see cref="PaintColor"/>) and the tree stays as it is; otherwise it is turned into the
    /// boxes the parser would have built (<see cref="CssBoxMath.MaterializeSource()"/>).
    /// </summary>
    /// <remarks>
    /// The element for the <c>&lt;math&gt;</c> itself is the root of the tree (<see cref="IsRoot"/>): it carries the tag and
    /// holds the top-level nodes, but as a node of the document it is the <see cref="CssBoxMath"/>, which is what the
    /// parent of a top-level element answers.
    /// </remarks>
    internal sealed class MathSourceElement : IMathSourceNode, ICssDomNode
    {
        private List<MathSourceElement>? _children;
        private List<MathSourceElement>? _elements;
        private string? _text;

        internal MathSourceElement(HtmlTag? tag, CssBoxMath owner, MathSourceElement? parent, string? text = null)
        {
            Tag = tag;
            Owner = owner;
            ParentElement = parent;
            _text = text;
        }

        /// <summary>The element's tag, or null for a run of text.</summary>
        internal HtmlTag? Tag { get; }

        /// <summary>The <c>&lt;math&gt;</c> box this node belongs to.</summary>
        internal CssBoxMath Owner { get; }

        internal MathSourceElement? ParentElement { get; }

        /// <summary>Whether this is the <c>&lt;math&gt;</c> element's own node, which has no parent node.</summary>
        internal bool IsRoot => ParentElement is null;

        /// <summary>The text of a run of text, or the text a token element holds in place of a single child run.</summary>
        internal string? Text => _text;

        /// <summary>The child nodes in order, elements and runs of text alike (empty for a node with none).</summary>
        internal IReadOnlyList<MathSourceElement> ChildNodes => (IReadOnlyList<MathSourceElement>?)_children ?? [];

        /// <summary>The token elements, whose content is text and which keep a single run of it in <see cref="Text"/>.</summary>
        internal static bool IsTokenName(string? name) => name is "mi" or "mo" or "mn" or "mtext" or "ms";

        /// <summary>Appends an element. The text a token has been holding becomes its first child run first, as it would be a box.</summary>
        internal MathSourceElement AddElement(HtmlTag tag)
        {
            MaterializeText();

            var element = new MathSourceElement(tag, Owner, this);
            (_children ??= []).Add(element);
            _elements = null;

            return element;
        }

        /// <summary>Appends text: held in place when this is a token with no other content so far, otherwise as a run of its own.</summary>
        internal void AddText(string text)
        {
            if (Tag is not null && _children is null && IsTokenName(Tag.Name))
            {
                _text = _text is null ? text : _text + text;
                return;
            }

            (_children ??= []).Add(new MathSourceElement(null, Owner, this, text));
            _elements = null;
        }

        private void MaterializeText()
        {
            if (Tag is null || _text is null) return;

            (_children ??= []).Add(new MathSourceElement(null, Owner, this, _text));
            _text = null;
        }

        /// <summary>The text of every run of text directly under this node, or the token text it holds.</summary>
        public string GetTextContent()
        {
            if (_text is not null) return _text;
            if (_children is null) return string.Empty;

            return string.Concat(_children.Where(c => c.Tag is null).Select(c => c._text));
        }

        /// <summary>
        /// Appends the text of every run under this node in document order, each followed by <paramref name="after"/> when given: what a
        /// reader of a box's text (generated content, a named string) collects from the text boxes this tree stands for.
        /// </summary>
        internal void AppendRuns(StringBuilder builder, string? after)
        {
            if (!string.IsNullOrEmpty(_text))
            {
                builder.Append(_text);
                if (after is not null) builder.Append(after);
            }

            if (_children is null) return;

            foreach (var child in _children)
            {
                child.AppendRuns(builder, after);
            }
        }

        // ---- IMathSourceNode

        public string Name => Tag?.Name ?? string.Empty;

        public string? GetAttribute(string name) => Tag?.TryGetAttribute(name);

        public IEnumerable<KeyValuePair<string, string>> Attributes =>
            Tag?.Attributes ?? Enumerable.Empty<KeyValuePair<string, string>>();

        IEnumerable<IMathSourceNode> IMathSourceNode.Children => ElementChildren;

        /// <summary>The child elements only, in order. Cached: the builder and the serializer both walk it.</summary>
        internal IReadOnlyList<MathSourceElement> ElementChildren
        {
            get
            {
                if (_children is null) return [];
                if (_elements is not null) return _elements;

                return _elements = _children.Count(c => c.Tag is not null) == _children.Count
                    ? _children
                    : _children.Where(c => c.Tag is not null).ToList();
            }
        }

        public PaintColor PaintColor => Owner.ActualColor;

        public double FontSizePt => Owner.ActualFont.Size;

        public Font GetFontAtSize(double sizePt) => Owner.GetActualFontAtSize(sizePt);

        // ---- ICssDomNode

        string? ICssDomNode.TagName => Tag?.Name;

        // Foreign content: attribute names are case-sensitive here, as CssBox answers for a box inside <math>.
        string? ICssDomNode.GetAttribute(string name)
        {
            var attributes = Tag?.Attributes;
            if (attributes is null) return null;

            foreach (var attribute in attributes)
            {
                if (string.Equals(attribute.Key, name, StringComparison.Ordinal)) return attribute.Value;
            }

            return null;
        }

        StringComparison ICssDomNode.NameComparison => StringComparison.Ordinal;

        ICssDomNode? ICssDomNode.Parent => IsRoot ? Owner.ParentBox : ParentElement!.IsRoot ? Owner : ParentElement;

        IReadOnlyList<ICssDomNode> ICssDomNode.Children => ChildNodes;

        bool ICssDomNode.IsRoot => false;

        bool ICssDomNode.IsEmpty => IsEmpty;

        /// <summary>Selectors 4 §9.5's <c>:empty</c>: no element child, and no text but collapsible white space.</summary>
        internal bool IsEmpty
        {
            get
            {
                if (_text is not null && !HtmlUtils.IsNullOrCollapsibleWhitespace(_text)) return false;
                if (_children is null) return true;

                foreach (var child in _children)
                {
                    if (child.Tag is not null) return false;
                    if (!HtmlUtils.IsNullOrCollapsibleWhitespace(child._text)) return false;
                }

                return true;
            }
        }

        Dictionary<string, string>? ICssDomNode.CustomProperties
        {
            get => null;
            set { }
        }
    }
}
