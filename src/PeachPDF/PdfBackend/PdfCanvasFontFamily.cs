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

using PeachDrawing.Core;
using PeachPDF.PDF.Drawing;

namespace PeachPDF.PdfBackend
{
    /// <summary>
    /// Adapter for WinForms Font object for core.
    /// </summary>
    internal sealed class PdfCanvasFontFamily : FontFamily
    {
        private readonly string _name;

        /// <summary>
        /// Init.
        /// </summary>
        public PdfCanvasFontFamily(string name)
        {
            _name = name;
        }

        public override string Name => _name;
    }
}