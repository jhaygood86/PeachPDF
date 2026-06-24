using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal interface IStylesheetNode : IStyleFormattable
    {
        IEnumerable<IStylesheetNode> Children { get; }
        StylesheetText StylesheetText { get; }
    }
}