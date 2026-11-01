namespace PeachDrawing.PDF
{
    /// <summary>Which pages of one PDF are drawn over (or under) which pages of another, for <see cref="PdfOperation.OverlayFile"/> and <see cref="PdfOperation.UnderlayFile"/>.</summary>
    public sealed class PdfLayerConfiguration
    {
        /// <summary>The PDF to draw from, as a file path. Give this, <see cref="Data"/> or <see cref="Stream"/>.</summary>
        public string? FilePath { get; set; }

        /// <summary>The PDF to draw from, as its bytes.</summary>
        public byte[]? Data { get; set; }

        /// <summary>The PDF to draw from, as a stream that is read to the end and left open.</summary>
        public System.IO.Stream? Stream { get; set; }

        /// <summary>The password of the layer's PDF, if it has one.</summary>
        public string? Password { get; set; }

        /// <summary>The pages of the document that get the layer, as a <see cref="PdfPageSelector"/> selector; every page when <see langword="null"/>.</summary>
        public string? TargetPages { get; set; }

        /// <summary>The pages of the layer's PDF to draw, one for each target page in turn; all of them in order when <see langword="null"/>.</summary>
        public string? SourcePages { get; set; }

        /// <summary>The pages of the layer's PDF drawn, cycling, on the target pages left once <see cref="SourcePages"/> runs out; none when <see langword="null"/>.</summary>
        public string? RepeatSourcePages { get; set; }
    }
}
