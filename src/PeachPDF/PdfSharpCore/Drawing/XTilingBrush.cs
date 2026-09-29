namespace PeachPDF.PdfSharpCore.Drawing
{
    /// <summary>
    /// A brush that repeats a Form XObject, or an image, across the plane as a PDF tiling pattern (PDF 32000-1 §8.7.3): the tile is
    /// written once however many cells show it, and a form stays vector content.
    /// </summary>
    internal sealed class XTilingBrush : XBrush
    {
        /// <param name="tile">the tile: drawn once per cell</param>
        /// <param name="cellWidth">the cell's width in points</param>
        /// <param name="cellHeight">the cell's height in points</param>
        /// <param name="matrix">maps brush space (top-left origin, y down, in points) onto the user space the brush is painted in</param>
        /// <param name="interpolate">for an image tile, whether a viewer may smooth it when scaling; null keeps the image's own setting</param>
        public XTilingBrush(XImage tile, double cellWidth, double cellHeight, XMatrix matrix, bool? interpolate = null)
        {
            Tile = tile;
            Interpolate = interpolate;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Matrix = matrix;
        }

        public XImage Tile { get; }

        public bool? Interpolate { get; }

        public double CellWidth { get; }

        public double CellHeight { get; }

        public XMatrix Matrix { get; }
    }
}
