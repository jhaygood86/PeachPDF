#nullable enable

namespace PeachPDF.CSS
{
    /// <summary>
    /// Legacy vendor-prefixed <em>keyword values</em> (<c>display: -webkit-flex</c>, <c>position: -webkit-sticky</c>)
    /// that mean exactly their standard keyword. The CSS-OM accepts them (<see cref="Map"/>); the box layer compares
    /// display/position by keyword text, so they are rewritten to the standard keyword before reaching it.
    /// </summary>
    internal static class VendorValueAliases
    {
        internal static string Normalize(string propertyName, string value)
        {
            if (value.Length == 0 || value[0] != '-') return value;

            if (propertyName.Isi(PropertyNames.Display))
            {
                if (value.Isi(Keywords.WebkitFlex)) return Keywords.Flex;
                if (value.Isi(Keywords.WebkitInlineFlex)) return Keywords.InlineFlex;
            }
            else if (propertyName.Isi(PropertyNames.Position) && value.Isi(Keywords.WebkitSticky))
            {
                return Keywords.Sticky;
            }

            return value;
        }
    }
}
