#nullable disable

using System.Collections.Generic;
using System.Linq;

namespace PeachPDF.CSS
{
    internal sealed class UnorderedOptionsConverter(params IValueConverter[] converters) : IValueConverter
    {
        public IPropertyValue Convert(IEnumerable<Token> value)
        {
            var perms = GetPermutations(converters).ToList();
            
            foreach (var perm in perms)
            {
                var list = new List<Token>(value);
                var options = new IPropertyValue[perm.Length];
                var success = TryOrder(perm, list, options);
                if (success && list.Count == 0)
                {
                    return new OptionsValue(options, value);
                }
            }

            // If none work, lenient with original order
            var list2 = new List<Token>(value);
            var options2 = new IPropertyValue[converters.Length];
            TryOrder(converters, list2, options2);
            return new OptionsValue(options2, value);
        }

        private static bool TryOrder(IValueConverter[] converters, List<Token> list, IPropertyValue[] options)
        {
            for (var i = 0; i < converters.Length; i++)
            {
                options[i] = converters[i].VaryAll(list);
                if (options[i] == null) return false;
            }
            return true;
        }

        private static IEnumerable<IValueConverter[]> GetPermutations(IValueConverter[] converters)
        {
            var list = new List<IValueConverter>(converters);
            return Permute(list, 0).Select(l => l.ToArray());
        }

        private static IEnumerable<List<IValueConverter>> Permute(List<IValueConverter> list, int start)
        {
            if (start == list.Count - 1)
            {
                yield return [..list];
            }
            else
            {
                for (var i = start; i < list.Count; i++)
                {
                    Swap(list, start, i);
                    foreach (var perm in Permute(list, start + 1))
                    {
                        yield return perm;
                    }
                    Swap(list, start, i); // backtrack
                }
            }
        }

        private static void Swap<T>(List<T> list, int i, int j)
        {
            (list[i], list[j]) = (list[j], list[i]);
        }

        public IPropertyValue Construct(Property[] properties)
        {
            var result = properties.Guard<OptionsValue>();

            if (result != null) return result;

            var values = new IPropertyValue[converters.Length];

            for (var i = 0; i < converters.Length; i++)
            {
                var value = converters[i].Construct(properties);

                if (value == null) return null;

                values[i] = value;
            }

            result = new OptionsValue(values, []);

            return result;
        }

        private sealed class OptionsValue(IPropertyValue[] options, IEnumerable<Token> tokens) : IPropertyValue
        {
            public string CssText
            {
                get
                {
                    return string.Join(" ",
                        options.Where(m => !string.IsNullOrEmpty(m.CssText)).Select(m => m.CssText));
                }
            }

            public TokenValue Original { get; } = new TokenValue(tokens);

            public TokenValue ExtractFor(string name)
            {
                var tokens = new List<Token>();

                foreach (var option in options)
                {
                    var extracted = option.ExtractFor(name);

                    if (extracted is not { Count: > 0 }) continue;
                    if (tokens.Count > 0) tokens.Add(Token.Whitespace);
                    tokens.AddRange(extracted);
                }

                return new TokenValue(tokens);
            }
        }
    }
}