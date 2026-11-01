using System.Collections.Generic;

namespace PeachBrackets.Css
{
    internal interface IRuleList : IEnumerable<IRule>
    {
        IRule this[int index] { get; }
        int Length { get; }
    }
}