using PeachDrawing;
using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>Mesh shadings (types 4 to 7) drawn onto a raster canvas: each is read back at a few points to check the geometry and the colour blending.</summary>
    public class PdfMeshShadingTests
    {
        private const string Decode = "/Decode [0 100 0 100 0 1 0 1 0 1]";

        private static string Mesh(int type, string entries, byte[] data) =>
            TestPdf.Stream($"/ShadingType {type} /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 {entries}", TestPdf.Binary(data));

        private static (byte R, byte G, byte B, byte A) At(byte[] pdf, int x, int y, out IReadOnlyCollection<string> unsupported)
        {
            var doc = PdfSourceDocument.Open(pdf);
            using var canvas = new RasterRenderContext().CreateCanvas(100, 100);
            unsupported = PdfPagePainter.Paint(doc, doc.Pages[0], canvas);
            var span = canvas.ToPixelBuffer().PremultipliedRgba.Span;
            var i = (y * 100 + x) * 4;
            return (span[i], span[i + 1], span[i + 2], span[i + 3]);
        }

        private static byte[] Sh(string meshObject, string content = "/S0 sh", string extra = "") =>
            TestPdf.Build(content, resources: $"/Shading << /S0 5 0 R >> /Pattern << /P0 6 0 R >> {extra}", extraObjects: [meshObject, "<< /PatternType 2 /Shading 5 0 R >>"]);

        [Fact]
        public void FreeFormTriangle_BlendsItsVertexColours()
        {
            // One triangle: red at the lower left, green at the lower right, blue at the top left (flag, x, y, r, g, b).
            byte[] data = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255];
            var pdf = Sh(Mesh(4, "/BitsPerFlag 8 " + Decode, data));

            var nearRed = At(pdf, 4, 95, out var unsupported);
            Assert.Empty(unsupported);
            Assert.True(nearRed.R > 200 && nearRed.G < 60 && nearRed.A == 255, $"near the red vertex: {nearRed}");
            var nearGreen = At(pdf, 95, 95, out _);
            Assert.True(nearGreen.G > 200 && nearGreen.R < 60, $"near the green vertex: {nearGreen}");
            var nearBlue = At(pdf, 4, 4, out _);
            Assert.True(nearBlue.B > 200 && nearBlue.R < 60, $"near the blue vertex: {nearBlue}");
            var middle = At(pdf, 30, 70, out _);
            Assert.True(middle.R > 40 && middle.G > 40 && middle.B > 40 && middle.A == 255, $"blended in the middle: {middle}");
            Assert.True(At(pdf, 90, 10, out _).A < 10, "outside the triangle nothing is drawn");
        }

        [Fact]
        public void FreeFormMesh_ContinuesTrianglesWithEdgeFlags()
        {
            // A first triangle, then a vertex with flag 1 (new triangle from the last edge) and another with flag 2.
            byte[] data =
            [
                0, 0, 0, 255, 0, 0,   0, 255, 0, 255, 0, 0,   0, 0, 255, 255, 0, 0,
                1, 255, 255, 255, 0, 0,
                2, 128, 128, 255, 0, 0,
            ];
            var pdf = Sh(Mesh(4, "/BitsPerFlag 8 " + Decode, data));

            Assert.True(At(pdf, 10, 90, out _).A == 255);              // first triangle
            Assert.True(At(pdf, 90, 10, out _).A == 255);              // the flag-1 triangle fills the opposite corner
        }

        [Fact]
        public void FreeFormMesh_WithAFunction_MapsItsParameterThroughTheFunction()
        {
            const string red2blue = "<< /FunctionType 2 /Domain [0 1] /C0 [1 0 0] /C1 [0 0 1] /N 1 >>";
            // Parameter 0 at the left vertices, 1 at the right one.
            byte[] data = [0, 0, 0, 0, 0, 0, 255, 0, 0, 255, 255, 255, 0];
            var mesh = TestPdf.Stream($"/ShadingType 4 /ColorSpace /DeviceRGB /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 /Decode [0 100 0 100 0 1] /Function {red2blue}", TestPdf.Binary(data));
            var pdf = Sh(mesh);

            var left = At(pdf, 3, 60, out var unsupported);
            Assert.Empty(unsupported);
            Assert.True(left.R > 200, $"parameter 0 is red: {left}");
        }

        [Fact]
        public void Lattice_IsTriangulatedRowByRow()
        {
            // 2x2 lattice covering the page: (x, y, r, g, b); red green on the bottom row, blue white on the top.
            byte[] data = [0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 0, 0, 255, 255, 255, 255, 255, 255];
            var pdf = Sh(Mesh(5, "/VerticesPerRow 2 " + Decode, data));

            var bottomLeft = At(pdf, 3, 96, out _);
            Assert.True(bottomLeft.R > 200 && bottomLeft.G < 60, $"red corner: {bottomLeft}");
            var topRight = At(pdf, 96, 3, out _);
            Assert.True(topRight.R > 200 && topRight.G > 200 && topRight.B > 200, $"white corner: {topRight}");
        }

        /// <summary>A square Coons patch: boundary points p11 p12 p13 p14 p24 p34 p44 p43 p42 p41 p31 p21, corner colours red, green, blue, white.</summary>
        private static byte[] CoonsSquare(bool tensor)
        {
            var bytes = new List<byte> { 0 };
            (byte X, byte Y)[] boundary =
            [
                (0, 0), (85, 0), (170, 0), (255, 0), (255, 85), (255, 170), (255, 255), (170, 255), (85, 255), (0, 255), (0, 170), (0, 85),
            ];
            foreach (var (x, y) in boundary) { bytes.Add(x); bytes.Add(y); }
            if (tensor)
                foreach (var (x, y) in new (byte, byte)[] { (85, 85), (170, 85), (170, 170), (85, 170) }) { bytes.Add(x); bytes.Add(y); }
            bytes.AddRange([255, 0, 0, 0, 255, 0, 0, 0, 255, 255, 255, 255]);
            return [.. bytes];
        }

        [Theory]
        [InlineData(6)]
        [InlineData(7)]
        public void CoonsAndTensorPatches_AreTessellated_WithBilinearCornerColours(int type)
        {
            var pdf = Sh(Mesh(type, "/BitsPerFlag 8 " + Decode, CoonsSquare(type == 7)));

            var c11 = At(pdf, 3, 96, out var unsupported);                // (0,0) in the lower left: red
            Assert.Empty(unsupported);
            Assert.True(c11.R > 200 && c11.G < 60 && c11.B < 60, $"c11 red: {c11}");
            var c14 = At(pdf, 96, 96, out _);                             // (255,0) lower right: green
            Assert.True(c14.G > 200 && c14.R < 60, $"c14 green: {c14}");
            var c44 = At(pdf, 96, 3, out _);                              // (255,255) upper right: blue
            Assert.True(c44.B > 200 && c44.R < 60 && c44.G < 60, $"c44 blue: {c44}");
            var c41 = At(pdf, 3, 3, out _);                               // (0,255) upper left: white
            Assert.True(c41.R > 200 && c41.G > 200 && c41.B > 200, $"c41 white: {c41}");
        }

        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        public void PatchesThatShareAnEdge_ReadFewerPointsAndColours(int flag)
        {
            // A square patch, then a second patch whose first edge comes from the first (8 new points, 2 new colours).
            var bytes = new List<byte>(CoonsSquare(false)) { (byte)flag };
            foreach (var (x, y) in new (byte, byte)[] { (255, 85), (255, 170), (255, 255), (170, 255), (85, 255), (0, 255), (0, 170), (0, 85) }) { bytes.Add(x); bytes.Add(y); }
            bytes.AddRange([10, 20, 30, 40, 50, 60]);
            var pdf = Sh(Mesh(6, "/BitsPerFlag 8 " + Decode, [.. bytes]));

            Assert.True(At(pdf, 50, 50, out var unsupported).A == 255);
            Assert.Empty(unsupported);
        }

        [Fact]
        public void MeshAsAPattern_IsClippedToThePathItFills()
        {
            byte[] data = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255];
            var pdf = Sh(Mesh(4, "/BitsPerFlag 8 " + Decode, data), "/Pattern cs /P0 scn 0 0 30 100 re f");

            Assert.True(At(pdf, 10, 90, out var unsupported).A == 255, "inside the path and the mesh");
            Assert.Empty(unsupported);
            Assert.True(At(pdf, 60, 90, out _).A < 10, "outside the path");
        }

        [Fact]
        public void MeshPatternUsedForStroking_IsReported()
        {
            byte[] data = [0, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255, 0, 0, 0, 255, 0, 0, 255];
            var pdf = Sh(Mesh(4, "/BitsPerFlag 8 " + Decode, data), "/Pattern CS /P0 SCN 5 w 0 50 m 100 50 l S");

            At(pdf, 0, 0, out var unsupported);

            Assert.Contains("stroke with a mesh shading", unsupported);
        }

        [Theory]
        [InlineData("/BitsPerFlag 8 /Decode [0 100 0 100 0 1 0 1 0 1]", new byte[0], "empty mesh")]
        [InlineData("/BitsPerFlag 0 /Decode [0 100 0 100 0 1 0 1 0 1]", new byte[] { 0, 0, 0, 0, 0, 0 }, "mesh bit depths")]
        [InlineData("/BitsPerFlag 8", new byte[] { 0, 0, 0, 0, 0, 0 }, "mesh decode array")]
        [InlineData("/BitsPerFlag 8 /Decode [0 100 0 100 0 1]", new byte[] { 0, 0, 0, 0, 0, 0 }, "mesh decode array")]
        public void UnreadableMeshes_AreReportedWithTheReason(string entries, byte[] data, string expected)
        {
            At(Sh(Mesh(4, entries, data)), 0, 0, out var unsupported);

            Assert.Contains(expected, unsupported);
        }

        [Fact]
        public void MeshWithAnUnknownColourSpace_OrNoData_IsReported()
        {
            var noSpace = TestPdf.Stream("/ShadingType 4 /ColorSpace /Nope /BitsPerCoordinate 8 /BitsPerComponent 8 /BitsPerFlag 8 " + Decode, "x");
            At(Sh(noSpace), 0, 0, out var first);
            Assert.Contains("shading colour space", first);

            At(Sh("<< /ShadingType 4 /ColorSpace /DeviceRGB >>"), 0, 0, out var second);
            Assert.Contains("mesh shading data", second);
        }

        [Fact]
        public void DegenerateMesh_IsReported()
        {
            // All three vertices at one point: no area to draw.
            byte[] data = [0, 5, 5, 255, 0, 0, 0, 5, 5, 255, 0, 0, 0, 5, 5, 255, 0, 0];
            At(Sh(Mesh(4, "/BitsPerFlag 8 " + Decode, data)), 0, 0, out var unsupported);

            Assert.Contains("degenerate mesh", unsupported);
        }
    }
}
