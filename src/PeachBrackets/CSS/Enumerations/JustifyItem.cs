namespace PeachBrackets.Css
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

        /// <summary><c>baseline</c> / <c>first baseline</c> (CSS Box Alignment 3 §9.3).</summary>
        Baseline,

        /// <summary><c>last baseline</c>.</summary>
        LastBaseline,

        /// <summary>
        /// <c>justify-self</c> only - defers to the container's <c>justify-items</c> (CSS Box Alignment 3
        /// §5.3). Not a valid <c>justify-items</c> value; never produced by <c>Map.JustifyItemKeywords</c>,
        /// only by <c>Map.JustifySelfKeywords</c>.
        /// </summary>
        Auto
    }
}
