namespace PeachPDF.Html.Core.Entities
{
    internal record NamedPageElement(string Name, double Y)
    {
        public double Y { get; set; } = Y;
    }
}
