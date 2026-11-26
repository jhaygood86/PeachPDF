using System.Security.Cryptography;
using PeachDrawing;

namespace PeachBrackets.Scripting.Tests;

/// <summary>
/// Pins the exact pixels of a fixed set of canvas scenes as they are painted into a page today, so work that moves canvas logic into
/// <c>PeachDrawing.*</c> or onto a GPU surface can prove it changed nothing it did not mean to. A scene that changes on purpose is
/// regenerated with <c>PEACH_UPDATE_BASELINE=1</c> and the diff reviewed; a changed hash is never regenerated blindly. Scenes draw no text, so
/// the hashes do not depend on a machine's fonts. A second test pins what scripts read back (<c>getImageData</c>, hit tests, <c>toDataURL</c>).
/// </summary>
public class CanvasParityTests
{
    private const string Red = "#d22", Green = "#2a2", Blue = "#22d";

    // Each scene runs against a 60x40 canvas c with context g.
    internal static readonly (string Name, string Script)[] Scenes =
    [
        ("fill-alpha", $"g.fillStyle = '{Red}'; g.fillRect(2, 2, 30, 20); g.globalAlpha = 0.5; g.fillStyle = '{Blue}'; g.fillRect(16, 10, 30, 20);"),
        ("linear-gradient", $"var l = g.createLinearGradient(0, 0, 60, 0); l.addColorStop(0, '{Red}'); l.addColorStop(0.5, '#fff'); l.addColorStop(1, '{Blue}'); g.fillStyle = l; g.fillRect(0, 0, 60, 40);"),
        ("radial-gradient", $"var r = g.createRadialGradient(20, 20, 4, 36, 20, 20); r.addColorStop(0, '#ff0'); r.addColorStop(1, '{Blue}'); g.fillStyle = r; g.fillRect(0, 0, 60, 40);"),
        ("conic-gradient", $"var k = g.createConicGradient(0.5, 30, 20); k.addColorStop(0, '{Red}'); k.addColorStop(0.5, '{Green}'); k.addColorStop(1, '{Red}'); g.fillStyle = k; g.fillRect(0, 0, 60, 40);"),
        ("stroke-dash-caps", $"g.strokeStyle = '{Green}'; g.lineWidth = 5; g.lineCap = 'round'; g.lineJoin = 'bevel'; g.setLineDash([8, 6]); g.lineDashOffset = 3; g.beginPath(); g.moveTo(5, 8); g.lineTo(50, 8); g.lineTo(30, 32); g.stroke();"),
        ("arc-ellipse-curves", $"g.fillStyle = '{Red}'; g.beginPath(); g.arc(15, 20, 12, 0.3, 5.2); g.fill(); g.strokeStyle = '{Blue}'; g.beginPath(); g.ellipse(40, 20, 14, 8, 0.6, 0, 6.28); g.moveTo(5, 38); g.bezierCurveTo(20, 10, 40, 60, 58, 30); g.quadraticCurveTo(30, 0, 5, 38); g.stroke();"),
        ("clip-evenodd", $"g.beginPath(); g.rect(4, 4, 52, 32); g.rect(16, 12, 28, 16); g.clip('evenodd'); g.fillStyle = '{Green}'; g.fillRect(0, 0, 60, 40);"),
        ("transform-roundrect", $"g.translate(30, 20); g.rotate(0.4); g.scale(1, 0.6); g.fillStyle = '{Blue}'; g.beginPath(); g.roundRect(-20, -15, 40, 30, [8, 2]); g.fill(); g.strokeStyle = '#000'; g.stroke();"),
        ("shadow", $"g.shadowColor = 'rgba(0,0,0,0.6)'; g.shadowBlur = 6; g.shadowOffsetX = 4; g.shadowOffsetY = 3; g.fillStyle = '{Red}'; g.fillRect(8, 6, 28, 20);"),
        ("blend-multiply", $"g.fillStyle = '{Red}'; g.fillRect(2, 2, 36, 30); g.globalCompositeOperation = 'multiply'; g.fillStyle = '{Blue}'; g.fillRect(20, 10, 36, 28);"),
        ("blend-lighter", $"g.fillStyle = '#400'; g.fillRect(2, 2, 36, 30); g.globalCompositeOperation = 'lighter'; g.fillStyle = '#004'; g.fillRect(20, 10, 36, 28);"),
        ("pd-source-in", PorterDuff("source-in")),
        ("pd-source-out", PorterDuff("source-out")),
        ("pd-source-atop", PorterDuff("source-atop")),
        ("pd-destination-over", PorterDuff("destination-over")),
        ("pd-destination-in", PorterDuff("destination-in")),
        ("pd-destination-out", PorterDuff("destination-out")),
        ("pd-destination-atop", PorterDuff("destination-atop")),
        ("pd-xor", PorterDuff("xor")),
        ("pd-copy", PorterDuff("copy")),
        ("pd-with-clip", $"g.fillStyle = '{Red}'; g.fillRect(0, 0, 60, 40); g.beginPath(); g.arc(30, 20, 14, 0, 6.28); g.clip(); g.globalCompositeOperation = 'destination-out'; g.fillStyle = '#000'; g.fillRect(0, 0, 60, 40);"),
        ("clear-rect-partial", $"g.fillStyle = '{Green}'; g.fillRect(0, 0, 60, 40); g.clearRect(10, 8, 24, 16);"),
        ("clear-rect-all", $"g.fillStyle = '{Green}'; g.fillRect(0, 0, 60, 40); g.clearRect(0, 0, 60, 40); g.fillStyle = '{Red}'; g.fillRect(5, 5, 10, 10);"),
        ("pattern-repeat", $"var s = document.createElement('canvas'); s.width = 8; s.height = 8; var q = s.getContext('2d'); q.fillStyle = '{Red}'; q.fillRect(0, 0, 4, 4); q.fillStyle = '{Blue}'; q.fillRect(4, 4, 4, 4); g.fillStyle = g.createPattern(s, 'repeat'); g.fillRect(2, 2, 54, 34);"),
        ("pattern-no-repeat", $"var s = document.createElement('canvas'); s.width = 8; s.height = 8; var q = s.getContext('2d'); q.fillStyle = '{Green}'; q.fillRect(0, 0, 8, 8); g.fillStyle = g.createPattern(s, 'no-repeat'); g.translate(10, 10); g.fillRect(0, 0, 40, 20);"),
        ("draw-canvas-scaled", $"var s = document.createElement('canvas'); s.width = 10; s.height = 10; var q = s.getContext('2d'); q.fillStyle = '{Red}'; q.fillRect(0, 0, 5, 10); q.fillStyle = '{Blue}'; q.fillRect(5, 0, 5, 10); g.drawImage(s, 5, 5, 40, 30);"),
        ("draw-canvas-snapshot", $"g.fillStyle = '{Red}'; g.fillRect(0, 0, 20, 20); g.drawImage(c, 30, 10); g.fillStyle = '{Blue}'; g.fillRect(0, 20, 20, 20);"),
        ("put-image-data", $"g.fillStyle = '{Green}'; g.fillRect(0, 0, 60, 40); var d = g.createImageData(10, 10); for (var i = 0; i < d.data.length; i += 4) {{ d.data[i] = 200; d.data[i + 3] = 128; }} g.putImageData(d, 10, 10); g.putImageData(d, 30, 20, 2, 2, 5, 5);"),
        ("image-smoothing-off", $"var s = document.createElement('canvas'); s.width = 2; s.height = 2; var q = s.getContext('2d'); q.fillStyle = '{Red}'; q.fillRect(0, 0, 1, 2); q.fillStyle = '{Blue}'; q.fillRect(1, 0, 1, 2); g.imageSmoothingEnabled = false; g.drawImage(s, 0, 0, 60, 40);"),
        ("save-restore", $"g.fillStyle = '{Red}'; g.save(); g.translate(20, 0); g.fillStyle = '{Blue}'; g.fillRect(0, 0, 10, 10); g.restore(); g.fillRect(0, 20, 10, 10);"),
        ("evenodd-fill-hole", $"g.fillStyle = '{Blue}'; g.beginPath(); g.rect(4, 4, 52, 32); g.rect(14, 12, 32, 16); g.fill('evenodd');"),
        ("miter-join", $"g.strokeStyle = '{Red}'; g.lineWidth = 6; g.lineJoin = 'miter'; g.miterLimit = 2; g.beginPath(); g.moveTo(6, 34); g.lineTo(30, 6); g.lineTo(54, 34); g.stroke();"),
    ];

    private static string PorterDuff(string operation) =>
        $"g.fillStyle = '{Red}'; g.fillRect(0, 0, 30, 40); g.globalCompositeOperation = '{operation}'; g.fillStyle = '{Blue}'; g.fillRect(15, 0, 30, 40);";

    internal const string ReadBackScript = """
        var c = document.getElementById('c'), g = c.getContext('2d');
        function px(x, y) { return Array.from(g.getImageData(x, y, 1, 1).data).join(','); }
        g.fillStyle = '#d22'; g.fillRect(0, 0, 15, 20);
        g.globalAlpha = 0.5; g.fillStyle = '#22d'; g.fillRect(10, 0, 15, 20); g.globalAlpha = 1;
        Log.info('px ' + [px(5, 5), px(12, 5), px(20, 5), px(28, 5)].join(' '));
        g.beginPath(); g.rect(2, 2, 10, 10); g.rect(5, 5, 4, 4);
        Log.info('inPath ' + [g.isPointInPath(3, 3), g.isPointInPath(6, 6), g.isPointInPath(6, 6, 'evenodd'), g.isPointInPath(20, 20)].join(','));
        g.lineWidth = 4; g.beginPath(); g.moveTo(0, 10); g.lineTo(30, 10);
        Log.info('inStroke ' + [g.isPointInStroke(15, 11), g.isPointInStroke(15, 14)].join(','));
        var img = g.getImageData(0, 0, 30, 20); var sum = 0; for (var i = 0; i < img.data.length; i++) sum = (sum * 31 + img.data[i]) % 1000003;
        Log.info('sum ' + sum);
        Log.info('url ' + c.toDataURL().length);
        g.globalCompositeOperation = 'destination-out'; g.fillRect(0, 0, 8, 8);
        Log.info('erased ' + [px(2, 2), px(12, 12)].join(' '));
        """;

    internal static async Task<(RasterCanvas Page, List<string> Log)> PaintAsync(string body, string canvasAttributes, bool surfaces = false)
    {
        var logger = new CollectingLogger();
        var host = new JintScriptHost(new JintScriptOptions { Logger = logger });
        var context = await TestDocument.NewContextAsync();
        var options = new HtmlDocumentOptions { MediaType = MediaType.Screen, PageWidthPoints = 200, ScriptHost = host, MeasuringCanvasFactory = () => context.CreateCanvas(1, 1), CanvasSurfaces = surfaces };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(
            TestDocument.Wrap($"<canvas id=c style=\"display:block\" {canvasAttributes}></canvas><script>{body}</script>"),
            context,
            options);
        Assert.True(logger.Errors.Count == 0, string.Join(" / ", logger.Errors));
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);

        var sheet = document.Pages[0];
        var page = context.CreateCanvas((int)Math.Ceiling(sheet.WidthPoints), (int)Math.Ceiling(sheet.HeightPoints));
        page.DrawRectangle(page.GetSolidBrush(PeachDrawing.Core.PaintColor.FromArgb(255, 255, 255, 255)), 0, 0, sheet.WidthPoints, sheet.HeightPoints);
        await document.PaintPageAsync(0, page);
        return (page, logger.Texts);
    }

    internal static string Hash(RasterCanvas page) => Convert.ToHexString(SHA256.HashData(page.ToPixelBuffer().PremultipliedRgba.Span));

    private static string SourcePath => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "Baselines", "CanvasParity.txt"));

    private static string BuiltPath => Path.Combine(AppContext.BaseDirectory, "Baselines", "CanvasParity.txt");

    private static Dictionary<string, string> Load() =>
        File.Exists(BuiltPath)
            ? File.ReadAllLines(BuiltPath).Where(l => l.Length > 0).Select(l => l.Split(' ', 2)).ToDictionary(p => p[0], p => p[1])
            : [];

    private static bool Updating => Environment.GetEnvironmentVariable("PEACH_UPDATE_BASELINE") == "1";

    // The scenes and the read-back log share one file: scene lines are "scene:<name> <hash>", read-back lines "read:<n> <text>".
    private static void Save(IEnumerable<KeyValuePair<string, string>> lines)
    {
        var existing = File.Exists(SourcePath)
            ? File.ReadAllLines(SourcePath).Where(l => l.Length > 0).Select(l => l.Split(' ', 2)).ToDictionary(p => p[0], p => p[1])
            : [];
        foreach (var (key, value) in lines)
            existing[key] = value;
        File.WriteAllLines(SourcePath, existing.OrderBy(p => p.Key, StringComparer.Ordinal).Select(p => p.Key + " " + p.Value));
    }

    [Fact]
    public async Task EveryScene_PaintsTheSamePixelsAsItDidWhenItWasPinned()
    {
        var actual = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (name, script) in Scenes)
        {
            var (page, _) = await PaintAsync($"var c = document.getElementById('c'); var g = c.getContext('2d'); {script}", "width=60 height=40");
            actual["scene:" + name] = Hash(page);
        }

        if (Updating)
        {
            Save(actual);
            return;
        }

        var baseline = Load();
        var changed = actual.Where(p => !baseline.TryGetValue(p.Key, out var pinned) || pinned != p.Value).Select(p => p.Key).ToList();
        Assert.True(changed.Count == 0, "Scenes whose pixels changed (or are not pinned): " + string.Join(", ", changed));
    }

    [Fact]
    public async Task WhatScriptsReadBack_IsTheSameAsItWasWhenItWasPinned()
    {
        var (_, log) = await PaintAsync(ReadBackScript, "width=30 height=20");
        var actual = log.Select((text, i) => KeyValuePair.Create($"read:{i}", text)).ToList();

        if (Updating)
        {
            Save(actual);
            return;
        }

        var baseline = Load();
        Assert.Equal(actual.Count, baseline.Keys.Count(k => k.StartsWith("read:", StringComparison.Ordinal)));
        foreach (var (key, text) in actual)
            Assert.Equal(baseline.GetValueOrDefault(key), text);
    }
}
