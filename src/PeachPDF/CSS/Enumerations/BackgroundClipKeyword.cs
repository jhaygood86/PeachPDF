namespace PeachPDF.CSS
{
    /// <summary>
    /// <c>background-clip</c>'s own keyword grammar - the three <see cref="BoxModel"/> values plus
    /// <c>text</c>, which is valid only here and must not leak into <c>background-origin</c>/
    /// <c>box-sizing</c> (both of which share <see cref="BoxModel"/>/<c>Map.BoxModels</c> instead).
    /// </summary>
    internal enum BackgroundClipKeyword : byte
    {
        BorderBox,
        PaddingBox,
        ContentBox,
        Text
    }
}
