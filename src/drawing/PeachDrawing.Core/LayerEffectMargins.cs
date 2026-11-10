using System;
using System.Collections.Generic;

namespace PeachDrawing.Core
{
    /// <summary>How far past a layer's content its effects spread ink, so the region a layer is composited from can be made large enough for them.</summary>
    internal static class LayerEffectMargins
    {
        /// <summary>
        /// The horizontal and vertical distance, in the layer's own units, that <paramref name="effects"/> together spread ink past the content: three standard
        /// deviations of every blur and shadow (a Gaussian is negligible beyond that), plus the offset of every shadow.
        /// </summary>
        public static (double X, double Y) InkMargin(IReadOnlyList<LayerEffect> effects)
        {
            double x = 0, y = 0;
            foreach (var effect in effects)
            {
                switch (effect)
                {
                    case BlurEffect blur:
                        x += 3 * Math.Max(0, blur.SigmaX);
                        y += 3 * Math.Max(0, blur.SigmaY);
                        break;
                    case DropShadowEffect shadow:
                        x += Math.Abs(shadow.OffsetX) + 3 * Math.Max(0, shadow.SigmaX);
                        y += Math.Abs(shadow.OffsetY) + 3 * Math.Max(0, shadow.SigmaY);
                        break;
                }
            }

            return (x, y);
        }
    }
}
