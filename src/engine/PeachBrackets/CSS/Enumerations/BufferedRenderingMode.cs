namespace PeachBrackets.Css
{
    /// <summary>The <c>buffered-rendering</c> keywords (SVG Tiny 1.2 section 11.2). A caching hint: it never changes what is drawn.</summary>
    internal enum BufferedRenderingMode : byte
    {
        Auto,
        Dynamic,
        Static
    }
}
