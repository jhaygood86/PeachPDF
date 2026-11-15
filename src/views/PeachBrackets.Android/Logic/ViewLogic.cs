using PeachDrawing.Core;
using System;

namespace PeachBrackets.Android.Logic;

// The parts of the Android view that need no Android: they are compiled into the test project as well, so they run without an emulator.

/// <summary>The system pointer icons (<c>android.view.PointerIcon.TYPE_*</c>) a <see cref="CursorShape"/> is shown as.</summary>
internal static class AndroidCursors
{
    public const int Null = 0;
    public const int Arrow = 1000;
    public const int Context = 1001;
    public const int Hand = 1002;
    public const int Help = 1003;
    public const int Wait = 1004;
    public const int Cell = 1006;
    public const int Crosshair = 1007;
    public const int Text = 1008;
    public const int VerticalText = 1009;
    public const int Alias = 1010;
    public const int Copy = 1011;
    public const int NoDrop = 1012;
    public const int AllScroll = 1013;
    public const int HorizontalDoubleArrow = 1014;
    public const int VerticalDoubleArrow = 1015;
    public const int TopRightDiagonalDoubleArrow = 1016;
    public const int TopLeftDiagonalDoubleArrow = 1017;
    public const int ZoomIn = 1018;
    public const int ZoomOut = 1019;
    public const int Grab = 1020;
    public const int Grabbing = 1021;

    /// <summary>The icon type for a shape; a shape with no icon of its own gets the nearest one. <c>cursor: none</c> is no icon.</summary>
    public static int ToType(CursorShape shape) => shape switch
    {
        CursorShape.None => Null,
        CursorShape.ContextMenu => Context,
        CursorShape.Pointer => Hand,
        CursorShape.Help => Help,
        CursorShape.Wait or CursorShape.Progress => Wait,
        CursorShape.Cell => Cell,
        CursorShape.Crosshair => Crosshair,
        CursorShape.Text => Text,
        CursorShape.VerticalText => VerticalText,
        CursorShape.Alias => Alias,
        CursorShape.Copy => Copy,
        CursorShape.NoDrop or CursorShape.NotAllowed => NoDrop,
        CursorShape.Move or CursorShape.ScrollAll => AllScroll,
        CursorShape.ResizeHorizontal or CursorShape.ResizeEast or CursorShape.ResizeWest or CursorShape.ResizeColumn => HorizontalDoubleArrow,
        CursorShape.ResizeVertical or CursorShape.ResizeNorth or CursorShape.ResizeSouth or CursorShape.ResizeRow => VerticalDoubleArrow,
        CursorShape.ResizeDiagonalUp or CursorShape.ResizeNorthEast or CursorShape.ResizeSouthWest => TopRightDiagonalDoubleArrow,
        CursorShape.ResizeDiagonalDown or CursorShape.ResizeNorthWest or CursorShape.ResizeSouthEast => TopLeftDiagonalDoubleArrow,
        CursorShape.ZoomIn => ZoomIn,
        CursorShape.ZoomOut => ZoomOut,
        CursorShape.Grab => Grab,
        CursorShape.Grabbing => Grabbing,
        _ => Arrow,
    };
}

/// <summary>What the pointing devices attached to the device amount to for the <c>hover</c> and <c>pointer</c> media features.</summary>
internal static class AndroidInput
{
    /// <summary>A mouse, trackpad or stylus is accurate and can hover; a touch screen is coarse and cannot; the first of those present is primary.</summary>
    public static InputCapabilities Capabilities(bool mouse, bool stylus, bool touch)
    {
        var fine = mouse || stylus;
        var any = (fine ? PointerTypes.Fine : PointerTypes.None) | (touch ? PointerTypes.Coarse : PointerTypes.None);
        if (fine)
            return new InputCapabilities(PointerTypes.Fine, true, any, true);

        return touch ? new InputCapabilities(PointerTypes.Coarse, false, any, false) : InputCapabilities.None;
    }
}
