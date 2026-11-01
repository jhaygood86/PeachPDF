using System;

namespace PeachDrawing.Core;

/// <summary>An integer pixel rectangle; the right and bottom edges are exclusive.</summary>
public readonly record struct IntRect(int Left, int Top, int Right, int Bottom)
{
    /// <summary>The rectangle's width, in pixels.</summary>
    public int Width => Right - Left;

    /// <summary>The rectangle's height, in pixels.</summary>
    public int Height => Bottom - Top;

    /// <summary>Whether the rectangle covers no pixels.</summary>
    public bool IsEmpty => Right <= Left || Bottom <= Top;

    /// <summary>The rectangle covering no pixels.</summary>
    public static IntRect Empty => new(0, 0, 0, 0);

    /// <summary>The rectangle both this one and <paramref name="other"/> cover (empty when they do not overlap).</summary>
    public IntRect Intersect(in IntRect other)
    {
        var l = Math.Max(Left, other.Left);
        var t = Math.Max(Top, other.Top);
        var r = Math.Min(Right, other.Right);
        var b = Math.Min(Bottom, other.Bottom);
        return r <= l || b <= t ? Empty : new IntRect(l, t, r, b);
    }
}
