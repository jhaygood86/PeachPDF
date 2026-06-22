namespace PeachPDF.Html.Core.Entities
{
    internal class CssImage
    {
        protected CssImage()
        {

        }

        public string? Url { get; private set; }

        public CssImageKind Kind { get; private set; }

        internal enum CssImageKind
        {
            Url,
            Gradient,
            Element,
            Image,
            ImageFragment,
            SolidColor,
            CrossFade,
            ImageSet,
            Paint
        }

        public static CssImage GetUrl(string url)
        {
            CssImage image = new()
            {
                Kind = CssImageKind.Url,
                Url = url
            };

            return image;
        }
    }
}
