namespace PeachPDF
{
    /// <summary>
    /// A resolved size in points, handed to a dynamic content callback (a <c>Func&lt;PdfSize,byte[]&gt;</c>/
    /// <c>Func&lt;PdfSize,string&gt;</c> passed to <c>IContainer.Image</c>/<c>IContainer.Svg</c>) so it can
    /// generate content at exactly the size the container it's placed in already resolves to, rather than
    /// a guessed fixed size - see those methods' own doc comments for the size-resolution rules.
    /// </summary>
    public readonly struct PdfSize
    {
        /// <summary>The width, in points.</summary>
        public double Width { get; }

        /// <summary>The height, in points.</summary>
        public double Height { get; }

        /// <summary>Creates a size of <paramref name="width"/> by <paramref name="height"/> points.</summary>
        public PdfSize(double width, double height)
        {
            Width = width;
            Height = height;
        }

        /// <summary>Returns this size formatted as <c>"{Width}pt x {Height}pt"</c>.</summary>
        public override string ToString() => $"{Width}pt x {Height}pt";
    }
}
