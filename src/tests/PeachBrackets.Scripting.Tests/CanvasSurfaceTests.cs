namespace PeachBrackets.Scripting.Tests;

/// <summary>
/// A host that asks for it (<see cref="HtmlDocumentOptions.CanvasSurfaces"/>) has each canvas kept on a bitmap of its own backend, drawn as the script draws and shown as
/// that bitmap. What a script reads back and what the page shows must be the picture the recording makes.
/// </summary>
public class CanvasSurfaceTests
{
    public static TheoryData<string> SceneNames => [.. CanvasParityTests.Scenes.Select(s => s.Name)];

    [Theory]
    [MemberData(nameof(SceneNames))]
    public async Task APageShowsTheSamePictureFromTheSurfaceAsFromTheRecording(string name)
    {
        var script = CanvasParityTests.Scenes.First(s => s.Name == name).Script;
        var body = $"var c = document.getElementById('c'); var g = c.getContext('2d'); {script}";

        var (recorded, _) = await CanvasParityTests.PaintAsync(body, "width=60 height=40");
        var (surfaced, _) = await CanvasParityTests.PaintAsync(body, "width=60 height=40", surfaces: true);

        // The recording is played at the page's scale as vectors; the bitmap is the canvas at one pixel a unit, resampled to it. The same picture, to within the edges.
        var a = recorded.ToPixelBuffer().PremultipliedRgba.ToArray();
        var b = surfaced.ToPixelBuffer().PremultipliedRgba.ToArray();
        Assert.Equal(a.Length, b.Length);
        long difference = 0;
        for (var i = 0; i < a.Length; i++)
            difference += Math.Abs(a[i] - b[i]);

        var mean = difference / (double)a.Length;
        Assert.True(mean < 2, $"{name}: the pages differ by {mean:0.0} levels on average");
    }

    [Fact]
    public async Task TheSurfaceIsWhatThePageShows_NotTheRecording()
    {
        // A one pixel line is crisp in the bitmap and resampled when it is drawn at the page's scale; played as a recording it is stroked at that scale. The
        // two differ, so a page that has the same pixels both ways did not use the surface.
        const string body = "var c = document.getElementById('c'); var g = c.getContext('2d'); g.strokeStyle = '#000'; g.lineWidth = 1; g.beginPath(); g.moveTo(10.5, 0); g.lineTo(10.5, 40); g.stroke(); g.fillStyle = '#d22'; g.fillRect(31, 7, 9, 9);";

        var (recorded, _) = await CanvasParityTests.PaintAsync(body, "width=60 height=40");
        var (surfaced, _) = await CanvasParityTests.PaintAsync(body, "width=60 height=40", surfaces: true);

        Assert.NotEqual(CanvasParityTests.Hash(recorded), CanvasParityTests.Hash(surfaced));
    }

    [Fact]
    public async Task WhatAScriptReadsBack_IsTheSameFromTheSurface()
    {
        var (_, recorded) = await CanvasParityTests.PaintAsync(CanvasParityTests.ReadBackScript, "width=30 height=20");
        var (_, surfaced) = await CanvasParityTests.PaintAsync(CanvasParityTests.ReadBackScript, "width=30 height=20", surfaces: true);

        Assert.Equal(recorded, surfaced);
    }

    [Fact]
    public async Task ResizingACanvas_GivesItANewSurface()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            g.fillStyle = '#d22'; g.fillRect(0, 0, 30, 20);
            c.width = 40;
            g = c.getContext('2d');
            Log.info('after ' + Array.from(g.getImageData(5, 5, 1, 1).data).join(',') + ' ' + c.width);
            g.fillStyle = '#22d'; g.fillRect(0, 0, 40, 20);
            Log.info('drawn ' + Array.from(g.getImageData(5, 5, 1, 1).data).join(','));
            """, "width=30 height=20", surfaces: true);

        Assert.Equal(["after 0,0,0,0 40", "drawn 34,34,221,255"], log);
    }

    [Fact]
    public async Task DrawingOneCanvasOntoAnother_WorksOnSurfaces()
    {
        var (_, log) = await CanvasParityTests.PaintAsync("""
            var c = document.getElementById('c'), g = c.getContext('2d');
            var s = document.createElement('canvas'); s.width = 10; s.height = 10;
            var q = s.getContext('2d'); q.fillStyle = '#d22'; q.fillRect(0, 0, 10, 10);
            g.drawImage(s, 5, 5);
            q.fillStyle = '#22d'; q.fillRect(0, 0, 10, 10);
            Log.info(Array.from(g.getImageData(8, 8, 1, 1).data).join(','));
            """, "width=30 height=20", surfaces: true);

        // The copy is of what the source held when it was drawn: red, not the blue drawn after.
        Assert.Equal(["221,34,34,255"], log);
    }
}
