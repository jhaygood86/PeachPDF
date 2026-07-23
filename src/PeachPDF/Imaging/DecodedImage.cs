namespace PeachPDF.Imaging
{
    /// <summary>
    /// A decoded raster image, normalized to 32bpp RGBA regardless of which decoder (native OS codec or
    /// STB) produced it, so encoders never need to know the image's provenance.
    /// </summary>
    internal readonly struct DecodedImage
    {
        public DecodedImage(byte[] rgba, int width, int height, bool hasAlpha)
        {
            Rgba = rgba;
            Width = width;
            Height = height;
            HasAlpha = hasAlpha;
        }

        /// <summary>
        /// Builds a <see cref="DecodedImage"/> from a tightly-packed RGBA buffer, computing
        /// <see cref="HasAlpha"/> from the real decoded alpha channel rather than sniffing the source
        /// container format - the one shared implementation every decoder (native or STB) uses.
        /// </summary>
        public static DecodedImage FromRgba(byte[] rgba, int width, int height)
        {
            var hasAlpha = false;
            for (var i = 3; i < rgba.Length; i += 4)
            {
                if (rgba[i] < 255)
                {
                    hasAlpha = true;
                    break;
                }
            }

            return new DecodedImage(rgba, width, height, hasAlpha);
        }

        /// <summary>
        /// Pixel data, 4 bytes per pixel (R, G, B, A), row-major, top-to-bottom, no padding between rows.
        /// </summary>
        public byte[] Rgba { get; }

        public int Width { get; }

        public int Height { get; }

        /// <summary>
        /// True if any pixel's alpha byte is less than 255 - computed from the actual decoded pixel data
        /// rather than sniffed from the source container format.
        /// </summary>
        public bool HasAlpha { get; }
    }
}
