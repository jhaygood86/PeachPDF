using PeachPDF.Html.Adapters.Entities;

namespace PeachPDF.Html.Core.Entities
{
    internal sealed class ParsedLinearGradient
    {
        public double AngleRad { get; init; }
        public required (RColor Color, double? Position)[] Stops { get; init; }
        public bool IsRepeating { get; init; }
    }
}
