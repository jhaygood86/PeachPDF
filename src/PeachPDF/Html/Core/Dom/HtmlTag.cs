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

using PeachPDF.Html.Core.Utils;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PeachPDF.Html.Core.Dom
{
    internal sealed class HtmlTag
    {
        /// <summary>
        /// Init.
        /// </summary>
        /// <param name="name">the name of the html tag</param>
        /// <param name="isSingle">if the tag is single placed; in other words it doesn't have a separate closing tag;</param>
        /// <param name="attributes">collection of attributes and their value the html tag has</param>
        public HtmlTag(string name, bool isSingle, Dictionary<string, string>? attributes = null)
        {
            ArgChecker.AssertArgNotNullOrEmpty(name, "name");

            Name = name;
            IsSingle = isSingle;
            Attributes = attributes?.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value);
        }

        /// <summary>
        /// Gets the name of this tag
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Gets collection of attributes and their value the html tag has
        /// </summary>
        public Dictionary<string, string>? Attributes { get; }

        /// <summary>
        /// Gets if the tag is single placed; in other words it doesn't have a separate closing tag; <br/>
        /// e.g. &lt;br&gt;
        /// </summary>
        public bool IsSingle { get; }

        /// <summary>
        /// is the html tag has attributes.
        /// </summary>
        /// <returns>true - has attributes, false - otherwise</returns>
        public bool HasAttributes()
        {
            return Attributes is not null && Attributes.Count > 0;
        }

        /// <summary>
        /// Gets a boolean indicating if the attribute list has the specified attribute
        /// </summary>
        /// <param name="attribute">attribute name to check if exists</param>
        /// <returns>true - attribute exists, false - otherwise</returns>
        public bool HasAttribute(string attribute)
        {
            return Attributes is not null && Attributes.ContainsKey(attribute);
        }

        /// <summary>
        /// Get attribute value for given attribute name or null if not exists.
        /// </summary>
        /// <param name="attribute">attribute name to get by</param>
        /// <param name="defaultValue">optional: value to return if attribute is not specified</param>
        /// <returns>attribute value or null if not found</returns>
        public string? TryGetAttribute(string attribute, string? defaultValue = null)
        {
            if (Attributes is not null && Attributes.TryGetValue(attribute, out var value))
            {
                return value;
            }

            return defaultValue;
        }

        public override string ToString()
        {
            if (Attributes is null)
            {
                return $"<{Name}>";
            }

            else
            {
                var sb = new StringBuilder();
                sb.Append('<').Append(Name);

                foreach (var attribute in Attributes)
                {
                    sb.Append(' ').Append(attribute.Key).Append("=\"").Append(attribute.Value).Append('"');
                }

                sb.Append('>');

                return sb.ToString();
            }
        }
    }
}