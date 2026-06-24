// "Therefore those skilled at the unorthodox
// are infinite as heaven and earth,
// inexhaustible as the great rivers.
// When they come to an end,
// they begin again,
// like the days and months;
// they die and are reborn,
// like the four seasons."
// 
// - Sun Tsu,
// "The Art of War"

namespace PeachPDF.Html.Core.Entities
{
    /// <summary>
    /// Holds data on link element in HTML.<br/>
    /// Used to expose data outside of HTML Renderer internal structure.
    /// </summary>
    internal sealed class LinkElementData<T>
    {
        /// <summary>
        /// Init.
        /// </summary>
        public LinkElementData(string id, string href, T rectangle)
        {
            Id = id;
            Href = href;
            Rectangle = rectangle;
        }

        /// <summary>
        /// the id of the link element if present
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// the href data of the link
        /// </summary>
        public string Href { get; }

        /// <summary>
        /// the rectangle of element as calculated by html layout
        /// </summary>
        public T Rectangle { get; }

        /// <summary>
        /// Is the link is directed to another element in the html
        /// </summary>
        public bool IsAnchor => Href.Length > 0 && Href[0] == '#';

        /// <summary>
        /// Return the id of the element this anchor link is referencing.
        /// </summary>
        public string AnchorId => IsAnchor && Href.Length > 1 ? Href[1..] : string.Empty;

        public override string ToString()
        {
            return $"Id: {Id}, Href: {Href}, Rectangle: {Rectangle}";
        }
    }
}