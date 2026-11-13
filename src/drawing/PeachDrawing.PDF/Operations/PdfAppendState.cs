using System;
using System.Collections.Generic;

namespace PeachDrawing.PDF;

/// <summary>The names an output file has taken so far, shared by every operation added to one composer so that they never collide.</summary>
internal sealed class PdfAppendState
{
    public HashSet<string> DestinationNames { get; } = [with(StringComparer.Ordinal)];

    public HashSet<string> FieldNames { get; } = [with(StringComparer.Ordinal)];

    /// <summary>How many operations were added before this one; it keeps the generated link-target names of one operation from another's.</summary>
    public int Calls { get; set; }
}
