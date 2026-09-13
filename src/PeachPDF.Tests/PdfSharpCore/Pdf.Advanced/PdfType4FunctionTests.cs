using PeachPDF.Html.Adapters.Entities;
using PeachPDF.PdfSharpCore.Pdf;
using PeachPDF.PdfSharpCore.Pdf.Advanced;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using System.Text;

namespace PeachPDF.Tests.PdfSharpCoreTests.Pdf.Advanced
{
    /// <summary>
    /// Per this repo's testing convention, a token-substring check on a generated PDF function's
    /// PostScript program is not proof it computes the right thing - so beyond the shape assertions
    /// (FunctionType/Domain/Range/indirection), these tests run the generated PostScript through
    /// <see cref="PostScriptCalculator"/>, a tiny interpreter covering exactly the operator subset
    /// <see cref="PdfType4Function"/> emits, and check the numeric result against
    /// <see cref="ColorMatrix.Apply"/> directly. This is what actually exercises the stack-juggling
    /// (<c>index</c>/<c>roll</c>) arithmetic, which a substring match can't.
    /// </summary>
    public class PdfType4FunctionTests
    {
        private static PdfDocument NewDocument() => new();

        [Fact]
        public void BuildColorMatrixFunction_HasType4DomainAndRange()
        {
            var document = NewDocument();
            var fn = PdfType4Function.BuildColorMatrixFunction(document, ColorMatrix.Identity);

            Assert.Equal(4, fn.Elements.GetInteger("/FunctionType"));
            Assert.Equal("[0 1 0 1 0 1 0 1]", fn.Elements["/Domain"].ToString());
            Assert.Equal("[0 1 0 1 0 1 0 1]", fn.Elements["/Range"].ToString());
            Assert.True(fn.IsIndirect, "A Type 4 function is a stream object and must be indirect.");
        }

        [Theory]
        [InlineData(1f, 0f, 0f, 1f)] // pure red
        [InlineData(0f, 1f, 0f, 1f)] // pure green
        [InlineData(0f, 0f, 1f, 1f)] // pure blue
        [InlineData(0.2f, 0.4f, 0.6f, 0.8f)] // arbitrary
        public void BuildColorMatrixFunction_EvaluatesToColorMatrixApply(float r, float g, float b, float a)
        {
            // Grayscale (feColorMatrix-style luminance weights) - genuinely exercises every cross term,
            // unlike Identity, which would pass even if the index/roll bookkeeping silently dropped a
            // channel (every coefficient would still be 0 or 1).
            var matrix = new ColorMatrix(new Matrix4x4(
                0.2126f, 0.2126f, 0.2126f, 0,
                0.7152f, 0.7152f, 0.7152f, 0,
                0.0722f, 0.0722f, 0.0722f, 0,
                0, 0, 0, 1), Vector4.Zero);

            var document = NewDocument();
            var fn = PdfType4Function.BuildColorMatrixFunction(document, matrix);
            var program = Encoding.ASCII.GetString(fn.Stream.Value);

            var actual = PostScriptCalculator.Evaluate(program, r, g, b, a);
            var expected = matrix.Apply(new Vector4(r, g, b, a));

            Assert.Equal(4, actual.Length);
            Assert.Equal(expected.X, actual[0], 4);
            Assert.Equal(expected.Y, actual[1], 4);
            Assert.Equal(expected.Z, actual[2], 4);
            Assert.Equal(expected.W, actual[3], 4);
        }

        [Fact]
        public void BuildColorMatrixFunction_ClampsOutOfRangeResultsTo01()
        {
            // A matrix that pushes R past 1 and B below 0 for an in-range input.
            var matrix = new ColorMatrix(new Matrix4x4(
                3, 0, 0, 0,
                0, 1, 0, 0,
                0, -3, 0, 0,
                0, 0, 0, 1), Vector4.Zero);

            var document = NewDocument();
            var fn = PdfType4Function.BuildColorMatrixFunction(document, matrix);
            var program = Encoding.ASCII.GetString(fn.Stream.Value);

            var actual = PostScriptCalculator.Evaluate(program, 0.8f, 0.8f, 0.5f, 1f);

            Assert.Equal(1.0, actual[0], 4); // 3 * 0.8 = 2.4, clamped to 1
            Assert.Equal(0.0, actual[2], 4); // -3 * 0.8 = -2.4, clamped to 0
        }

        [Fact]
        public void BuildInvertFunction_ComputesOneMinusX()
        {
            var document = NewDocument();
            var fn = PdfType4Function.BuildInvertFunction(document);

            Assert.Equal(4, fn.Elements.GetInteger("/FunctionType"));
            Assert.Equal("[0 1]", fn.Elements["/Domain"].ToString());
            Assert.True(fn.IsIndirect);

            var program = Encoding.ASCII.GetString(fn.Stream.Value);
            Assert.Equal(0.7, PostScriptCalculator.Evaluate(program, 0.3f)[0], 4);
            Assert.Equal(0.0, PostScriptCalculator.Evaluate(program, 1f)[0], 4);
            Assert.Equal(1.0, PostScriptCalculator.Evaluate(program, 0f)[0], 4);
        }

        [Fact]
        public void BuildChannelIndependentTransferFunction_UniformScale_ReturnsSingleFunction()
        {
            // brightness(1.5): R, G, B all scale identically - ISO 32000-1 §8.6.5.3's "a single
            // function applied to all process colorants" form, not the three-function array.
            var matrix = new ColorMatrix(new Matrix4x4(
                1.5f, 0, 0, 0,
                0, 1.5f, 0, 0,
                0, 0, 1.5f, 0,
                0, 0, 0, 1), Vector4.Zero);

            var document = NewDocument();
            var result = PdfType4Function.BuildChannelIndependentTransferFunction(document, matrix);

            var fn = Assert.IsType<PdfDictionary>(result);
            var program = Encoding.ASCII.GetString(fn.Stream.Value);
            Assert.Equal(0.6, PostScriptCalculator.Evaluate(program, 0.4f)[0], 4);
        }

        [Fact]
        public void BuildChannelIndependentTransferFunction_DifferingChannels_ReturnsArrayOfThree()
        {
            var matrix = new ColorMatrix(new Matrix4x4(
                2f, 0, 0, 0,
                0, 3f, 0, 0,
                0, 0, 4f, 0,
                0, 0, 0, 1), Vector4.Zero);

            var document = NewDocument();
            var result = PdfType4Function.BuildChannelIndependentTransferFunction(document, matrix);

            var array = Assert.IsType<PdfArray>(result);
            Assert.Equal(3, array.Elements.Count);

            // Each array element is stored as an indirect reference to its own function (see
            // PdfArray.Elements.Add's auto-conversion) - dereference through the document to check
            // each channel's own scale independently.
            double[] expectedScales = [2, 3, 4];
            for (int i = 0; i < 3; i++)
            {
                var reference = Assert.IsType<PdfReference>(array.Elements[i]);
                var fn = Assert.IsType<PdfDictionary>(reference.Value);
                var program = Encoding.ASCII.GetString(fn.Stream.Value);
                Assert.Equal(expectedScales[i] * 0.2, PostScriptCalculator.Evaluate(program, 0.2f)[0], 4);
            }
        }

        /// <summary>
        /// A minimal PostScript calculator interpreter covering exactly the operators
        /// <see cref="PdfType4Function"/> emits (arithmetic, stack manipulation, and <c>if</c> with a
        /// literal boolean test) - enough to actually execute a generated Type 4 function's program
        /// against sample inputs in-process, rather than trusting the generated text by inspection.
        /// </summary>
        private static class PostScriptCalculator
        {
            public static double[] Evaluate(string program, params float[] inputs)
            {
                var tokens = new Queue<string>(program.Replace("{", " { ").Replace("}", " } ")
                    .Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries));

                var body = (List<object>)Parse(tokens)[0];
                var stack = new List<object>(inputs.Select(v => (object)(double)v));
                Execute(body, stack);

                return stack.Select(v => (double)v).ToArray();
            }

            private static List<object> Parse(Queue<string> tokens)
            {
                var result = new List<object>();
                while (tokens.Count > 0)
                {
                    var t = tokens.Dequeue();
                    if (t == "{")
                        result.Add(Parse(tokens));
                    else if (t == "}")
                        return result;
                    else
                        result.Add(t);
                }
                return result;
            }

            private static void Execute(List<object> body, List<object> stack)
            {
                foreach (var item in body)
                {
                    if (item is List<object> proc)
                    {
                        stack.Add(proc);
                        continue;
                    }

                    switch ((string)item)
                    {
                        case "mul": { double b = Pop(stack), a = Pop(stack); stack.Add(a * b); break; }
                        case "add": { double b = Pop(stack), a = Pop(stack); stack.Add(a + b); break; }
                        case "sub": { double b = Pop(stack), a = Pop(stack); stack.Add(a - b); break; }
                        case "dup": stack.Add(stack[^1]); break;
                        case "pop": stack.RemoveAt(stack.Count - 1); break;
                        case "exch": { (stack[^1], stack[^2]) = (stack[^2], stack[^1]); break; }
                        case "index": { int n = (int)Pop(stack); stack.Add(stack[^(n + 1)]); break; }
                        case "roll": { int j = (int)Pop(stack); int n = (int)Pop(stack); Roll(stack, n, j); break; }
                        case "lt": { double b = Pop(stack), a = Pop(stack); stack.Add(a < b); break; }
                        case "gt": { double b = Pop(stack), a = Pop(stack); stack.Add(a > b); break; }
                        case "if":
                            {
                                var ifProc = (List<object>)stack[^1];
                                stack.RemoveAt(stack.Count - 1);
                                bool cond = (bool)stack[^1];
                                stack.RemoveAt(stack.Count - 1);
                                if (cond) Execute(ifProc, stack);
                                break;
                            }
                        default:
                            stack.Add(double.Parse((string)item, CultureInfo.InvariantCulture));
                            break;
                    }
                }
            }

            private static double Pop(List<object> stack)
            {
                var v = (double)stack[^1];
                stack.RemoveAt(stack.Count - 1);
                return v;
            }

            private static void Roll(List<object> stack, int n, int j)
            {
                if (n <= 0) return;
                int start = stack.Count - n;
                var segment = stack.GetRange(start, n);
                j = ((j % n) + n) % n;
                var result = new object[n];
                for (int i = 0; i < n; i++)
                    result[(i + j) % n] = segment[i];
                stack.RemoveRange(start, n);
                stack.AddRange(result);
            }
        }
    }
}
