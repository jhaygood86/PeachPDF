namespace PeachBrackets.Css
{
    internal interface IAttrSelector : ISelector
    {
        string Attribute { get; }
        string Value { get; }
        AttrCaseSensitivity CaseSensitivity { get; }
    }
}
