using System;

namespace PeachPDF.Tests.TestSupport
{
    /// <summary>
    /// A real, small (32x32) Adobe-authored direct-CMYK JPEG (Adobe APP14 transform=0, so Adobe-inverted
    /// per PeachImage's <c>ColorSpaceResolver</c> convention), copied byte-for-byte from PeachImage's own
    /// test corpus (<c>tests/corpus/image-rs-jpeg-decoder/tests/reftest/images/mozilla/jpg-cmyk-1.jpg</c>)
    /// - same "reuse a known-good real file" approach <c>PeachImageSourceTests</c> already takes for
    /// WebP/AVIF, since PeachImage has no CMYK JPEG encoder to synthesize one with. Carries no embedded
    /// ICC profile - see <see cref="IccProfileFixture"/> for splicing one in.
    /// </summary>
    internal static class CmykJpegFixture
    {
        internal const int Width = 32;
        internal const int Height = 32;

        private const string NoIccBase64 =
            "/9j/4AAQSkZJRgABAQEASABIAAD/7gAOQWRvYmUAZAAAAAAA/9sAQwABAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB" +
            "AQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEBAQEB/8AAFAgAIAAgBEMRAE0RAFkRAEsRAP/EABkAAAIDAQAAAAAAAAAA" +
            "AAAAAAAKBwkLCP/EAEoQAAECAgYDCwUNBwUAAAAAAAECAwQFAAYHERIhCBUxCRMUIiMkMjNBUXIYQ2FigUJERWRlcZOxssHC" +
            "4vAlNHODkaGjUrPR4fH/2gAOBEMATQBZAEsAAD8AedtQszgK7yqJKGEawDS7gEp5xkfR1v2/H0nnbM7UJVXeAYQYloTDAkAY" +
            "xzjijLb1v2/H0s/+jtNpdpczqTM4jnC+A41HNSuQ4x9PV/Y8PRoT0qrBomB1lfBKGHfs96I79mX1dmdJboUijyl/lBP0v5qL" +
            "UaVVm7sDrK+HKcO++5uIGfs7ProUKHlL/KCfpfzUWq0qpSYHWV6cN2+7b9ueff37fvoUmmyWyCeWizWGDUG8ZeXUZhtXOMxm" +
            "Muq+bp+DNR5S/wAoJ+l/NTbzotxYNpVcBiYI6yw3Kbv5buIy2+zu76QtQ0l/hDwu/ipAVt9jEstKkMaWYZsTYMOEJCBzq5J2" +
            "Zdfls872cp1l5VjFt8htKlkMyY1gTYNoCQXEjhWQy2jl+4ed2dZ1hSke0u0uZVJmT/LuGBLi/dnkM/8Ab+x4eiqPp5aN8fIt" +
            "c45etvBwja0Rsxei7/r+lJ9p01o+aONbLZp9BJhZZFLlKn2wFBlZ4XxhmOL1HcfO/wAPpxT5Svx7/LRK/Tyqm7Itc42ijBwi" +
            "+9N2zF3/AK/tcUbT0DdynrHPdTb3ViIXj4PmINRJvw+p/wAd/ZQ8pX49/lptC0zYbN9KrgLsOdZYcJT53IEe0dn/ALRJKjB2" +
            "kv8ACHhd/FQpbTo36eWoo+Xr1zg3tbR/eLthHrX7PZ/anTWjjo+T62atkshUwT6pSuKZSoBtXO71pyOXUd487/D6a+Okt7+/" +
            "nffSundDbPLNYiyCe13rQtqXx0I04wlLbbRcmjy2HVpASpbZS4jAN/e4ycKkqUnfSA8w/Z5uhtkERZqutFd561CR0vabbSll" +
            "xhb00cLS1JASt1GBxJb5Z+5ScKkqUku3B7RV3HrceqhzSocurvXeXCWQcsEGlKUwba3Jg4tsrS00lZbCVJDfKu8dCELSpSSs" +
            "oQ7TRWGsNaWa06lku+PpfcWQCtYTDJCwCSQFXpOLiIyN4IBw3lGXrup1YZRGzqscHJYduHg0vRaEAELcUkKWAVuAC9V23ClC" +
            "fVvpXHpI7s3J4dEwlNQ4qGk0EA60l5mISuOeQd8SC7FXpUMSFYVpZSy0sAEtnbRr6zmyCzyyiVMSmpFW4CUtMNBrhQZbcj3U" +
            "pCk3uRRQlScSVYVJZSy2oAYkEi+nadhNitYK0RUHFTpURFrWpte9qCgwgkpOTeYJBF6SsqUDfcodmxBTM+lOlVwEpOsrsOfW" +
            "57e3Pvz2/wBaYXdkFks1tFnkG0IZ0y8vNg8Q844wyOXVdnr92DpNpaS/wh4XfxUKTdVPTy1E60vXODAUm/hF2zP/AFfr6nBN" +
            "yn0Dde1jqw3qbHjiIMHm995Kker+rs7qL46S3v7+d99Fzd2b0kUQ8niqhymYAQUmhohh5LTt6Ho5aTwp0hLikKuUlLKVpuC2" +
            "mW1EC80mWsO6nTqNlEPJYOsbyYOHbICERaglTiwA4sgLuxG5Kb8uKhPbTRAsgs5lVlFnlW6kSlhphqUwDIit6ASl2PcbQYpw" +
            "4VKSrCpKWUqTcFNsoVcCTTjuxWwmKrRWBU6ioNS1xcQFN42yShgK5MZpBBIJWUnMKUodmWb3pyVhXOZnOXFOY8bj5vvvvvKr" +
            "8/b6b/nzpzTWHTkmc5W4pycuLx333vk337c8R/R7TfSS6X2aNOjTfq/9n5cl5rbszOWz6/m2/wD/2Q==";

        internal static byte[] NoIccBytes => Convert.FromBase64String(NoIccBase64);
    }
}
