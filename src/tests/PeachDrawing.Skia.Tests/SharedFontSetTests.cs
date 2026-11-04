using PeachDrawing.Skia;
using PeachDrawing.Text;

namespace PeachDrawing.Skia.Tests;

/// <summary>Contexts that share one <see cref="FontSet"/>: a font added to it is read once and every context can use it.</summary>
public class SharedFontSetTests
{
    private static byte[] Font() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));

    [Fact]
    public void AFontAddedToTheSharedSet_IsSeenByEveryContextOnceItSyncs()
    {
        var set = new FontSet();
        using var first = new SkiaRenderContext(fontSet: set);
        using var second = new SkiaRenderContext(fontSet: set);

        set.AddData(Font(), new AddOptions { FamilyName = "Shared Skia Sans" });

        Assert.False(first.IsFontExists("Shared Skia Sans"));
        first.SyncFonts();
        second.SyncFonts();
        Assert.True(first.IsFontExists("Shared Skia Sans"));
        Assert.True(second.IsFontExists("Shared Skia Sans"));
    }

    [Fact]
    public void AContextMadeAfterTheFontWasAdded_SeesItAtOnce()
    {
        var set = new FontSet();
        set.AddData(Font(), new AddOptions { FamilyName = "Shared Skia Sans" });

        using var context = new SkiaRenderContext(fontSet: set);

        Assert.True(context.IsFontExists("Shared Skia Sans"));
    }

    [Fact]
    public async Task ASetOfTheContextsOwn_IsNotSharedWithOthers()
    {
        using var first = new SkiaRenderContext();
        using var second = new SkiaRenderContext();

        await first.AddFont(new MemoryStream(Font()), "Private Skia Sans");

        Assert.True(first.IsFontExists("Private Skia Sans"));
        Assert.False(second.IsFontExists("Private Skia Sans"));
    }
}
