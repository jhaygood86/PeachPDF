/*
 * Derived from FreeType's tests/issue-1063/main.c (FreeType 2.14.3, VER-2-14-3), which has no header of its own and, like the rest of
 * FreeType, is distributed under the FreeType Project License (FTL.TXT, in src/PeachDrawing.Text/Internal/Hinting/FreeType/).
 * Copyright (C) 1996-2026 by David Turner, Robert Wilhelm, and Werner Lemberg.
 *
 * Ported to C# for PeachDrawing.Text.Tests; modified. The changes are recorded in PORTING-NOTES.md, next to FTL.TXT.
 */

using PeachDrawing.Text.Internal.Hinting.FreeType;
using PeachPDF.Tests.TestSupport;
using System.Text;

namespace PeachDrawing.Text.Tests.Hinting
{
    /// <summary>
    /// Regression tests ported from FreeType's own test directory. FreeType has one, <c>issue-1063</c>: it loads the glyphs of character
    /// codes 59 to 170 of a heavily hinted font with the default flags and reports any error the interpreter raises. The font it
    /// uses is not redistributable here, so the same program is run over the hinted fonts this repository does bundle, at several sizes
    /// and in both interpreter versions, and passes when no glyph program of them stops with an error.
    /// </summary>
    public class FreeTypeRegressionTests
    {
        [Theory]
        [InlineData("LiberationSans-Regular.woff")]
        [InlineData("LiberationSerif-Regular.woff")]
        [InlineData("SourceSans3-Regular.ttf")]
        public void Issue1063_TheGlyphsOfCharacterCodes59To170LoadWithoutAnError(string fontFile)
        {
            var face = HintingFixtures.Face(fontFile);
            var typeface = TypefaceFixtures.FromFile(Path.Combine(AppContext.BaseDirectory, fontFile));

            int loaded = 0;
            foreach (var version in new[] { TtInterpreterVersion.V40, TtInterpreterVersion.V35 })
            {
                foreach (int ppem in new[] { 9, 12, 16, 24 })
                {
                    var size = TtSize.Create(face, ppem * 64, version, version == TtInterpreterVersion.V35 ? TtRenderMode.Mono : TtRenderMode.Normal);

                    for (int code = 59; code < 171; code++)
                    {
                        if (!typeface.TryMapRune(new Rune(code), out var glyph))
                            continue;

                        var hinted = TtGlyphLoader.Load(size, glyph); // throws HintingException where FreeType returns an error
                        Assert.True(hinted.ProgramError == 0, $"{fontFile}: U+{code:X4} at {ppem} ppem ({version}) stopped with error {hinted.ProgramError}");
                        loaded++;
                    }
                }
            }

            Assert.True(loaded > 400, $"only {loaded} glyphs were loaded");
        }
    }
}
