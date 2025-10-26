namespace PeachPDF.CSS
{
    public interface IAttrSelector : ISelector
    {
        string Attribute { get; }
        string Value { get; }
    }
}