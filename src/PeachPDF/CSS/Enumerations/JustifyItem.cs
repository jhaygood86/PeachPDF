namespace PeachPDF.CSS
{
    internal enum JustifyItem : byte
    {
        Normal,
        Stretch,
        Center,
        Start,
        End,
        SelfEnd,
        Left,
        Right,

        /// <summary>
        /// <c>justify-self</c> only - defers to the container's <c>justify-items</c> (CSS Box Alignment 3
        /// §5.3). Not a valid <c>justify-items</c> value; never produced by <c>Map.JustifyItemKeywords</c>,
        /// only by <c>Map.JustifySelfKeywords</c>.
        /// </summary>
        Auto
    }
}
