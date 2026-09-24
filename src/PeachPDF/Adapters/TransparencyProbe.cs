using PeachPDF.Html.Adapters;
using PeachPDF.PdfSharpCore.Drawing;
using PeachPDF.PdfSharpCore.Pdf;
using System;

namespace PeachPDF.Adapters;

/// <summary>
/// Answers "would painting this need PDF transparency?" by painting it into a scratch page that is never saved, with the
/// transparency guard recording instead of throwing. It exists so the answer comes from the very code that decides what the writer
/// emits (<see cref="PeachPDF.PdfSharpCore.Pdf.Advanced.PdfATransparencyGuard"/> is the single choke point), rather than from a second
/// list of constructs that would drift from it.
/// </summary>
/// <remarks>
/// The scratch document has no conformance level of its own, so a colour or glyph rule of PDF/A or PDF/X cannot interrupt a probe
/// before it has seen everything; only the transparency question is asked. One scratch page serves many probes and is replaced once it
/// has collected enough content to matter.
/// </remarks>
internal sealed class TransparencyProbe : IDisposable
{
    private const int ProbesPerScratchDocument = 200;

    [ThreadStatic]
    private static TransparencyProbe? s_current;

    private readonly RAdapter _adapter;
    private readonly double _pixelsPerPoint;
    private PdfDocument? _document;
    private XGraphics? _xGraphics;
    private GraphicsAdapter? _graphics;
    private int _probes;

    internal TransparencyProbe(RAdapter adapter, double pixelsPerPoint)
    {
        _adapter = adapter;
        _pixelsPerPoint = pixelsPerPoint;
    }

    private bool Hit { get; set; }

    /// <summary>Called by the transparency guard: true while a probe is running (the construct is recorded and the guard must not throw).</summary>
    internal static bool Observe()
    {
        if (s_current is not { } probe)
            return false;

        probe.Hit = true;
        return true;
    }

    /// <summary>Whether <paramref name="paint"/> needs transparency. Anything else that goes wrong while painting is ignored: the real paint reports it.</summary>
    public bool Requires(Action<RGraphics> paint)
    {
        if (_graphics is null || _probes++ >= ProbesPerScratchDocument)
            Reset();

        Hit = false;
        var outer = s_current;
        s_current = this;
        try
        {
            paint(_graphics!);
        }
        catch (Exception)
        {
            // Inconclusive: a font or image problem the real paint will hit again and report properly.
        }
        finally
        {
            s_current = outer;
        }

        return Hit;
    }

    private void Reset()
    {
        DisposeScratch();
        _document = new PdfDocument();
        var page = _document.AddPage();
        _xGraphics = XGraphics.FromPdfPage(page);
        _graphics = new GraphicsAdapter(_adapter, _xGraphics, _pixelsPerPoint);
        _probes = 1;
    }

    private void DisposeScratch()
    {
        _graphics?.Dispose();
        _xGraphics?.Dispose();
        _document?.Dispose();
        _graphics = null;
        _xGraphics = null;
        _document = null;
    }

    public void Dispose() => DisposeScratch();
}
