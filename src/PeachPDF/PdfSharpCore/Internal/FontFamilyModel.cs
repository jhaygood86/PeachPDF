using PeachPDF.PdfSharpCore.Utils;
using System.Collections.Generic;

namespace PeachPDF.PdfSharpCore.Internal
{
    internal class FontFamilyModel
    {
        public string Name { get; set; } = null!;

        /// <summary>
        /// Every registered face for this family, keyed by its own (numeric weight, italic, stretch) axis
        /// values - the real CSS Fonts Level 4 matching axes, not the coarser 4-slot Regular/Bold/Italic/
        /// BoldItalic bucket the previous <c>Dictionary&lt;XFontStyle, TtfFontDescription&gt;</c> design
        /// could only hold one face per. See <see cref="FontResolver"/>'s nearest-weight/-stretch matching
        /// (CSS Fonts Level 4 §5.2), which selects among these when there's no exact match.
        /// </summary>
        public Dictionary<(int Weight, bool Italic, int Stretch), TtfFontDescription> Faces { get; } = new();
    }
}
