using System;

namespace PeachBrackets;

/// <summary>The gestures a touch may start on an element, as the CSS <c>touch-action</c> property allows them.</summary>
[Flags]
public enum TouchActions
{
    /// <summary>The page handles every gesture itself: the host must not pan, zoom or double-tap-zoom.</summary>
    None = 0,

    /// <summary>Panning towards the left (the content moves right).</summary>
    PanLeft = 1,

    /// <summary>Panning towards the right.</summary>
    PanRight = 2,

    /// <summary>Panning upwards.</summary>
    PanUp = 4,

    /// <summary>Panning downwards.</summary>
    PanDown = 8,

    /// <summary>Pinch to zoom.</summary>
    PinchZoom = 16,

    /// <summary>Double tap to zoom.</summary>
    DoubleTapZoom = 32,

    /// <summary>Panning in any direction.</summary>
    Pan = PanLeft | PanRight | PanUp | PanDown,

    /// <summary>Every gesture: the initial value, <c>auto</c>.</summary>
    All = Pan | PinchZoom | DoubleTapZoom,
}

/// <summary>What the document says about a point for a host that handles pointer and touch input.</summary>
/// <param name="Inert">whether the element under the point takes no part in interaction (<c>interactivity: inert</c> or the <c>inert</c> attribute on it or an ancestor)</param>
/// <param name="TouchActions">the gestures a touch starting at the point may begin: the element's <c>touch-action</c> intersected with its ancestors'</param>
/// <param name="CanSelectText">whether text at the point may be selected (<c>user-select</c> is not <c>none</c>, and the point is not inert)</param>
public sealed record DocumentInteraction(bool Inert, TouchActions TouchActions, bool CanSelectText);
