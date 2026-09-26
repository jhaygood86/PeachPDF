namespace PeachPDF.Tests.TestSupport
{
    /// <summary>Typefaces as a caller outside the engine gets them: through a <see cref="PeachDrawing.Text.FontSet"/>.</summary>
    internal static class TypefaceFixtures
    {
        /// <summary>The typeface a font file gives, as a caller outside the engine gets it: through a font set.</summary>
        public static PeachDrawing.Text.Typeface FromFile(string path) => FromBytes(System.IO.File.ReadAllBytes(path));

        /// <summary>The typeface font data gives, as a caller outside the engine gets it: through a font set.</summary>
        public static PeachDrawing.Text.Typeface FromBytes(byte[] data)
        {
            var set = new PeachDrawing.Text.FontSet();
            var family = set.AddData(data, new PeachDrawing.Text.AddOptions { FamilyName = "TestFonts-" + System.Guid.NewGuid().ToString("N") });
            if (!family.TryMatch(new PeachDrawing.Text.TypefaceQuery(), out var match))
                throw new System.InvalidOperationException("The font data matched no face.");
            return match.Typeface;
        }
    }
}
