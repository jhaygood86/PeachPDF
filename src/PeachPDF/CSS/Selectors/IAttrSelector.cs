namespace PeachPDF.CSS
{
    internal interface IAttrSelector : ISelector
    {
        string Attribute { get; }
        string Value { get; }
    }
}