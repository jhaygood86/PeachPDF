namespace PeachDrawing.Core
{
    /// <summary>How the pixels of an image are read when it is drawn at a size other than its own.</summary>
    public enum ImageSampling
    {
        /// <summary>Whatever the image itself asks for (<see cref="Image.Interpolate"/>): smooth, or crisp when enlarged.</summary>
        Automatic = 0,

        /// <summary>
        /// Each output pixel takes the colour of the one source pixel it falls in. Enlarging gives hard-edged blocks (pixel art
        /// stays crisp); shrinking skips pixels and can look jagged.
        /// </summary>
        Nearest = 1,

        /// <summary>Neighbouring source pixels are blended by distance; shrinking averages the pixels it covers.</summary>
        Bilinear = 2,

        /// <summary>
        /// A sharper blend over the sixteen nearest source pixels (a Catmull-Rom curve). Enlarging keeps edges cleaner than
        /// <see cref="Bilinear"/>. A canvas without a bicubic filter of its own treats this as <see cref="Bilinear"/>.
        /// </summary>
        Bicubic = 3,

        /// <summary>
        /// Crisp when enlarging, like <see cref="Nearest"/> (pixel art stays as hard-edged blocks), but smooth when shrinking, where
        /// skipping pixels would only look jagged.
        /// </summary>
        Pixelated = 4,
    }
}
