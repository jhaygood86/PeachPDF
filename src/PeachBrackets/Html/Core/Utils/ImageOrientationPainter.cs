using PeachDrawing.Core;
using PeachPDF.CSS;
using System;
using System.Numerics;

namespace PeachPDF.Html.Core.Utils
{
    /// <summary>
    /// Draws a raster upright through a rotation/flip matrix (<c>image-orientation</c>) instead of re-encoding it, so every
    /// byte-for-byte embed (DCT, PNG, GIF, CMYK) still reaches the PDF unchanged and the orientation is only a <c>cm</c>.
    /// The identity orientation makes exactly the call it always did, with no transform pushed.
    /// </summary>
    internal static class ImageOrientationPainter
    {
        /// <summary>
        /// Draws <paramref name="image"/> upright into <paramref name="dest"/>.
        /// </summary>
        /// <param name="g">the canvas</param>
        /// <param name="image">the raster, in its stored (unrotated) pixels</param>
        /// <param name="dest">where the upright image goes</param>
        /// <param name="src">the part of the <em>oriented</em> image to draw, in oriented pixels; null for all of it</param>
        /// <param name="sampling">how to sample</param>
        /// <param name="orientation">the effective orientation (never <c>from-image</c>)</param>
        public static void Draw(Canvas g, Image image, Rect dest, Rect? src, ImageSampling sampling, ImageOrientation orientation)
        {
            if (orientation.IsIdentity)
            {
                if (src is { } plain) g.DrawImage(image, dest, plain, sampling);
                else g.DrawImage(image, dest, sampling);
                return;
            }

            var local = LocalRect(dest, orientation);
            g.PushTransform(Matrix(dest, orientation));
            try
            {
                if (src is { } oriented)
                    g.DrawImage(image, local, ToStoredSpace(oriented, image.Width, image.Height, orientation), sampling);
                else
                    g.DrawImage(image, local, sampling);
            }
            finally
            {
                g.PopTransform();
            }
        }

        /// <summary>
        /// The rectangle the stored raster is drawn into before <see cref="Matrix"/> turns it: <paramref name="dest"/>'s
        /// size (width and height exchanged for a quarter turn) about the same center.
        /// </summary>
        public static Rect LocalRect(Rect dest, ImageOrientation orientation)
        {
            if (!orientation.SwapsAxes) return dest;
            var cx = dest.X + dest.Width / 2;
            var cy = dest.Y + dest.Height / 2;
            return new Rect(cx - dest.Height / 2, cy - dest.Width / 2, dest.Height, dest.Width);
        }

        /// <summary>
        /// The matrix mapping <see cref="LocalRect"/> onto <paramref name="dest"/>: rotate clockwise by the quarter
        /// turns about the center, then mirror horizontally if flipped.
        /// </summary>
        public static Matrix3x2 Matrix(Rect dest, ImageOrientation orientation)
        {
            var cx = (float)(dest.X + dest.Width / 2);
            var cy = (float)(dest.Y + dest.Height / 2);

            var (cos, sin) = (orientation.QuarterTurns & 3) switch
            {
                1 => (0f, 1f),
                2 => (-1f, 0f),
                3 => (0f, -1f),
                _ => (1f, 0f),
            };

            var rotate = new Matrix3x2(cos, sin, -sin, cos, 0, 0);
            var flip = orientation.Flip ? new Matrix3x2(-1, 0, 0, 1, 0, 0) : Matrix3x2.Identity;

            return Matrix3x2.CreateTranslation(-cx, -cy) * rotate * flip * Matrix3x2.CreateTranslation(cx, cy);
        }

        /// <summary>
        /// Maps a rectangle in the oriented image's pixels back to the stored raster's pixels (the corners go through the
        /// inverse of the orientation, taken as a bounding box).
        /// </summary>
        internal static Rect ToStoredSpace(Rect oriented, double storedWidth, double storedHeight, ImageOrientation orientation)
        {
            var orientedWidth = orientation.SwapsAxes ? storedHeight : storedWidth;
            var orientedHeight = orientation.SwapsAxes ? storedWidth : storedHeight;

            var whole = new Rect(0, 0, orientedWidth, orientedHeight);
            var local = LocalRect(whole, orientation);
            if (!Matrix3x2.Invert(Matrix(whole, orientation), out var inverse))
                return oriented;

            var a = Vector2.Transform(new Vector2((float)oriented.Left, (float)oriented.Top), inverse);
            var b = Vector2.Transform(new Vector2((float)oriented.Right, (float)oriented.Bottom), inverse);

            // The inverse maps oriented pixels into the local rect's frame; subtract its origin for stored pixels.
            var left = Math.Min(a.X, b.X) - local.X;
            var top = Math.Min(a.Y, b.Y) - local.Y;
            return new Rect(left, top, Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
        }
    }
}
