
namespace PeachPDF.CSS
{
    internal enum AlignItem : byte
    {
        Normal,
        Stretch,
        Center,
        Start,
        End,
        FlexStart,
        FlexEnd,
        SelfStart,
        SelfEnd,
        Baseline,

        /// <summary>
        /// <c>align-self</c> only - defers to the container's <c>align-items</c> (CSS Box Alignment 3
        /// §5.3). Not a valid <c>align-items</c> value; never produced by <c>Map.AlignItemKeywords</c>,
        /// only by <c>Map.AlignSelfKeywords</c>.
        /// </summary>
        Auto
    }
}
