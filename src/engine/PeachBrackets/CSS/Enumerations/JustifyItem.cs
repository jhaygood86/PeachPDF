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
        Auto,

        /// <summary>
        /// <c>justify-items</c> only - <c>legacy</c>, the property's initial value (CSS Box Alignment 3 §6.1):
        /// <c>normal</c> in grid layout (flex containers ignore <c>justify-items</c>), and the container's
        /// alignment passed down to a block-level child's <c>justify-self: auto</c> in a block container
        /// (not reachable here: block-level <c>justify-self</c> is not applied).
        /// </summary>
        Legacy,

        /// <summary><c>legacy left</c>: <c>legacy</c> plus the companion keyword.</summary>
        LegacyLeft,

        /// <summary><c>legacy right</c>.</summary>
        LegacyRight,

        /// <summary><c>legacy center</c>.</summary>
        LegacyCenter
    }
}
