using PeachDrawing.Win2D;
using PeachDrawing.Text;

namespace PeachDrawing.Win2D.Tests;

/// <summary>Contexts that share one <see cref="FontSet"/>: a font added to it is read once and every context can use it.</summary>
public class SharedFontSetTests
{
    private static byte[] Font() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf"));

    [Fact]
    public void AFontAddedToTheSharedSet_IsSeenByEveryContextOnceItSyncs()
    {
        var set = new FontSet();
        using var first = new Win2dRenderContext(fontSet: set);
        using var second = new Win2dRenderContext(fontSet: set);

        set.AddData(Font(), new AddOptions { FamilyName = "Shared Win2D Sans" });

        Assert.False(first.IsFontExists("Shared Win2D Sans"));
        first.SyncFonts();
        second.SyncFonts();
        Assert.True(first.IsFontExists("Shared Win2D Sans"));
        Assert.True(second.IsFontExists("Shared Win2D Sans"));
    }

    [Fact]
    public void AContextMadeAfterTheFontWasAdded_SeesItAtOnce()
    {
        var set = new FontSet();
        set.AddData(Font(), new AddOptions { FamilyName = "Shared Win2D Sans" });

        using var context = new Win2dRenderContext(fontSet: set);

        Assert.True(context.IsFontExists("Shared Win2D Sans"));
    }

    [Fact]
    public async Task ASetOfTheContextsOwn_IsNotSharedWithOthers()
    {
        using var first = new Win2dRenderContext();
        using var second = new Win2dRenderContext();

        await first.AddFont(new MemoryStream(Font()), "Private Win2D Sans");

        Assert.True(first.IsFontExists("Private Win2D Sans"));
        Assert.False(second.IsFontExists("Private Win2D Sans"));
    }
}
