using System.Security.Cryptography;
using System.Text;
using PeachPDF.Tests.TestSupport;

namespace PeachDrawing.Text.Tests.Fonts
{
    /// <summary>
    /// Pins the glyph every Basic Multilingual Plane codepoint maps to, through the public API, for a spread of bundled fonts. The format-4 segment
    /// search was a linear scan and is now a binary search; the two have to pick the same segment for every codepoint, so the whole map is hashed.
    /// The hashes were recorded from the linear scan. A hash cannot say which codepoint changed: map both ways and compare.
    /// </summary>
    public class CmapLookupGoldenTests
    {
        private static Typeface Load(string path)
        {
            var set = new FontSet();
            var family = set.AddData(File.ReadAllBytes(path), new AddOptions { FamilyName = "Cmap-" + Guid.NewGuid().ToString("N") });
            Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
            return match.Typeface;
        }

        private static string MapHash(string path)
        {
            var face = Load(path);
            var bytes = new byte[0x10000 * 2];
            for (int c = 0; c <= 0xFFFF; c++)
            {
                // A surrogate is not a scalar value and cannot be asked about.
                ushort glyph = 0;
                if (c is < 0xD800 or > 0xDFFF && face.TryMapRune(new Rune(c), out var g))
                    glyph = g;
                bytes[c * 2] = (byte)(glyph >> 8);
                bytes[c * 2 + 1] = (byte)glyph;
            }

            return Convert.ToHexString(SHA256.HashData(bytes));
        }

        public static TheoryData<string> Fonts => new()
        {
            { "Ttf" }, { "Otf" }, { "Hebrew" }, { "Arabic" }, { "Devanagari" }, { "Cjk" }, { "Emoji" }, { "Math" }, { "VariableTest" }, { "SvgTest" },
        };

        private static string PathOf(string name) => name switch
        {
            "Ttf" => BundledFonts.Ttf,
            "Otf" => BundledFonts.Otf,
            "Hebrew" => BundledFonts.Hebrew,
            "Arabic" => BundledFonts.Arabic,
            "Devanagari" => BundledFonts.Devanagari,
            "Cjk" => BundledFonts.Cjk,
            "Emoji" => BundledFonts.Emoji,
            "Math" => BundledFonts.Math,
            "VariableTest" => BundledFonts.VariableTest,
            "SvgTest" => BundledFonts.SvgTest,
            _ => throw new ArgumentOutOfRangeException(nameof(name)),
        };

        [Theory]
        [MemberData(nameof(Fonts))]
        public void EveryBasicMultilingualPlaneCodepointMapsToTheRecordedGlyph(string name)
        {
            var actual = MapHash(PathOf(name));

            Assert.True(Hashes.TryGetValue(name, out var recorded) && recorded == actual,
                $"{name}: recorded {(Hashes.TryGetValue(name, out var h) ? h : "(none)")}, actual {actual}");
        }

        private static readonly Dictionary<string, string> Hashes = new()
        {
            ["Arabic"] = "9C266DC1E73A03448FBB71D85D85E206EBD3B0174DC641F410EE21D5060AA08E",
            ["Cjk"] = "26ECAE56A75876697F0664EF20D2863246DB50F2481ACFDAA59BEC899F2851BE",
            ["Devanagari"] = "B814BA6146684CE10732D95DFE551DCF073BE5F449F9CA47FEE051FABDF00744",
            ["Emoji"] = "4D5BE4481E710F121D0E86BBB7A731A0C51E52BF770940C9F9CD5B02C12F1006",
            ["Hebrew"] = "F6F6014B641F11BC1D9E888FAEF3AE339B04F9CB4B496B398D114B135CEC537F",
            ["Math"] = "F320921747B8F7C3F31AC6DE5C2D98002483773D161F51A77F09386EEE9BB43A",
            ["Otf"] = "19116186A7EA7AE403C865AF08FF89C0C06F3C296CDC0C986D3C357FF94F7EAB",
            ["SvgTest"] = "A38270F9192981AD805B0B20C01CB79EA3E7C71B3174748AAB8207E1214C51C9",
            ["Ttf"] = "418FCD3E5F03DE33D07BDA2766D7406B38FEB44A16CCC70AA0CA87A5E36FCAA5",
            ["VariableTest"] = "35A71E1109E43041BC13D66B4C9EDFF0EC52B5F8E9C6ED6F8AEBFE0E1E877E84",
        };
    }
}
