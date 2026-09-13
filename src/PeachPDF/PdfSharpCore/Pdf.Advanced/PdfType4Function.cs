using PeachPDF.Html.Adapters.Entities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace PeachPDF.PdfSharpCore.Pdf.Advanced
{
    /// <summary>
    /// Builds PDF Type 4 (PostScript calculator) function objects (ISO 32000-1 §7.10.5). Unlike every
    /// other function this codebase builds (<see cref="PdfShading"/>'s Type 2/Type 3 functions, both
    /// plain dictionaries with no stream), a Type 4 function IS a stream object - its PostScript program
    /// is the stream content - so every function built here is registered as an indirect object
    /// (<c>document._irefTable.Add(...)</c>) before <see cref="PdfDictionary.CreateStream"/> is called,
    /// matching the convention <see cref="PdfShading"/>'s own Form XObjects already use for the same
    /// reason (see e.g. its <c>BuildAlphaExtGStateIfNeeded</c>). Callers attach the result via
    /// <c>Elements.SetReference</c>, never a plain <c>Elements[key] = ...</c> assignment - see
    /// <see cref="PdfExtGState.TransferFunction"/>/<see cref="PdfSoftMask.TransferFunction"/>.
    /// </summary>
    /// <remarks>
    /// See <see cref="ColorMatrix"/>'s remarks for why <see cref="BuildColorMatrixFunction"/>'s general
    /// 4-in/4-out function is NOT plugged into an ExtGState <c>/TR</c> directly for a cross-channel
    /// matrix - only <see cref="BuildChannelIndependentTransferFunction"/>'s per-component functions are
    /// spec-legal there. The 4-in/4-out function is still a genuine, correctly-built PDF construct; a
    /// later phase's legitimate use for it is as a raster image's DeviceN colour-space tint-transform
    /// (ISO 32000-1 §8.6.6.2), where "one function evaluated per pixel's stored components" is exactly
    /// what the format already means, no compositing-time cross-channel mechanism required.
    /// </remarks>
    internal static class PdfType4Function
    {
        private const string Format = "0.######";

        /// <summary>
        /// Builds a Type 4 function computing <c>[R' G' B' A'] = matrix.Linear * [R G B A] + matrix.Offset</c>,
        /// each output clamped to <c>[0, 1]</c>. 4 inputs and 4 outputs even though most real callers only
        /// need 3 (R/G/B) out - <c>feColorMatrix</c>/CSS filters conceptually operate on all of RGBA even
        /// when the alpha row/column is typically identity, and a function's <c>/Domain</c>/<c>/Range</c>
        /// arity has to match how it's actually invoked by whatever colour space or graphics-state entry
        /// ends up referencing it.
        /// </summary>
        public static PdfDictionary BuildColorMatrixFunction(PdfDocument document, ColorMatrix matrix)
        {
            var fn = new PdfDictionary(document);
            document._irefTable.Add(fn);
            fn.Elements["/FunctionType"] = new PdfInteger(4);
            fn.Elements["/Domain"] = new PdfLiteral("[0 1 0 1 0 1 0 1]");
            fn.Elements["/Range"] = new PdfLiteral("[0 1 0 1 0 1 0 1]");

            var m = matrix.Linear;
            var o = matrix.Offset;

            // Coefficients per output channel: (weight-on-R, weight-on-G, weight-on-B, weight-on-A, bias).
            // Linear's row is the INPUT component and column is the OUTPUT component (see ColorMatrix.Linear's
            // remarks - Vector4.Transform treats a color as a row vector), so output channel c reads column c:
            // M1c (from R), M2c (from G), M3c (from B), M4c (from A).
            float[][] rows =
            [
                [m.M11, m.M21, m.M31, m.M41, o.X], // R'
                [m.M12, m.M22, m.M32, m.M42, o.Y], // G'
                [m.M13, m.M23, m.M33, m.M43, o.Z], // B'
                [m.M14, m.M24, m.M34, m.M44, o.W], // A'
            ];

            var program = new StringBuilder("{ ");

            // Virtual stack, bottom-to-top, mirroring the real PostScript operand stack exactly (one
            // entry per real stack slot) so each fetch below can compute the right "N index" distance.
            // "r"/"g"/"b"/"a" mark the 4 original inputs, which stay put - never popped - until the
            // final cleanup below; every other entry (a fetched copy, a pushed weight/bias literal, a
            // mul/add/clamp result) is opaque (null) and exists purely for depth-counting, since nothing
            // ever needs to fetch THOSE by name.
            var stack = new List<string?> { "r", "g", "b", "a" };

            foreach (var row in rows)
                AppendChannelExpression(program, stack, row);

            // Stack is now [r g b a R' G' B' A'] (8 items) - roll the original 4 inputs to the top and
            // discard them, leaving [R' G' B' A'] as this function's 4 outputs in Range order.
            program.Append("8 4 roll pop pop pop pop }");

            fn.CreateStream(Encoding.ASCII.GetBytes(program.ToString()));
            return fn;
        }

        /// <summary>
        /// Builds a 1-input/1-output Type 4 function computing <c>1 - x</c> (PostScript <c>1 exch sub</c>),
        /// for inverting a soft mask's own alpha/luminosity value via <see cref="PdfSoftMask.TransferFunction"/>.
        /// Unlike <see cref="BuildColorMatrixFunction"/>, this is unconditionally spec-legal for its use
        /// site: a soft mask's <c>/TR</c> genuinely is a single-value-in/single-value-out function
        /// (ISO 32000-1 §11.6.5.2), no cross-channel question ever arises for it.
        /// </summary>
        public static PdfDictionary BuildInvertFunction(PdfDocument document)
        {
            var fn = new PdfDictionary(document);
            document._irefTable.Add(fn);
            fn.Elements["/FunctionType"] = new PdfInteger(4);
            fn.Elements["/Domain"] = new PdfLiteral("[0 1]");
            fn.Elements["/Range"] = new PdfLiteral("[0 1]");
            fn.CreateStream(Encoding.ASCII.GetBytes("{ 1 exch sub }"));
            return fn;
        }

        /// <summary>
        /// Builds the ExtGState <c>/TR</c> value for a channel-independent <see cref="ColorMatrix"/>
        /// (<see cref="ColorMatrix.IsChannelIndependent"/> must already be true - this does not check).
        /// Returns a single shared 1-in/1-out function when R, G and B all apply the identical scale and
        /// offset (the common case - CSS <c>brightness()</c>/<c>contrast()</c>/<c>invert()</c> all apply
        /// uniformly across R/G/B), matching ISO 32000-1 §8.6.5.3's "a single function... applied to all
        /// process colorants" form; otherwise an array of three functions, one per colorant, per the same
        /// section's alternative form.
        /// </summary>
        public static PdfItem BuildChannelIndependentTransferFunction(PdfDocument document, ColorMatrix matrix)
        {
            var m = matrix.Linear;
            var o = matrix.Offset;

            // Row = input component, column = output component (see ColorMatrix.Linear's remarks); the
            // diagonal entries below are each channel's own weight since IsChannelIndependent guarantees
            // every off-diagonal contribution is already ~0.
            double scaleR = m.M11, scaleG = m.M22, scaleB = m.M33;
            double offsetR = o.X, offsetG = o.Y, offsetB = o.Z;

            const double epsilon = 1e-5;
            bool uniform =
                System.Math.Abs(scaleR - scaleG) < epsilon && System.Math.Abs(scaleG - scaleB) < epsilon &&
                System.Math.Abs(offsetR - offsetG) < epsilon && System.Math.Abs(offsetG - offsetB) < epsilon;

            if (uniform)
                return BuildLinearFunction(document, scaleR, offsetR);

            var array = new PdfArray(document);
            array.Elements.Add(BuildLinearFunction(document, scaleR, offsetR));
            array.Elements.Add(BuildLinearFunction(document, scaleG, offsetG));
            array.Elements.Add(BuildLinearFunction(document, scaleB, offsetB));
            return array;
        }

        /// <summary>
        /// Builds a 1-input/1-output Type 4 function computing <c>clamp(scale * x + offset, 0, 1)</c>.
        /// </summary>
        private static PdfDictionary BuildLinearFunction(PdfDocument document, double scale, double offset)
        {
            var fn = new PdfDictionary(document);
            document._irefTable.Add(fn);
            fn.Elements["/FunctionType"] = new PdfInteger(4);
            fn.Elements["/Domain"] = new PdfLiteral("[0 1]");
            fn.Elements["/Range"] = new PdfLiteral("[0 1]");

            var program = new StringBuilder("{ ");
            program.Append(scale.ToString(Format, CultureInfo.InvariantCulture)).Append(" mul ");
            program.Append(offset.ToString(Format, CultureInfo.InvariantCulture)).Append(" add ");
            AppendClampTop(program);
            program.Append('}');

            fn.CreateStream(Encoding.ASCII.GetBytes(program.ToString()));
            return fn;
        }

        /// <summary>
        /// Appends the PostScript computing one output channel - <c>weights[0..3]</c> times the current
        /// <c>r g b a</c> (fetched non-destructively via <c>index</c> so later channels can still reach
        /// them) plus <c>weights[4]</c>, clamped to <c>[0, 1]</c> - leaving the single result value on
        /// top of the real stack, one item deeper than <paramref name="stack"/> reported before this
        /// call (<paramref name="stack"/> is updated to match, so the next channel's own fetches see the
        /// correct depth).
        /// </summary>
        /// <remarks>
        /// Every helper here (<see cref="AppendFetch"/>, <see cref="AppendLiteralAndCombine"/>,
        /// <see cref="Combine"/>) mutates <paramref name="stack"/> by EXACTLY the real operand-stack
        /// effect of the PostScript it just emitted - a fetch pushes one opaque entry, a literal push
        /// followed by <c>mul</c>/<c>add</c> nets to a pop-2-push-1. Getting this precise (rather than
        /// "the net effect across a whole channel is +1, so just add one placeholder at the end") is the
        /// difference between <c>index</c> distances that are right and ones that are off by however
        /// many un-popped placeholders accumulated earlier - exactly the bug an early version of this
        /// method had (caught by <c>PdfType4FunctionTests</c> actually executing the generated program
        /// against a cross-channel matrix, not by inspecting it).
        /// </remarks>
        private static void AppendChannelExpression(StringBuilder program, List<string?> stack, float[] weights)
        {
            string[] inputs = ["r", "g", "b", "a"];
            bool first = true;

            for (int i = 0; i < 4; i++)
            {
                if (weights[i] == 0f)
                    continue;

                AppendFetch(program, stack, inputs[i]);
                AppendLiteralAndCombine(program, stack, weights[i], "mul");
                if (!first)
                {
                    program.Append("add ");
                    Combine(stack);
                }
                first = false;
            }

            if (first)
            {
                // Every weight was zero - push a literal 0 as the running sum so the bias add below
                // still has an operand to add to.
                program.Append("0 ");
                stack.Add(null);
            }

            AppendLiteralAndCombine(program, stack, weights[4], "add");
            AppendClampTop(program); // one value in, one (clamped) value out - stack shape unchanged
        }

        /// <summary>
        /// Emits <c>{depth} index</c> to duplicate <paramref name="name"/>'s current position in
        /// <paramref name="stack"/> onto the top of the real PostScript stack, and records the fetched
        /// copy as a new (opaque - nothing ever fetches a fetched COPY by name) entry.
        /// </summary>
        private static void AppendFetch(StringBuilder program, List<string?> stack, string name)
        {
            int sourceIndex = stack.LastIndexOf(name);
            if (sourceIndex < 0)
                throw new InvalidOperationException($"Input '{name}' is not on the virtual stack - this is a bug in {nameof(PdfType4Function)}.");

            int distanceFromTop = stack.Count - 1 - sourceIndex;
            program.Append(distanceFromTop).Append(" index ");
            stack.Add(null);
        }

        /// <summary>
        /// Pushes a literal number and immediately combines it with what's now below it via
        /// <paramref name="op"/> (<c>mul</c> or <c>add</c>) - net stack effect zero on its own (push +1,
        /// combine -1), so this only changes <paramref name="stack"/>'s depth when paired with a
        /// preceding, separately-tracked push (e.g. <see cref="AppendFetch"/>).
        /// </summary>
        private static void AppendLiteralAndCombine(StringBuilder program, List<string?> stack, float value, string op)
        {
            program.Append(value.ToString(Format, CultureInfo.InvariantCulture)).Append(' ').Append(op).Append(' ');
            stack.Add(null);
            Combine(stack);
        }

        /// <summary>
        /// Records a 2-operand-in/1-operand-out PostScript operator's (<c>mul</c>/<c>add</c>) real
        /// stack effect on <paramref name="stack"/>.
        /// </summary>
        private static void Combine(List<string?> stack)
        {
            stack.RemoveAt(stack.Count - 1);
            stack.RemoveAt(stack.Count - 1);
            stack.Add(null);
        }

        /// <summary>
        /// Appends PostScript clamping the top-of-stack value to <c>[0, 1]</c>, using the
        /// <c>dup ... lt/gt {pop ...} if</c> idiom Type 4 functions' restricted operator set supports
        /// (ISO 32000-1 Table 42) - net stack effect is one value in, one (clamped) value out.
        /// </summary>
        private static void AppendClampTop(StringBuilder program)
        {
            program.Append("dup 0 lt { pop 0 } if dup 1 gt { pop 1 } if ");
        }
    }
}
