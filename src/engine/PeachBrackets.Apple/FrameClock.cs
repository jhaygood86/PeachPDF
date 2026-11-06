namespace PeachBrackets.Apple;

/// <summary>The clock an animating document is stepped on: one tick a display frame, with the time in seconds from a fixed origin.</summary>
internal static class FrameClock
{
    /// <summary>The time now on the clock the ticks are measured against.</summary>
    public static TimeSpan Now => TimeSpan.FromSeconds(CoreAnimation.CAAnimation.CurrentMediaTime());
}
