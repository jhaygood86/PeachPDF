using System.Text.RegularExpressions;

namespace PeachPDF.CSS
{
    internal sealed class RegexpFunction : DocumentFunction
    {
        private readonly Regex _regex;

        public RegexpFunction(string url) : base(FunctionNames.Regexp, url)
        {
            _regex = new Regex(url, RegexOptions.ECMAScript | RegexOptions.CultureInvariant);
        }

        public override bool Matches(Url url)
        {
            return _regex.IsMatch(url.Href);
        }
    }
}