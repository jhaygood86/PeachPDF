namespace PeachBrackets.ScriptHosting;

/// <summary>What a script learns from loading an image: its size and where it came from.</summary>
/// <param name="Width">the image's natural width in CSS pixels</param>
/// <param name="Height">the image's natural height in CSS pixels</param>
/// <param name="Address">the absolute address it was loaded from</param>
public sealed record ScriptImageInfo(double Width, double Height, string Address);
