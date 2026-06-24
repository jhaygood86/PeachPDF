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

        public PageSelector(string name)
        {
            _name = name;
        }

        public PageSelector() : this(string.Empty)
        {
        }

        public override void ToCss(TextWriter writer, IStyleFormatter formatter)
        {
            var pseudo = _name == string.Empty ? "" : ":";
            writer.Write($"{pseudo}{_name}");
        }

        public Priority Specificity => Priority.Inline;
        public string Text => this.ToCss();
    }
}