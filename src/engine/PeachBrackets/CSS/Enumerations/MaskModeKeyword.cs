namespace PeachBrackets.Css
{
    /// <summary><c>mask-mode</c> (CSS Masking 1 section 6.2): which channel of a mask layer supplies its mask values.</summary>
    internal enum MaskModeKeyword : byte
    {
        MatchSource,
        Alpha,
        Luminance
    }

    /// <summary><c>mask-clip</c>'s keywords: the painting area a mask layer is clipped to, or <c>no-clip</c>.</summary>
    internal enum MaskClipKeyword : byte
    {
        BorderBox,
        PaddingBox,
        ContentBox,
        FillBox,
        StrokeBox,
        ViewBox,
        NoClip
    }

    /// <summary><c>mask-composite</c>: how a mask layer combines with the layers beneath it.</summary>
    internal enum MaskCompositeKeyword : byte
    {
        Add,
        Subtract,
        Intersect,
        Exclude
    }
}
