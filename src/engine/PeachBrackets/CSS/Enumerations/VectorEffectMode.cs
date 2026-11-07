namespace PeachBrackets.Css
{
    /// <summary>The <c>vector-effect</c> keywords (SVG 2 section 13.8). Only <see cref="NonScalingStroke"/> changes painting.</summary>
    internal enum VectorEffectMode : byte
    {
        None,
        NonScalingStroke,
        NonScalingSize,
        NonRotation,
        FixedPosition
    }
}
