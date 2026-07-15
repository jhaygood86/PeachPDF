using System.Collections.Generic;
using System.IO;

namespace PeachPDF.CSS
{
    internal sealed class KeyframeSelector : StylesheetNode
    {
        private readonly List<Percent> _stops;

        public KeyframeSelector(IEnumerable<Percent> stops)
        {
            _stops = new List<Percent>(stops);
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            if (_stops.Count <= 0) return;

            writer.Write(_stops[0].ToString());
            for (var i = 1; i < _stops.Count; i++)
            {
                writer.Write(", ");
                writer.Write(_stops[i].ToString());
            }
        }

        public IEnumerable<Percent> Stops => _stops;
        public string Text => this.ToCss();
    }

    internal sealed class PageSelector : StylesheetNode, ISelector
    {
        private readonly string _name;
        private readonly bool _isPseudo;

        /// <param name="name">
        /// For a pseudo-class selector (<paramref name="isPseudo"/> true, the default — e.g. <c>:first</c>,
        /// <c>:left</c>, <c>:right</c>), the pseudo-class name without its leading colon. For a named-page
        /// selector (<paramref name="isPseudo"/> false — e.g. <c>@page chapter</c>, or a comma-separated
        /// list <c>@page chapter1, chapter2</c>), the raw page name(s) as written, joined with ", " if more
        /// than one — never colon-prefixed, since CSS page names are plain custom-idents, not pseudo-classes.
        /// </param>
        /// <param name="isPseudo">Whether <paramref name="name"/> is a pseudo-class name (colon-prefixed in <see cref="Text"/>) rather than a named-page identifier.</param>
        public PageSelector(string name, bool isPseudo = true)
        {
            _name = name;
            _isPseudo = isPseudo;
        }

        public PageSelector() : this(string.Empty)
        {
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var pseudo = _name != string.Empty && _isPseudo ? ":" : "";
            writer.Write($"{pseudo}{_name}");
        }

        public Priority Specificity => Priority.Inline;
        public string Text => this.ToCss();
    }
}