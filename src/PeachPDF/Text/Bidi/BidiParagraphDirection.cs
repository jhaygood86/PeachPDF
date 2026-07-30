namespace PeachPDF.Text.Bidi
{
    /// <summary>
    /// The paragraph embedding-direction input to <see cref="BidiResolver.Resolve"/> - maps 1:1 from CSS
    /// <c>direction</c> for a normal block (<c>Ltr</c>/<c>Rtl</c>), or drives UAX #9 P2/P3 content-based
    /// detection for <c>unicode-bidi: plaintext</c> or HTML <c>dir="auto"</c> (<c>Auto</c>).
    /// </summary>
    internal enum BidiParagraphDirection : byte
    {
        Ltr,
        Rtl,
        Auto
    }
}
