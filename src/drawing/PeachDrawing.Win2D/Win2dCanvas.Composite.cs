using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using PeachDrawing.Core;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace PeachDrawing.Win2D;

public sealed partial class Win2dCanvas
{
    // A Porter-Duff operator needs the pixels under the layer, which a session cannot read while it draws: it blends a source over the target by
    // a few simple rules only. The target is a bitmap of its own, though, so a copy of it made on the GPU can be combined with the layer by an
    // effect, and that result put back over the region with the Copy blend. Nothing is read back to the CPU.
    //
    // It needs a canvas that owns its target (a composer's, where the bitmap is known) and no geometry clip open (Direct2D draws what is inside
    // one into a layer and composites it at the pop, so the Copy would be composited, not applied). Otherwise the layer is composited as Normal.

    private static bool IsCompositeOperator(PaintBlendMode mode) => mode >= PaintBlendMode.SourceIn;

    /// <inheritdoc />
    public override bool SupportsCompositeOperators => _backdrop is not null;

    private static CanvasComposite CompositeFor(PaintBlendMode mode) => mode switch
    {
        PaintBlendMode.SourceIn => CanvasComposite.SourceIn,
        PaintBlendMode.SourceOut => CanvasComposite.SourceOut,
        PaintBlendMode.SourceAtop => CanvasComposite.SourceAtop,
        PaintBlendMode.DestinationOver => CanvasComposite.DestinationOver,
        PaintBlendMode.DestinationIn => CanvasComposite.DestinationIn,
        PaintBlendMode.DestinationOut => CanvasComposite.DestinationOut,
        PaintBlendMode.DestinationAtop => CanvasComposite.DestinationAtop,
        PaintBlendMode.Xor => CanvasComposite.Xor,
        _ => CanvasComposite.SourceOver,
    };

    /// <summary>
    /// Composites <paramref name="layer"/> (already placed by <paramref name="placement"/>) over the target with a Porter-Duff operator, inside
    /// <paramref name="bounds"/>. Returns false, having drawn nothing, when this canvas cannot (see above).
    /// </summary>
    private bool TryDrawComposited(ICanvasImage layer, Matrix3x2 placement, PaintBlendMode mode, Rect? bounds, List<IDisposable> made)
    {
        if (_backdrop is not { } target || InsideGeometryLayer || _disposed)
            return false;

        // What the target holds so far is in it once the session has flushed.
        _session.Flush();

        var dpi = target.Dpi;
        var size = target.Size;
        var snapshot = new CanvasRenderTarget(target.Device, (float)size.Width, (float)size.Height, dpi);
        _context.Counts.Tile(1);
        made.Add(new ReleaseAction(() =>
        {
            snapshot.Dispose();
            _context.Counts.Tile(-1);
        }));

        using (var copy = snapshot.CreateDrawingSession())
        {
            copy.Blend = CanvasBlend.Copy;
            copy.DrawImage(target);
        }

        // The region the operator reaches, in the target's own units: the layer's bounds through the placement, or the whole target.
        var region = new Windows.Foundation.Rect(0, 0, size.Width, size.Height);
        if (bounds is { } b)
        {
            var corners = new[]
            {
                Vector2.Transform(new Vector2((float)b.X, (float)b.Y), placement),
                Vector2.Transform(new Vector2((float)(b.X + b.Width), (float)b.Y), placement),
                Vector2.Transform(new Vector2((float)b.X, (float)(b.Y + b.Height)), placement),
                Vector2.Transform(new Vector2((float)(b.X + b.Width), (float)(b.Y + b.Height)), placement),
            };
            float left = float.MaxValue, top = float.MaxValue, right = float.MinValue, bottom = float.MinValue;
            foreach (var c in corners)
            {
                left = Math.Min(left, c.X); top = Math.Min(top, c.Y);
                right = Math.Max(right, c.X); bottom = Math.Max(bottom, c.Y);
            }

            left = Math.Max(left, 0); top = Math.Max(top, 0);
            right = Math.Min(right, (float)size.Width); bottom = Math.Min(bottom, (float)size.Height);
            if (right <= left || bottom <= top)
                return true; // nothing of the region is on the target

            region = new Windows.Foundation.Rect(left, top, right - left, bottom - top);
        }

        // Destination first, the layer composited onto it; cropped to the region, so only that is replaced.
        var placed = new Transform2DEffect { Source = layer, TransformMatrix = placement };
        made.Add(placed);
        var combined = new CompositeEffect { Mode = CompositeFor(mode) };
        if (mode == PaintBlendMode.Copy || mode == PaintBlendMode.Clear)
        {
            // Copy is the layer alone; Clear is nothing. Neither reads the backdrop.
            combined.Mode = CanvasComposite.SourceOver;
            if (mode == PaintBlendMode.Copy)
                combined.Sources.Add(placed);
        }
        else
        {
            combined.Sources.Add(snapshot);
            combined.Sources.Add(placed);
        }

        made.Add(combined);

        // A source that is empty (Clear) draws nothing, and Copy over the region then leaves it transparent.
        ICanvasImage result = combined;
        if (combined.Sources.Count == 0)
            result = new ColorSourceEffect { Color = Windows.UI.Color.FromArgb(0, 0, 0, 0) };

        var cropped = new CropEffect { Source = result, SourceRectangle = region };
        made.Add(cropped);

        var savedTransform = _session.Transform;
        var savedBlend = _session.Blend;
        try
        {
            _session.Transform = Matrix3x2.Identity;
            _session.Blend = CanvasBlend.Copy;
            _session.DrawImage(cropped, (float)region.X, (float)region.Y, region);
        }
        finally
        {
            _session.Transform = savedTransform;
            _session.Blend = savedBlend;
        }

        return true;
    }

    /// <summary>Runs an action when it is disposed, so a resource the composite made can sit in the list a layer's effects are released from.</summary>
    private sealed class ReleaseAction(Action release) : IDisposable
    {
        public void Dispose() => release();
    }
}
