namespace PeachBrackets.ScriptHosting;

/// <summary>What a script learns from loading an image: its size and where it came from.</summary>
/// <param name="Width">the image's natural width in CSS pixels</param>
/// <param name="Height">the image's natural height in CSS pixels</param>
/// <param name="Address">the absolute address it was loaded from</param>
/// <param name="ResponseAddress">the address it was finally read from when its request was redirected, or null</param>
public sealed record ScriptImageInfo(double Width, double Height, string Address, string? ResponseAddress = null);
