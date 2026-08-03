using System;
using System.Collections.Generic;
using PeachPDF.Html.Core.Dom;
using PeachPDF.Html.Core.Utils;

namespace PeachPDF.CSS
{
    /// <summary>
    /// A single <c>style(&lt;property&gt;: &lt;value&gt;)</c> leaf (CSS Containment 3 SS7.3): true when
    /// the candidate query container's own resolved value for the queried property equals the queried
    /// value. A custom property (<c>--foo</c>) is looked up in the container's own
    /// <see cref="CssBox.CustomProperties"/> dictionary (the same one <c>var()</c> resolution reads); a
    /// standard property goes through <see cref="CssUtils.GetPropertyValue"/> - the same registry-backed
    /// dispatch the cascade itself uses - to read the container's already-cascaded value.
    /// </summary>
    /// <remarks>
    /// Comparison is a trimmed, ordinal string match against the authored query value text, not full
    /// computed-value equivalence (e.g. <c>style(color: red)</c> won't match a container whose resolved
    /// <c>color</c> serializes as <c>rgb(255, 0, 0)</c>, since the two sides are never normalized through
    /// the same value pipeline before comparing) - a documented, narrower-than-spec simplification. It
    /// still gets the common cases right: any custom-property comparison (values are opaque, author-
    /// controlled text with no separate "computed" serialization to diverge from) and a standard-property
    /// comparison whose authored value already matches the engine's own canonical form (e.g. keyword
    /// values like <c>style(display: block)</c>).
    /// </remarks>
    internal sealed class StyleDeclarationCondition : IStyleQueryCondition
    {
        private readonly string _propertyName;
        private readonly string _value;

        internal StyleDeclarationCondition(string propertyName, string value)
        {
            _propertyName = propertyName;
            _value = value.Trim();
        }

        public bool Check(CssBox container)
        {
            var actual = _propertyName.StartsWith("--", StringComparison.Ordinal)
                ? container.CustomProperties?.GetValueOrDefault(_propertyName)
                : CssUtils.GetPropertyValue(container, _propertyName);

            return actual is not null && string.Equals(actual.Trim(), _value, StringComparison.Ordinal);
        }
    }
}
