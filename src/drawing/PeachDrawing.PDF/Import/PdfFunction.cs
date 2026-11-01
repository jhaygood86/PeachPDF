using System;
using System.Collections.Generic;
using System.Globalization;
using PeachDrawing.PDF.Engine;

namespace PeachDrawing.PDF.Import;

/// <summary>
/// A PDF function (ISO 32000-1, 7.10): sampled (type 0), exponential (2), stitching (3) and PostScript calculator (4).
/// A function array (one single-output function per component) is read as one function.
/// </summary>
internal sealed class PdfFunction
{
    readonly Func<double[], double[]> _evaluate;
    readonly double[] _domain;
    readonly double[]? _range;

    PdfFunction(Func<double[], double[]> evaluate, double[] domain, double[]? range, int outputs, IReadOnlyList<double> bounds)
    {
        _evaluate = evaluate;
        _domain = domain;
        _range = range;
        Outputs = outputs;
        Bounds = bounds;
    }

    /// <summary>The number of values the function returns.</summary>
    public int Outputs { get; }

    /// <summary>For a stitching function, the input values where it switches subfunction; empty otherwise.</summary>
    public IReadOnlyList<double> Bounds { get; }

    /// <summary>The lower end of the first input's domain.</summary>
    public double DomainStart => _domain.Length > 0 ? _domain[0] : 0;

    /// <summary>The upper end of the first input's domain.</summary>
    public double DomainEnd => _domain.Length > 1 ? _domain[1] : 1;

    /// <summary>Evaluates the function, clamping the inputs to its domain and the outputs to its range.</summary>
    public double[] Evaluate(params double[] input)
    {
        var x = (double[])input.Clone();
        for (int i = 0; i < x.Length && i * 2 + 1 < _domain.Length; i++) x[i] = Math.Clamp(x[i], _domain[i * 2], _domain[i * 2 + 1]);
        var y = _evaluate(x);
        if (_range is not null)
            for (int i = 0; i < y.Length && i * 2 + 1 < _range.Length; i++) y[i] = Math.Clamp(y[i], _range[i * 2], _range[i * 2 + 1]);
        return y;
    }

    /// <summary>Reads a function (or an array of functions); null when it is of a kind that cannot be read.</summary>
    public static PdfFunction? Load(PdfSourceDocument doc, PdfItem? item, int depth = 0)
    {
        if (depth > 16) return null;
        var resolved = doc.Resolve(item);
        if (resolved is PdfArray arr)
        {
            var parts = new List<PdfFunction>();
            foreach (var e in arr.Elements)
            {
                var f = Load(doc, e, depth + 1);
                if (f is null) return null;
                parts.Add(f);
            }
            if (parts.Count == 0) return null;
            return new PdfFunction(x =>
            {
                var o = new double[parts.Count];
                for (int i = 0; i < o.Length; i++) o[i] = parts[i].Evaluate(x)[0];
                return o;
            }, parts[0]._domain, null, parts.Count, []);
        }
        if (resolved is not PdfDictionary d) return null;
        var domain = Numbers(doc, d, "Domain") ?? [0, 1];
        var range = Numbers(doc, d, "Range");
        switch (doc.IntOf(d.GetRaw("FunctionType")))
        {
            case 2:
            {
                var c0 = Numbers(doc, d, "C0") ?? [0];
                var c1 = Numbers(doc, d, "C1") ?? [1];
                double n = doc.NumberOf(d.GetRaw("N"), 1);
                int outputs = Math.Min(c0.Length, c1.Length);
                return new PdfFunction(x =>
                {
                    var o = new double[outputs];
                    double t = n == 1 ? x[0] : Math.Pow(x[0], n);
                    for (int i = 0; i < outputs; i++) o[i] = c0[i] + t * (c1[i] - c0[i]);
                    return o;
                }, domain, range, outputs, []);
            }
            case 3:
            {
                var subs = new List<PdfFunction>();
                if (doc.ArrayOf(d, "Functions") is not { } fa) return null;
                foreach (var e in fa.Elements)
                {
                    var f = Load(doc, e, depth + 1);
                    if (f is null) return null;
                    subs.Add(f);
                }
                var bounds = Numbers(doc, d, "Bounds") ?? [];
                var encode = Numbers(doc, d, "Encode") ?? [];
                if (subs.Count == 0 || bounds.Length != subs.Count - 1) return null;
                double d0 = domain[0], d1 = domain[1];
                return new PdfFunction(x =>
                {
                    double v = x[0];
                    int k = 0;
                    while (k < bounds.Length && v >= bounds[k]) k++;
                    double lo = k == 0 ? d0 : bounds[k - 1];
                    double hi = k == bounds.Length ? d1 : bounds[k];
                    double e0 = encode.Length > k * 2 + 1 ? encode[k * 2] : 0, e1 = encode.Length > k * 2 + 1 ? encode[k * 2 + 1] : 1;
                    double mapped = hi == lo ? e0 : e0 + (v - lo) * (e1 - e0) / (hi - lo);
                    return subs[k].Evaluate(mapped);
                }, domain, range, subs[0].Outputs, bounds);
            }
            case 0:
                return Sampled(doc, d, domain, range);
            case 4:
                return PostScript(doc, d, domain, range);
            default:
                return null;
        }
    }

    static double[]? Numbers(PdfSourceDocument doc, PdfDictionary d, string key)
    {
        if (doc.ArrayOf(d, key) is not { } a) return null;
        var o = new double[a.Elements.Count];
        for (int i = 0; i < o.Length; i++) o[i] = doc.NumberOf(a.Elements[i]);
        return o;
    }

    static PdfFunction? Sampled(PdfSourceDocument doc, PdfDictionary d, double[] domain, double[]? range)
    {
        if (d.Stream is null || range is null) return null;
        var size = Numbers(doc, d, "Size");
        int bps = doc.IntOf(d.GetRaw("BitsPerSample"));
        if (size is null || size.Length == 0 || bps is not (1 or 2 or 4 or 8 or 12 or 16 or 24 or 32)) return null;
        int m = size.Length, n = range.Length / 2;
        var encode = Numbers(doc, d, "Encode");
        var decode = Numbers(doc, d, "Decode") ?? range;
        var data = doc.DecodeStream(d).Data;
        double max = Math.Pow(2, bps) - 1;

        double Sample(int[] index, int comp)
        {
            long flat = 0, stride = 1;
            for (int i = 0; i < m; i++) { flat += index[i] * stride; stride *= (long)size[i]; }
            long bit = (flat * n + comp) * bps;
            long v = 0;
            for (int b = 0; b < bps; b++)
            {
                long pos = bit + b;
                int byteIndex = (int)(pos >> 3);
                int bitVal = byteIndex < data.Length ? (data[byteIndex] >> (7 - (int)(pos & 7))) & 1 : 0;
                v = (v << 1) | (uint)bitVal;
            }
            return v / max;
        }

        return new PdfFunction(x =>
        {
            var e = new double[m];
            for (int i = 0; i < m; i++)
            {
                double dm0 = domain[i * 2], dm1 = domain[i * 2 + 1];
                double e0 = encode is not null && encode.Length > i * 2 + 1 ? encode[i * 2] : 0;
                double e1 = encode is not null && encode.Length > i * 2 + 1 ? encode[i * 2 + 1] : size[i] - 1;
                e[i] = dm1 == dm0 ? e0 : e0 + (x[i] - dm0) * (e1 - e0) / (dm1 - dm0);
                e[i] = Math.Clamp(e[i], 0, size[i] - 1);
            }
            var o = new double[n];
            var idx = new int[m];
            for (int c = 0; c < n; c++)
            {
                double value;
                if (m == 1)
                {
                    int i0 = (int)Math.Floor(e[0]);
                    int i1 = Math.Min(i0 + 1, (int)size[0] - 1);
                    idx[0] = i0; double s0 = Sample(idx, c);
                    idx[0] = i1; double s1 = Sample(idx, c);
                    value = s0 + (e[0] - i0) * (s1 - s0);
                }
                else if (m == 2)
                {
                    // Two inputs (a function-based shading): interpolate between the four surrounding samples.
                    int x0 = (int)Math.Floor(e[0]), y0 = (int)Math.Floor(e[1]);
                    int x1 = Math.Min(x0 + 1, (int)size[0] - 1), y1 = Math.Min(y0 + 1, (int)size[1] - 1);
                    double fx = e[0] - x0, fy = e[1] - y0;
                    double At(int ix, int iy) { idx[0] = ix; idx[1] = iy; return Sample(idx, c); }
                    value = At(x0, y0) * (1 - fx) * (1 - fy) + At(x1, y0) * fx * (1 - fy) + At(x0, y1) * (1 - fx) * fy + At(x1, y1) * fx * fy;
                }
                else
                {
                    for (int i = 0; i < m; i++) idx[i] = (int)Math.Round(e[i]);
                    value = Sample(idx, c);
                }
                double dec0 = decode[c * 2], dec1 = decode[c * 2 + 1];
                o[c] = dec0 + value * (dec1 - dec0);
            }
            return o;
        }, domain, range, n, []);
    }

    // ------------------------------------------------------------ type 4: PostScript calculator

    static PdfFunction? PostScript(PdfSourceDocument doc, PdfDictionary d, double[] domain, double[]? range)
    {
        if (d.Stream is null || range is null) return null;
        var lx = new PdfLexer(doc.DecodeStream(d).Data);
        if (lx.Next() is not { Kind: PdfTokenKind.Keyword, Text: "{" }) return null;
        var program = ParseBlock(lx);
        if (program is null) return null;
        int outputs = range.Length / 2;
        return new PdfFunction(x =>
        {
            var stack = new List<double>(x);
            try { Run(program, stack); } catch (Exception e) when (e is InvalidOperationException or ArgumentOutOfRangeException or DivideByZeroException) { }
            var o = new double[outputs];
            for (int i = 0; i < outputs; i++)
            {
                int at = stack.Count - outputs + i;
                o[i] = at >= 0 && at < stack.Count ? stack[at] : 0;
            }
            return o;
        }, domain, range, outputs, []);
    }

    abstract record Node;
    sealed record Num(double Value) : Node;
    sealed record Op(string Name) : Node;
    sealed record Block(List<Node> Items) : Node;

    static Block? ParseBlock(PdfLexer lx)
    {
        var items = new List<Node>();
        while (true)
        {
            var t = lx.Next();
            switch (t.Kind)
            {
                case PdfTokenKind.Eof: return null;
                case PdfTokenKind.Integer or PdfTokenKind.Real: items.Add(new Num(t.Real)); break;
                case PdfTokenKind.Keyword when t.Text == "{":
                    var inner = ParseBlock(lx);
                    if (inner is null) return null;
                    items.Add(inner);
                    break;
                case PdfTokenKind.Keyword when t.Text == "}": return new Block(items);
                case PdfTokenKind.Keyword: items.Add(new Op(t.Text)); break;
            }
        }
    }

    static void Run(Block block, List<double> s)
    {
        for (int i = 0; i < block.Items.Count; i++)
        {
            switch (block.Items[i])
            {
                case Num n: s.Add(n.Value); break;
                case Block b:
                {
                    // A block is only meaningful before "if" or "ifelse".
                    if (i + 1 < block.Items.Count && block.Items[i + 1] is Op { Name: "if" })
                    {
                        double c = Pop(s);
                        if (c != 0) Run(b, s);
                        i++;
                    }
                    else if (i + 2 < block.Items.Count && block.Items[i + 1] is Block b2 && block.Items[i + 2] is Op { Name: "ifelse" })
                    {
                        double c = Pop(s);
                        Run(c != 0 ? b : b2, s);
                        i += 2;
                    }
                    break;
                }
                case Op op: Apply(op.Name, s); break;
            }
        }
    }

    static double Pop(List<double> s)
    {
        if (s.Count == 0) throw new InvalidOperationException("stack underflow");
        double v = s[^1];
        s.RemoveAt(s.Count - 1);
        return v;
    }

    static void Apply(string op, List<double> s)
    {
        double a, b;
        switch (op)
        {
            case "add": b = Pop(s); a = Pop(s); s.Add(a + b); break;
            case "sub": b = Pop(s); a = Pop(s); s.Add(a - b); break;
            case "mul": b = Pop(s); a = Pop(s); s.Add(a * b); break;
            case "div": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : a / b); break;
            case "idiv": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : Math.Truncate(a / b)); break;
            case "mod": b = Pop(s); a = Pop(s); s.Add(b == 0 ? 0 : a % b); break;
            case "neg": s.Add(-Pop(s)); break;
            case "abs": s.Add(Math.Abs(Pop(s))); break;
            case "sqrt": s.Add(Math.Sqrt(Pop(s))); break;
            case "sin": s.Add(Math.Sin(Pop(s) * Math.PI / 180)); break;
            case "cos": s.Add(Math.Cos(Pop(s) * Math.PI / 180)); break;
            case "atan": b = Pop(s); a = Pop(s); { double r = Math.Atan2(a, b) * 180 / Math.PI; s.Add(r < 0 ? r + 360 : r); } break;
            case "exp": b = Pop(s); a = Pop(s); s.Add(Math.Pow(a, b)); break;
            case "ln": s.Add(Math.Log(Pop(s))); break;
            case "log": s.Add(Math.Log10(Pop(s))); break;
            case "cvi" or "truncate": s.Add(Math.Truncate(Pop(s))); break;
            case "cvr": break;
            case "floor": s.Add(Math.Floor(Pop(s))); break;
            case "ceiling": s.Add(Math.Ceiling(Pop(s))); break;
            case "round": s.Add(Math.Floor(Pop(s) + 0.5)); break;
            case "eq": b = Pop(s); a = Pop(s); s.Add(a == b ? 1 : 0); break;
            case "ne": b = Pop(s); a = Pop(s); s.Add(a != b ? 1 : 0); break;
            case "gt": b = Pop(s); a = Pop(s); s.Add(a > b ? 1 : 0); break;
            case "ge": b = Pop(s); a = Pop(s); s.Add(a >= b ? 1 : 0); break;
            case "lt": b = Pop(s); a = Pop(s); s.Add(a < b ? 1 : 0); break;
            case "le": b = Pop(s); a = Pop(s); s.Add(a <= b ? 1 : 0); break;
            case "and": b = Pop(s); a = Pop(s); s.Add((long)a & (long)b); break;
            case "or": b = Pop(s); a = Pop(s); s.Add((long)a | (long)b); break;
            case "xor": b = Pop(s); a = Pop(s); s.Add((long)a ^ (long)b); break;
            case "not": a = Pop(s); s.Add(a == 0 ? 1 : (a == 1 ? 0 : ~(long)a)); break;
            case "bitshift": b = Pop(s); a = Pop(s); s.Add(b >= 0 ? (long)a << (int)b : (long)a >> (int)-b); break;
            case "true": s.Add(1); break;
            case "false": s.Add(0); break;
            case "pop": Pop(s); break;
            case "exch": b = Pop(s); a = Pop(s); s.Add(b); s.Add(a); break;
            case "dup": a = Pop(s); s.Add(a); s.Add(a); break;
            case "copy":
            {
                int n = (int)Pop(s);
                if (n < 0 || n > s.Count) throw new InvalidOperationException();
                s.AddRange(s.GetRange(s.Count - n, n));
                break;
            }
            case "index":
            {
                int n = (int)Pop(s);
                s.Add(s[s.Count - 1 - n]);
                break;
            }
            case "roll":
            {
                int j = (int)Pop(s), n = (int)Pop(s);
                if (n <= 0 || n > s.Count) break;
                var part = s.GetRange(s.Count - n, n);
                s.RemoveRange(s.Count - n, n);
                j = ((j % n) + n) % n;
                for (int i = 0; i < n; i++) s.Add(part[(i - j + n) % n]);
                break;
            }
        }
    }
}
