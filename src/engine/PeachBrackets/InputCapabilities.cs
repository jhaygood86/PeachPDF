using System;

namespace PeachBrackets;

/// <summary>The accuracy of a pointing device, as the <c>pointer</c> and <c>any-pointer</c> media features describe it.</summary>
[Flags]
public enum PointerTypes
{
    /// <summary>No pointing device.</summary>
    None = 0,

    /// <summary>A device of limited accuracy, such as a finger on a touch screen.</summary>
    Coarse = 1,

    /// <summary>An accurate device, such as a mouse, a pen or a trackpad.</summary>
    Fine = 2,
}

/// <summary>
/// What the document is shown with: the pointing devices of the host, which the <c>hover</c>, <c>any-hover</c>, <c>pointer</c> and
/// <c>any-pointer</c> media features report so a style sheet can offer hover styling only where there is something to hover with. A PDF is
/// not pointed at, so the default is no device at all and a document renders exactly as it did before these features were read.
/// </summary>
/// <param name="PrimaryPointer">the accuracy of the primary pointing device (<see cref="PointerTypes.None"/>, <see cref="PointerTypes.Coarse"/> or <see cref="PointerTypes.Fine"/>)</param>
/// <param name="PrimaryCanHover">whether the primary pointing device can hover over elements</param>
/// <param name="AnyPointers">every kind of pointing device available</param>
/// <param name="AnyCanHover">whether any available pointing device can hover</param>
public sealed record InputCapabilities(PointerTypes PrimaryPointer, bool PrimaryCanHover, PointerTypes AnyPointers, bool AnyCanHover)
{
    /// <summary>No pointing device: what a PDF or a printed page has.</summary>
    public static InputCapabilities None { get; } = new(PointerTypes.None, false, PointerTypes.None, false);

    /// <summary>A mouse, a pen or a trackpad: accurate, and able to hover.</summary>
    public static InputCapabilities Mouse { get; } = new(PointerTypes.Fine, true, PointerTypes.Fine, true);

    /// <summary>A touch screen: a coarse pointer that cannot hover.</summary>
    public static InputCapabilities Touch { get; } = new(PointerTypes.Coarse, false, PointerTypes.Coarse, false);
}
