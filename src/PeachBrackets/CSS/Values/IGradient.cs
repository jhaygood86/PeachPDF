using System.Collections.Generic;

namespace PeachBrackets.Css
{
    internal interface IGradient : IImageSource
    {
        IEnumerable<GradientStop> Stops { get; }
        bool IsRepeating { get; }
    }
}