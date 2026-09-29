using System;
using System.Numerics;

namespace PeachDrawing.Core
{
    /// <summary>
    /// A paint that repeats a small picture, the <em>tile</em>, across the whole plane: the picture is laid out in a grid of cells
    /// and each cell shows it once. A shape filled or stroked with it shows whatever part of the grid lies under it, so adjacent
    /// shapes filled with the same brush continue the same pattern.
    /// </summary>
    /// <remarks>
    /// Draw the tile onto the canvas returned by <see cref="Canvas.CreateTile"/> and pass the <see cref="Image"/> it returns. On a
    /// canvas that keeps drawings as vector content (a PDF), the tile stays vector content: one copy, however many cells show it.
    /// </remarks>
    public sealed class TileBrush : Brush
    {
        /// <summary>Creates a brush repeating <paramref name="tile"/> in cells <paramref name="cellWidth"/> by <paramref name="cellHeight"/>.</summary>
        /// <param name="tile">the picture shown in every cell</param>
        /// <param name="cellWidth">the width of a cell, in brush space; the tile is stretched to fit</param>
        /// <param name="cellHeight">the height of a cell, in brush space; the tile is stretched to fit</param>
        /// <param name="transform">maps brush space onto the canvas's user space at the moment of painting: it moves, scales, rotates or
        /// skews the whole grid. In brush space one cell has its top-left corner at the origin.</param>
        /// <param name="sampling">how the tile's pixels are read when a cell is drawn at a size other than the tile's own; only meaningful for a tile made of pixels</param>
        /// <exception cref="ArgumentNullException"><paramref name="tile"/> is <see langword="null"/></exception>
        /// <exception cref="ArgumentOutOfRangeException">a cell dimension is not a positive, finite number</exception>
        public TileBrush(Image tile, double cellWidth, double cellHeight, Matrix3x2 transform, ImageSampling sampling = ImageSampling.Automatic)
        {
            ArgumentNullException.ThrowIfNull(tile);
            if (!(cellWidth > 0) || double.IsInfinity(cellWidth))
                throw new ArgumentOutOfRangeException(nameof(cellWidth));
            if (!(cellHeight > 0) || double.IsInfinity(cellHeight))
                throw new ArgumentOutOfRangeException(nameof(cellHeight));

            Tile = tile;
            CellWidth = cellWidth;
            CellHeight = cellHeight;
            Transform = transform;
            Sampling = sampling;
        }

        /// <summary>Creates a brush whose cells start at the origin of the user space, with no other transform.</summary>
        /// <param name="tile">the picture shown in every cell</param>
        /// <param name="cellWidth">the width of a cell</param>
        /// <param name="cellHeight">the height of a cell</param>
        public TileBrush(Image tile, double cellWidth, double cellHeight)
            : this(tile, cellWidth, cellHeight, Matrix3x2.Identity)
        {
        }

        /// <summary>The picture shown in every cell.</summary>
        public Image Tile { get; }

        /// <summary>The width of a cell, in brush space.</summary>
        public double CellWidth { get; }

        /// <summary>The height of a cell, in brush space.</summary>
        public double CellHeight { get; }

        /// <summary>Maps brush space onto the canvas's user space.</summary>
        public Matrix3x2 Transform { get; }

        /// <summary>How the tile's pixels are read when a cell is drawn at a size other than the tile's own.</summary>
        public ImageSampling Sampling { get; }
    }

    /// <summary>The line pattern of a <see cref="HatchBrush"/>.</summary>
    public enum HatchStyle
    {
        /// <summary>Horizontal lines.</summary>
        Horizontal,

        /// <summary>Vertical lines.</summary>
        Vertical,

        /// <summary>Lines running from the top-left to the bottom-right.</summary>
        ForwardDiagonal,

        /// <summary>Lines running from the bottom-left to the top-right.</summary>
        BackwardDiagonal,

        /// <summary>Horizontal and vertical lines.</summary>
        Cross,

        /// <summary>Lines in both diagonal directions.</summary>
        DiagonalCross,
    }

    /// <summary>A paint of evenly spaced lines in one colour over a background colour, like the hatching on a map.</summary>
    public sealed class HatchBrush : Brush
    {
        /// <summary>Creates a hatch.</summary>
        /// <param name="style">the pattern of lines</param>
        /// <param name="foreground">the colour of the lines</param>
        /// <param name="background">the colour between the lines; use a fully transparent colour to leave it unpainted</param>
        /// <param name="spacing">the distance between neighbouring lines, in user units, measured across a horizontal or vertical line (for a diagonal, along a horizontal)</param>
        /// <param name="lineWidth">the thickness of a line, in user units; a value that is not positive uses a tenth of <paramref name="spacing"/></param>
        /// <exception cref="ArgumentOutOfRangeException"><paramref name="spacing"/> is not a positive, finite number</exception>
        public HatchBrush(HatchStyle style, PaintColor foreground, PaintColor background, double spacing = 8, double lineWidth = 0)
        {
            if (!(spacing > 0) || double.IsInfinity(spacing))
                throw new ArgumentOutOfRangeException(nameof(spacing));

            Style = style;
            Foreground = foreground;
            Background = background;
            Spacing = spacing;
            LineWidth = lineWidth > 0 ? lineWidth : spacing / 10;
        }

        /// <summary>The pattern of lines.</summary>
        public HatchStyle Style { get; }

        /// <summary>The colour of the lines.</summary>
        public PaintColor Foreground { get; }

        /// <summary>The colour between the lines.</summary>
        public PaintColor Background { get; }

        /// <summary>The distance between neighbouring lines, in user units, measured across a horizontal or vertical line (for a diagonal, along a horizontal).</summary>
        public double Spacing { get; }

        /// <summary>The thickness of a line, in user units.</summary>
        public double LineWidth { get; }

        /// <summary>
        /// Draws one cell of this hatch onto a tile of <paramref name="canvas"/> and returns the brush that repeats it. A canvas that
        /// does not know how to paint a hatch directly calls this and paints the result.
        /// </summary>
        /// <param name="canvas">the canvas the hatch will be painted on</param>
        /// <returns>the tiling brush, or <see langword="null"/> when <paramref name="canvas"/> cannot make tiles (see <see cref="Canvas.CreateTile"/>)</returns>
        /// <exception cref="ArgumentNullException"><paramref name="canvas"/> is <see langword="null"/></exception>
        public TileBrush? ToTileBrush(Canvas canvas)
        {
            ArgumentNullException.ThrowIfNull(canvas);

            // Diagonal lines meet cell edges at 45 degrees, so a square cell one spacing wide tiles them without a seam.
            var cell = Spacing;
            if (canvas.CreateTile(cell, cell) is not { } tile)
                return null;

            var g = tile.Graphics;
            var w = LineWidth;
            if (Background.A > 0)
            {
                using var back = g.GetSolidBrush(Background);
                g.DrawRectangle(back, 0, 0, cell, cell);
            }

            using (var ink = g.GetSolidBrush(Foreground))
            {
                if (Style is HatchStyle.Horizontal or HatchStyle.Cross)
                    g.DrawRectangle(ink, 0, (cell - w) / 2, cell, w);

                if (Style is HatchStyle.Vertical or HatchStyle.Cross)
                    g.DrawRectangle(ink, (cell - w) / 2, 0, w, cell);

                if (Style is HatchStyle.ForwardDiagonal or HatchStyle.DiagonalCross)
                    DrawDiagonal(g, ink, cell, w, forward: true);

                if (Style is HatchStyle.BackwardDiagonal or HatchStyle.DiagonalCross)
                    DrawDiagonal(g, ink, cell, w, forward: false);
            }

            g.Dispose();
            return new TileBrush(tile.Image, cell, cell);
        }

        /// <summary>
        /// One diagonal across the cell, plus the two corner triangles that continue it into the neighbouring cells, so a run of cells
        /// shows an unbroken line.
        /// </summary>
        private static void DrawDiagonal(Canvas g, Brush ink, double cell, double w, bool forward)
        {
            // A band of horizontal thickness d around the diagonal is a line of perpendicular thickness d / sqrt(2)... so scale up.
            var d = w * Math.Sqrt(2);
            foreach (var shift in new[] { -cell, 0.0, cell })
            {
                PaintPoint[] band = forward
                    ?
                    [
                        new(shift - d / 2, 0), new(shift + d / 2, 0),
                        new(shift + cell + d / 2, cell), new(shift + cell - d / 2, cell),
                    ]
                    :
                    [
                        new(shift + cell - d / 2, 0), new(shift + cell + d / 2, 0),
                        new(shift + d / 2, cell), new(shift - d / 2, cell),
                    ];

                g.DrawPolygon(ink, band);
            }
        }
    }
}
