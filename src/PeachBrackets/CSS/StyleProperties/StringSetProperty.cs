namespace PeachPDF.CSS
{
    /// <summary>
    /// Represents the CSS string-set property from CSS Generated Content for Paged Media Module.
    /// Syntax: string-set: [ &lt;custom-ident&gt; &lt;content-list&gt; ]# | none
    /// where &lt;content-list&gt; = [ &lt;string&gt; | &lt;counter()&gt; | &lt;counters()&gt; | &lt;content()&gt; | &lt;attr()&gt; ]+
    /// </summary>
    internal sealed class StringSetProperty : Property
    {
        // Exposed for css-properties.json's "cssom-grammar" validator (ValidatorExpressionBuilder),
        // which calls this same real grammar directly instead of the full cssom PropertyFactory/
        // StylesheetParser round trip - see CLAUDE.md's "one parser" rule.
        internal static readonly IValueConverter ValueGrammar = new StringSetValueConverter();

        private static readonly IValueConverter ValueConverter = ValueGrammar.OrDefault();

        internal StringSetProperty() : base(PropertyNames.StringSet)
        {
        }

        internal override IValueConverter Converter => ValueConverter;
    }
}
