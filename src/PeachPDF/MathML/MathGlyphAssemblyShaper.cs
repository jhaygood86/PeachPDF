#region PeachPDF - A .NET library for rendering HTML to PDF
//
// MathML Core §5.3.2's glyph-assembly shaping algorithm
// (https://w3c.github.io/mathml-core/#shaping-of-the-glyph-assembly), transcribed directly from the
// spec's own formulas (which the spec itself credits to HarfBuzz's OpenType MATH implementation, so
// this matches what Chromium/WebKit/Firefox actually draw - all three are HarfBuzz-backed for math
// shaping). Builds a taller/wider stretchy shape by repeating a GlyphAssembly's extender part(s) and
// distributing overlap between neighboring parts, for a target size beyond the font's largest pre-sized
// MathVariants entry - see MathLayoutEngine.SelectVerticalVariant, the only caller.
//
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using PeachPDF.Fonts.OpenType;

namespace PeachPDF.MathML
{
    internal static class MathGlyphAssemblyShaper
    {
        /// <summary>One shaped part: its glyph and its offset along the growth axis (design units,
        /// measured from the assembly's own start - the bottom for a vertical assembly, the left for a
        /// horizontal one - per OpenType MATH's "GlyphAssembly" part order).</summary>
        public readonly record struct ShapedPart(ushort GlyphId, double OffsetDesignUnits);

        public readonly record struct ShapeResult(IReadOnlyList<ShapedPart> Parts, double SizeDesignUnits);

        /// <summary>Shapes <paramref name="assembly"/> to reach at least <paramref name="targetDesignUnits"/>
        /// along its growth axis. Returns null if the assembly fails MathML Core's own validity
        /// conditions (no extender part; an extender part whose overlap-adjusted size can't actually
        /// grow the shape; or a connector shorter than the font's own <paramref name="minConnectorOverlap"/>)
        /// - cases a conformant font should never produce, but checked defensively rather than assumed.</summary>
        public static ShapeResult? Shape(MathGlyphAssembly assembly, double minConnectorOverlap, double targetDesignUnits)
        {
            var parts = assembly.Parts;
            if (parts.Count == 0)
                return null;

            int extenderCount = parts.Count(p => p.IsExtender);
            if (extenderCount == 0)
                return null; // N_Ext must be > 0 - otherwise the assembly can never grow.

            double sExt = parts.Where(p => p.IsExtender).Sum(p => p.FullAdvance);
            double sNonExt = parts.Where(p => !p.IsExtender).Sum(p => p.FullAdvance);
            int nNonExt = parts.Count - extenderCount;

            double sExtNonOverlapping = sExt - minConnectorOverlap * extenderCount;
            if (sExtNonOverlapping <= 0)
                return null; // the assembly doesn't grow when joining extenders.

            // Candidate connector lengths bounding the overlap usable at every real junction in the
            // assembled sequence. A junction exists between every pair of consecutively-drawn parts; the
            // very first drawn part's own start (its bottom, for a vertical assembly - nothing is drawn
            // before it to connect there) and the very last drawn part's own end (its top - nothing is
            // drawn after it) face outward and never actually form a junction, so their connector length
            // is excluded here rather than needlessly limiting the overlap used everywhere else. A real
            // font's outward-facing end-cap connector is commonly 0 for exactly this reason (STIX Two
            // Math's parenleft end caps both are) - excluding it is what lets the parts that DO join
            // still overlap by the font's real connector lengths instead of being forced to 0 overlap.
            var startCandidates = parts
                .Where((p, i) => !(i == 0 && !p.IsExtender))
                .Select(p => p.StartConnectorLength)
                .ToArray();
            var endCandidates = parts
                .Where((p, i) => !(i == parts.Count - 1 && !p.IsExtender))
                .Select(p => p.EndConnectorLength)
                .ToArray();

            int AssemblyGlyphCount(int r) => nNonExt + r * extenderCount;
            double AssemblySize(double o, int r) => sNonExt + r * sExt - o * (AssemblyGlyphCount(r) - 1);

            int rMin = Math.Max(0, (int)Math.Ceiling(
                (targetDesignUnits - sNonExt + minConnectorOverlap * (nNonExt - 1)) / sExtNonOverlapping));

            int glyphCount = AssemblyGlyphCount(rMin);
            double oMax;
            if (glyphCount <= 1)
            {
                oMax = 0;
            }
            else
            {
                double oMaxTheoretical = (AssemblySize(0, rMin) - targetDesignUnits) / (glyphCount - 1);
                oMax = new[] { oMaxTheoretical, startCandidates.Min(), endCandidates.Min() }.Min();
            }

            var size = AssemblySize(oMax, rMin);
            var shaped = new List<ShapedPart>(AssemblyGlyphCount(rMin));
            double cursor = 0;
            foreach (var part in parts)
            {
                int repeats = part.IsExtender ? rMin : 1;
                for (int i = 0; i < repeats; i++)
                {
                    shaped.Add(new ShapedPart(part.GlyphId, cursor));
                    cursor += part.FullAdvance - oMax;
                }
            }

            return new ShapeResult(shaped, size);
        }
    }
}
