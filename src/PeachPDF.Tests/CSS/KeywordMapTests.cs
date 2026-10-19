using PeachBrackets.Css;

namespace PeachPDF.Tests.CSS
{
    public class KeywordMapTests
    {
        private static KeywordMap<TextTransform> Sample() =>
            new KeywordMap<TextTransform>(StringComparer.OrdinalIgnoreCase)
            {
                { "none", TextTransform.None },
                { "capitalize", TextTransform.Capitalize },
                { "uppercase", TextTransform.Uppercase },
            }.Freeze();

        [Fact]
        public void LooksKeywordsUp_IgnoringCase()
        {
            var map = Sample();

            Assert.True(map.TryGetValue("UPPERCASE", out var value));
            Assert.Equal(TextTransform.Uppercase, value);
            Assert.True(map.ContainsKey("None"));
            Assert.Equal(TextTransform.Capitalize, map["Capitalize"]);
            Assert.Equal(3, map.Count);
        }

        [Fact]
        public void AnUnknownKeyIsNotFound()
        {
            var map = Sample();

            Assert.False(map.TryGetValue("lowercase", out var value));
            Assert.Equal(default, value);
            Assert.False(map.ContainsKey("lowercase"));
            Assert.Throws<KeyNotFoundException>(() => map["lowercase"]);
        }

        [Fact]
        public void EnumeratesKeysAndValuesInTheOrderTheyWereAdded()
        {
            var map = Sample();

            Assert.Equal(["none", "capitalize", "uppercase"], map.Keys);
            Assert.Equal([TextTransform.None, TextTransform.Capitalize, TextTransform.Uppercase], map.Values);
            Assert.Equal(map.Keys.Zip(map.Values), map.Select(pair => (pair.Key, pair.Value)));
        }

        [Fact]
        public void GrowsPastItsInitialCapacity()
        {
            var map = new KeywordMap<int>(StringComparer.Ordinal);
            for (var i = 0; i < 40; i++) map.Add("k" + i, i * 3);
            map.Freeze();

            Assert.Equal(40, map.Count);
            Assert.Equal(117, map["k39"]);
            Assert.Equal(0, map["k0"]);
        }

        [Fact]
        public void ADuplicateKey_ThrowsWhenFrozen()
        {
            var map = new KeywordMap<int>(StringComparer.OrdinalIgnoreCase) { { "a", 1 }, { "A", 2 } };

            Assert.Throws<ArgumentException>(() => map.Freeze());
        }

        [Fact]
        public void AFrozenMapRejectsFurtherEntries()
        {
            var map = Sample();

            Assert.Throws<InvalidOperationException>(() => map.Add("lowercase", TextTransform.Lowercase));
        }

        [Fact]
        public void EveryDeclaredMapAnswersItsOwnKeywords()
        {
            // Map's fields are what the CSS grammar and the generated registry read; each must find every keyword it lists.
            foreach (var field in typeof(Map).GetFields().Where(f => f.FieldType.IsGenericType && f.FieldType.GetGenericTypeDefinition() == typeof(KeywordMap<>)))
            {
                var map = field.GetValue(null)!;
                var keys = (IEnumerable<string>)map.GetType().GetProperty("Keys")!.GetValue(map)!;
                var contains = map.GetType().GetMethod("ContainsKey")!;

                Assert.NotEmpty(keys);
                Assert.All(keys, key => Assert.True((bool)contains.Invoke(map, [key.ToUpperInvariant()])!, $"{field.Name}.{key}"));
            }
        }
    }
}
