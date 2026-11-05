using PeachDrawing.Core;
using PeachDrawing.CoreGraphics;
using PeachDrawing.Text;

namespace PeachDrawing.CoreGraphics.Examples;

/// <summary>The standard Lorem Ipsum paragraph, word-wrapped and drawn with either text engine; shared by the macOS and iOS examples.</summary>
public static class LoremScene
{
    public const string Paragraph =
        "Lorem ipsum dolor sit amet, consectetur adipiscing elit, sed do eiusmod tempor incididunt ut labore et dolore magna aliqua. " +
        "Ut enim ad minim veniam, quis nostrud exercitation ullamco laboris nisi ut aliquip ex ea commodo consequat. " +
        "Duis aute irure dolor in reprehenderit in voluptate velit esse cillum dolore eu fugiat nulla pariatur. " +
        "Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum.";

    /// <summary>The system interface font's family name, as this library knows it.</summary>
    public static string SystemFamily => new FontSet().ResolveGeneric(GenericFamily.SystemUi) ?? "System Font";

    /// <summary>Draws the paragraph, wrapped to <paramref name="width"/> points, and returns how many lines it took.</summary>
    public static int Draw(CoreGraphicsCanvas canvas, CoreGraphicsRenderContext context, string family, double width, double size)
    {
        var font = context.GetFont(family, size, PaintFontStyle.Regular)!;
        var lineHeight = Math.Ceiling(font.Height * 1.25);
        var y = 10.0;
        var line = "";
        var lines = 0;
        void Flush()
        {
            canvas.DrawString(line, font, PaintColor.Black, new PaintPoint(10, y), canvas.MeasureString(line, font));
            y += lineHeight;
            lines++;
            line = "";
        }

        foreach (var word in Paragraph.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && canvas.MeasureString(candidate, font).Width > width - 20)
                Flush();

            line = line.Length == 0 ? word : line + " " + word;
        }

        if (line.Length > 0)
            Flush();

        return lines;
    }

    /// <summary>Renders the four combinations as PNGs named <c>lorem-{engine}-{font}.png</c> into <paramref name="folder"/>, each <paramref name="widthPoints"/> by 230 points at <paramref name="scale"/>x.</summary>
    public static async Task RenderAllAsync(string folder, string fontFile, double widthPoints = 360, int scale = 2)
    {
        foreach (var engine in new[] { CoreGraphicsTextEngine.Native, CoreGraphicsTextEngine.PeachDrawing })
        {
            using var context = new CoreGraphicsRenderContext(engine);
            await using (var font = File.OpenRead(fontFile))
                await context.AddFont(font, "Source Sans 3");

            foreach (var (label, family) in new[] { ("sourcesans", "Source Sans 3"), ("system", SystemFamily) })
            {
                using var composer = new CoreGraphicsComposer(context, widthPoints, 230, (int)(widthPoints * scale), 230 * scale,
                    new CoreGraphicsComposerOptions { Background = PaintColor.White });
                Draw(composer.Canvas, context, family, widthPoints, 15);
                using var file = File.Create(Path.Combine(folder, $"lorem-{engine.ToString().ToLowerInvariant()}-{label}.png"));
                composer.Save(file);
            }
        }
    }
}
