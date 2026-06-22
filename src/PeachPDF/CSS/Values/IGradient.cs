using System.Collections.Generic;

namespace PeachPDF.CSS
{
    internal interface IGradient : IImageSource
    {
        IEnumerable<GradientStop> Stops { get; }
        bool IsRepeating { get; }
    }
}