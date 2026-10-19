using System.Collections.Generic;

namespace PeachBrackets.Css
{
    internal interface IStylesheetNode : IStyleFormattable
    {
        IEnumerable<IStylesheetNode> Children { get; }
        StylesheetText StylesheetText { get; }
    }
}