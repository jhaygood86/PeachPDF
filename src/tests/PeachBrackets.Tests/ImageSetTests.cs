using PeachDrawing;

namespace PeachBrackets.Tests;

/// <summary>CSS <c>image-set()</c> through the public API: which option paints, and the zoom upgrade.</summary>
[Collection("HostResourceLoader state")]
public class ImageSetTests
{
    private static string Png(int width, int height, byte r, byte g, byte b)
    {
        var raw = new byte[height * (1 + (width * 3))];
        for (var y = 0; y < height; y++)
        {
            var row = y * (1 + (width * 3));
            for (var x = 0; x < width; x++)
            {
                raw[row + 1 + (x * 3)] = r;
                raw[row + 2 + (x * 3)] = g;
                raw[row + 3 + (x * 3)] = b;
            }
        }

        using var compressed = new MemoryStream();
        using (var zlib = new System.IO.Compression.ZLibStream(compressed, System.IO.Compression.CompressionMode.Compress, true))
            zlib.Write(raw);

        using var png = new MemoryStream();
        png.Write([0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]);
        var header = new byte[13];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header, width);
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4), height);
        header[8] = 8;
        header[9] = 2; // 8-bit RGB
        Chunk(png, "IHDR", header);
        Chunk(png, "IDAT", compressed.ToArray());
        Chunk(png, "IEND", []);
        return "data:image/png;base64," + Convert.ToBase64String(png.ToArray());
    }

    private static void Chunk(Stream stream, string type, byte[] data)
    {
        var typed = System.Text.Encoding.ASCII.GetBytes(type).Concat(data).ToArray();
        var length = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteInt32BigEndian(length, data.Length);
        stream.Write(length);
        stream.Write(typed);

        var crc = 0xFFFFFFFFu;
        foreach (var value in typed)
        {
            crc ^= value;
            for (var bit = 0; bit < 8; bit++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
        }

        var checksum = new byte[4];
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(checksum, ~crc);
        stream.Write(checksum);
    }

    // 1x is red, 2x is blue, so the colour tells which option painted.
    private static readonly string Html = TestDocument.Wrap(
        $"<div style=\"width:100px; height:100px; background: image-set(url({Png(10, 10, 255, 0, 0)}) 1x, url({Png(20, 20, 0, 0, 255)}) 2x) no-repeat\"></div>");

    private static async Task<(RasterRenderContext Context, HtmlDocument Document)> LoadAsync(MediaType media, double devicePixelRatio = 1)
    {
        // The context the way the views make theirs: it loads what the document refers to through the host loader.
        var context = new RasterRenderContext { ResourceLoader = uri => HostResourceLoader.LoadAsync(uri) };
        await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
            await context.AddFont(font, TestDocument.Family);

        var options = new HtmlDocumentOptions { MediaType = media, DevicePixelRatio = devicePixelRatio, PageWidthPoints = 200, ViewportHeightPoints = 100 };
        options.SetMargins(0);
        var document = await HtmlDocument.LoadAsync(Html, context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);
        return (context, document);
    }

    private static async Task<(byte R, byte G, byte B)> CentreAsync(RasterRenderContext context, HtmlDocument document)
    {
        var canvas = await TestDocument.PaintAsync(context, document);
        var (r, g, b, _) = TestDocument.Pixel(canvas, 3, 3);
        return (r, g, b);
    }

    [Fact]
    public async Task APagedDocument_PaintsTheHighestResolution()
    {
        var (context, document) = await LoadAsync(MediaType.Print);
        Assert.Equal((0, 0, 255), await CentreAsync(context, document));
    }

    [Fact]
    public async Task AScreenDocument_PaintsTheOptionForItsPixelRatio()
    {
        var (context, document) = await LoadAsync(MediaType.Screen);
        Assert.Equal((255, 0, 0), await CentreAsync(context, document));

        (context, document) = await LoadAsync(MediaType.Screen, 2);
        Assert.Equal((0, 0, 255), await CentreAsync(context, document));
    }

    [Fact]
    public async Task ZoomingAScreenDocument_UpgradesToTheDenserOption()
    {
        var (context, document) = await LoadAsync(MediaType.Screen);
        Assert.Equal((255, 0, 0), await CentreAsync(context, document));
        Assert.Empty((await document.UpgradeImagesAsync()).DirtyRegions);

        document.SetRenderScale(2);
        Assert.NotEmpty((await document.UpgradeImagesAsync()).DirtyRegions);
        Assert.Equal((0, 0, 255), await CentreAsync(context, document));
    }

    private static readonly string Red = Png(10, 10, 255, 0, 0);
    private static readonly string Blue = Png(20, 20, 0, 0, 255);

    [Theory]
    [InlineData("image-set('{R}' 1x, '{B}' 192dpi)", 0, 0, 255)]
    [InlineData("image-set(url({R}) 1dppx, url({B}) 2dppx)", 0, 0, 255)]
    [InlineData("image-set(url({R}) 1x, url({B}) 2x type('image/x-nonexistent'))", 255, 0, 0)]
    [InlineData("image-set(url({R}) 1x type('image/png'), url({B}) 2x type('image/png'))", 0, 0, 255)]
    [InlineData("image-set(url({R}), url({B}) 76dpcm)", 0, 0, 255)]
    [InlineData("image-set(url({R}) 2x, linear-gradient(rgb(0, 255, 0), rgb(0, 255, 0)) 1x)", 255, 0, 0)]
    [InlineData("image-set(url({R}) bogus)", 255, 255, 255)]
    public async Task EachOptionForm_ResolvesToTheRightImage(string value, int r, int g, int b)
    {
        var html = TestDocument.Wrap($"<div style=\"width:100px; height:100px; background: {value.Replace("{R}", Red).Replace("{B}", Blue)} no-repeat\"></div>");
        var context = new RasterRenderContext { ResourceLoader = uri => HostResourceLoader.LoadAsync(uri) };
        await using (var font = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "SourceSans3-Regular.ttf")))
            await context.AddFont(font, TestDocument.Family);

        var options = new HtmlDocumentOptions { MediaType = MediaType.Print, PageWidthPoints = 200 };
        options.SetMargins(0);
        using var document = await HtmlDocument.LoadAsync(html, context, options);
        using var measuring = context.CreateCanvas(1, 1);
        await document.LayoutAsync(measuring);

        var canvas = await TestDocument.PaintAsync(context, document);
        var (pr, pg, pb, _) = TestDocument.Pixel(canvas, 1, 1);
        Assert.Equal((r, g, b), ((int)pr, (int)pg, (int)pb));
    }
}
