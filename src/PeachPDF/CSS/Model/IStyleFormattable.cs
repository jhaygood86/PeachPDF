using System.IO;

namespace PeachPDF.CSS
{
    internal interface IStyleFormattable
    {
        void ToCss(TextWriter writer, IStyleFormatter formatter);
    }
}