namespace PeachDrawing.Text.Unicode
{
    /// <summary>
    /// The direction a paragraph is laid out in before its content says otherwise: the input to UAX #9 rules
    /// P2 and P3.
    /// </summary>
    /// <remarks>
    /// <see cref="Ltr"/> and <see cref="Rtl"/> map one-to-one onto the CSS <c>direction</c> property.
    /// <see cref="Auto"/> is what CSS <c>unicode-bidi: plaintext</c> and the HTML <c>dir="auto"</c> attribute ask for.
    /// </remarks>
    public enum BaseDirection : byte
    {
        /// <summary>The paragraph is left to right.</summary>
        Ltr,

        /// <summary>The paragraph is right to left.</summary>
        Rtl,

        /// <summary>The direction of the paragraph's first strongly directional character decides, or left to right when it has none.</summary>
        Auto
    }
}
