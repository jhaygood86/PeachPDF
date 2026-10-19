using System.IO;

namespace PeachBrackets.Css
{
    internal interface IStyleFormattable
    {
        void ToCss(TextWriter writer, IStyleFormatter formatter);
    }
}