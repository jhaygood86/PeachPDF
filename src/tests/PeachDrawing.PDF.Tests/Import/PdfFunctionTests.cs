using PeachDrawing.PDF.Import;

namespace PeachDrawing.PDF.Tests.Import
{
    /// <summary>PDF functions (sampled, exponential, stitching, PostScript calculator) read from a file and evaluated.</summary>
    public class PdfFunctionTests
    {
        private static PdfFunction? Load(string functionObject, params string[] more)
        {
            var objects = new List<string> { functionObject };
            objects.AddRange(more);
            var doc = PdfSourceDocument.Open(TestPdf.Build("", pageExtras: "/Fn 5 0 R", extraObjects: [.. objects]));
            return PdfFunction.Load(doc, doc.Pages[0].GetRaw("Fn"));
        }

        private static PdfFunction PostScript(string program, int outputs = 1, string domain = "[0 1]")
        {
            var range = string.Join(' ', Enumerable.Repeat("-1000 1000", outputs));
            return Load(TestPdf.Stream($"/FunctionType 4 /Domain {domain} /Range [{range}]", program))!;
        }

        [Fact]
        public void Exponential_InterpolatesWithItsExponent_AndClampsToDomainAndRange()
        {
            var f = Load("<< /FunctionType 2 /Domain [0 1] /Range [0 0.5 0 1] /C0 [0 0] /C1 [1 1] /N 2 >>")!;

            Assert.Equal(2, f.Outputs);
            Assert.Equal(0.25, f.Evaluate(0.5)[0], 6);
            Assert.Equal(0.5, f.Evaluate(1)[0], 6);          // range clamps the first output to 0.5
            Assert.Equal(1, f.Evaluate(7)[1], 6);            // the input clamps to the domain first
        }

        [Fact]
        public void Stitching_SwitchesSubfunctionAtItsBounds_AndExposesThem()
        {
            var f = Load("<< /FunctionType 3 /Domain [0 1] /Bounds [0.5] /Encode [0 1 1 0] /Functions [" +
                "<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [10] /N 1 >> << /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [10] /N 1 >>] >>")!;

            Assert.Equal([0.5], f.Bounds);
            Assert.Equal(5, f.Evaluate(0.25)[0], 6);          // first half, encoded 0..1
            Assert.Equal(10, f.Evaluate(0.5)[0], 6);          // second half starts reversed: encode 1 -> 0
            Assert.Equal(0, f.Evaluate(1)[0], 6);
        }

        [Fact]
        public void FunctionArray_IsOneFunctionWithAnOutputPerElement()
        {
            var doc = PdfSourceDocument.Open(TestPdf.Build("", pageExtras: "/Fn [5 0 R 6 0 R]", extraObjects:
                ["<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>", "<< /FunctionType 2 /Domain [0 1] /C0 [1] /C1 [0] /N 1 >>"]));
            var f = PdfFunction.Load(doc, doc.Pages[0].GetRaw("Fn"))!;

            Assert.Equal(2, f.Outputs);
            Assert.Equal([0.25, 0.75], f.Evaluate(0.25));
        }

        [Fact]
        public void Sampled_InterpolatesOneInput_AndTakesTheNearestSampleForSeveral()
        {
            var one = Load(TestPdf.Stream("/FunctionType 0 /Domain [0 1] /Range [0 1] /Size [3] /BitsPerSample 8", TestPdf.Binary([0, 128, 255])))!;
            Assert.Equal(0.25, one.Evaluate(0.25)[0], 2);
            Assert.Equal(1, one.Evaluate(1)[0], 6);

            var two = Load(TestPdf.Stream("/FunctionType 0 /Domain [0 1 0 1] /Range [0 1] /Size [2 2] /BitsPerSample 8", TestPdf.Binary([0, 64, 128, 255])))!;
            Assert.Equal(1, two.Evaluate(1, 1)[0], 6);
            Assert.Equal(0, two.Evaluate(0, 0)[0], 6);
            Assert.Equal(128 / 255.0, two.Evaluate(0, 1)[0], 6); // x varies fastest: sample (0,1) is the third
        }

        [Fact]
        public void Sampled_WithEncodeAndDecode_RemapsInputAndOutput()
        {
            var f = Load(TestPdf.Stream("/FunctionType 0 /Domain [0 1] /Range [0 10] /Size [2] /BitsPerSample 8 /Encode [1 0] /Decode [0 10]", TestPdf.Binary([0, 255])))!;

            Assert.Equal(10, f.Evaluate(0)[0], 6);
            Assert.Equal(0, f.Evaluate(1)[0], 6);
        }

        [Theory]
        [InlineData("{ 2 mul }", 0.25, 0.5)]
        [InlineData("{ 1 exch sub }", 0.25, 0.75)]
        [InlineData("{ dup 0.5 gt { pop 1 } { pop 0 } ifelse }", 0.75, 1)]
        [InlineData("{ dup 0.5 gt { pop 1 } { pop 0 } ifelse }", 0.25, 0)]
        [InlineData("{ 0.5 gt { 9 } if }", 0.75, 9)]
        [InlineData("{ pop 9 sqrt }", 0, 3)]
        [InlineData("{ pop 2 3 exp }", 0, 8)]
        [InlineData("{ pop 7 2 idiv }", 0, 3)]
        [InlineData("{ pop 7 2 mod }", 0, 1)]
        [InlineData("{ pop -4 abs 3 add neg }", 0, -7)]
        [InlineData("{ pop 2.5 floor 2.5 ceiling add 2.5 round add 2.7 truncate add }", 0, 2 + 3 + 3 + 2)]
        [InlineData("{ pop 90 sin 0 cos add }", 0, 2)]
        [InlineData("{ pop 1 1 atan }", 0, 45)]
        [InlineData("{ pop 100 log 1 ln add }", 0, 2)]
        [InlineData("{ pop 3 1 eq 3 3 eq add 3 4 ne add 4 3 ge add 4 3 le add 3 4 lt add }", 0, 4)]
        [InlineData("{ pop true false and true or false xor not }", 0, 0)]
        [InlineData("{ pop 1 2 bitshift 8 -1 bitshift add }", 0, 8)]
        [InlineData("{ pop 4 cvi cvr 1 index pop 7 8 2 copy pop pop pop pop }", 0, 4)]
        public void PostScript_Operators(string program, double input, double expected)
        {
            Assert.Equal(expected, PostScript(program).Evaluate(input)[0], 6);
        }

        [Fact]
        public void PostScript_Roll_RotatesTheTopOfTheStack()
        {
            Assert.Equal([3, 1, 2], PostScript("{ pop 1 2 3 3 1 roll }", outputs: 3).Evaluate(0));
            Assert.Equal([2, 3, 1], PostScript("{ pop 1 2 3 3 -1 roll }", outputs: 3).Evaluate(0));
        }

        [Fact]
        public void PostScript_StackUnderflow_YieldsZeroOutputsRatherThanThrowing()
        {
            Assert.Equal([0], PostScript("{ pop pop pop }").Evaluate(0.5));
        }

        [Theory]
        [InlineData("<< /FunctionType 99 /Domain [0 1] >>")]
        [InlineData("<< /FunctionType 3 /Domain [0 1] /Bounds [0.5 0.7] /Functions [<< /FunctionType 2 /Domain [0 1] /C0 [0] /C1 [1] /N 1 >>] >>")]
        [InlineData("<< /FunctionType 3 /Domain [0 1] /Bounds [] /Functions [] >>")]
        [InlineData("<< /FunctionType 3 /Domain [0 1] /Bounds [] >>")]
        [InlineData("<< /FunctionType 3 /Domain [0 1] /Bounds [] /Functions [<< /FunctionType 99 >>] >>")]
        [InlineData("<< /FunctionType 0 /Domain [0 1] /Range [0 1] /Size [2] /BitsPerSample 8 >>")]
        [InlineData("<< /FunctionType 4 /Domain [0 1] /Range [0 1] >>")]
        public void UnreadableFunctions_AreNull(string functionObject)
        {
            Assert.Null(Load(functionObject));
        }

        [Fact]
        public void UnbalancedPostScript_IsNull_AndANonFunctionItemIsNull()
        {
            Assert.Null(Load(TestPdf.Stream("/FunctionType 4 /Domain [0 1] /Range [0 1]", "{ 1 2 add")));
            Assert.Null(Load(TestPdf.Stream("/FunctionType 4 /Domain [0 1] /Range [0 1]", "1 2 add")));
            Assert.Null(Load("42"));
        }
    }
}
