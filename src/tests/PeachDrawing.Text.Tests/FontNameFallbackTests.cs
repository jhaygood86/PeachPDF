using System.Buffers.Binary;

namespace PeachDrawing.Text.Tests;

/// <summary>
/// A font that gives its names in a language other than US English is a valid font. The reader only took names in US English (or on the Mac platform) and asserted
/// that it found one, so a web font like that (one of yahoo.com's) ended the process in a Debug build; the names of any language now stand in.
/// </summary>
public class FontNameFallbackTests
{
    // The font with its name records rewritten: the Mac platform ones to platform 2, which nothing reads, and the Windows ones to German.
    private static byte[] WithGermanNamesOnly()
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));
        var tables = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(4));
        for (var i = 0; i < tables; i++)
        {
            var entry = 12 + i * 16;
            if (System.Text.Encoding.ASCII.GetString(data, entry, 4) != "name") continue;

            var offset = (int)BinaryPrimitives.ReadUInt32BigEndian(data.AsSpan(entry + 8));
            var count = BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(offset + 2));
            for (var r = 0; r < count; r++)
            {
                var record = offset + 6 + r * 12;
                if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record)) == 1)
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record), 2);
                else if (BinaryPrimitives.ReadUInt16BigEndian(data.AsSpan(record + 4)) == 0x0409)
                    BinaryPrimitives.WriteUInt16BigEndian(data.AsSpan(record + 4), 0x0407);
            }

            return data;
        }

        throw new InvalidOperationException("the font has no name table");
    }

    [Fact]
    public void AFontWithNamesInAnotherLanguageOnly_Loads()
    {
        var fonts = new FontSet();

        var family = fonts.AddData(WithGermanNamesOnly(), new AddOptions { FamilyName = "Other Language Test" });

        // Matching is what reads the font's own tables, the name table among them.
        Assert.True(family.TryMatch(new TypefaceQuery(), out var match));
        Assert.NotNull(match.Typeface);
    }
}
